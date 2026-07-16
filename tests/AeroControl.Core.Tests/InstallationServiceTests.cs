using AeroControl.Setup;
using Microsoft.Win32;
using SetupApp = AeroControl.Setup.App;

namespace AeroControl.Core.Tests;

public sealed class InstallationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AeroControl.Setup.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SetupLaunchOptions_ParseCaptureAndRejectUnknownOptions()
    {
        var options = SetupLaunchOptions.Parse(["--capture=C:\\setup.png"]);

        Assert.Equal("C:\\setup.png", options.CapturePath);
        Assert.Equal(SetupAction.Interactive, options.Action);
        Assert.Throws<ArgumentException>(() => SetupLaunchOptions.Parse(["--capture"]));
        Assert.Throws<ArgumentException>(() => SetupLaunchOptions.Parse(["--unknown"]));
    }

    [Fact]
    public void SetupLaunchOptions_ParseSilentActionsStrictly()
    {
        var install = SetupLaunchOptions.Parse(["--install", "--startup"]);
        var uninstall = SetupLaunchOptions.Parse(["--uninstall"]);

        Assert.Equal(SetupAction.Install, install.Action);
        Assert.True(install.StartWithWindows);
        Assert.Equal(SetupAction.Uninstall, uninstall.Action);
        Assert.Throws<ArgumentException>(() => SetupLaunchOptions.Parse(["--install", "--uninstall"]));
        Assert.Throws<ArgumentException>(() => SetupLaunchOptions.Parse(["--startup"]));
        Assert.Throws<ArgumentException>(() => SetupLaunchOptions.Parse(["--install", "--capture", "x.png"]));
    }

    [Fact]
    public void SetupRootOverride_RejectsArbitraryExistingDirectory()
    {
        var arbitrary = Path.Combine(_root, "NotAeroControl");
        Directory.CreateDirectory(arbitrary);
        File.WriteAllText(Path.Combine(arbitrary, "private.txt"), "private");

        Assert.False(SetupApp.TryResolveInstallRoot(arbitrary, out _, out _));

        var owned = Path.Combine(_root, "AeroControl");
        Directory.CreateDirectory(owned);
        File.WriteAllText(Path.Combine(owned, InstallationService.OwnershipMarker), "owned");
        Assert.True(SetupApp.TryResolveInstallRoot(owned, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(owned), resolved);
    }

    [Fact]
    public void SetupRootOverride_RejectsReparseAncestor()
    {
        var ancestor = Path.Combine(_root, "junction");
        Directory.CreateDirectory(ancestor);
        var candidate = Path.Combine(ancestor, "AeroControl");

        Assert.False(SetupApp.TryResolveInstallRoot(
            candidate,
            out _,
            out _,
            path => string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Install_CopiesOnlyPayloadAndConfiguresStartup()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);

        var result = installer.Install(source, true);

        Assert.True(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(destination, "DISCLAIMER.md")));
        Assert.True(File.Exists(Path.Combine(destination, InstallationService.OwnershipMarker)));
        Assert.Equal($"\"{Path.Combine(destination, "AeroControl.exe")}\"", startup.Value);
    }

    [Fact]
    public void Install_RejectsReparseRootWithoutMutatingFilesOrStartup()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "AeroControl.exe"), "existing-app");
        var startup = new FakeSetupStartupStore();
        startup.Write(new SetupStartupRegistryValue("foreign-command", RegistryValueKind.ExpandString));
        var installer = new InstallationService(
            startup,
            destination,
            () => false,
            isReparsePoint: path => string.Equals(path, destination, StringComparison.OrdinalIgnoreCase));

        var result = installer.Install(source, true);

        Assert.False(result.Succeeded);
        Assert.Equal("existing-app", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal("foreign-command", startup.RegistryValue?.Value);
        Assert.Equal(RegistryValueKind.ExpandString, startup.RegistryValue?.Kind);
    }

    [Fact]
    public void Install_UpdatesExistingPayloadWithoutLeavingTemporaryFiles()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var installer = new InstallationService(new FakeSetupStartupStore(), destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v2");

        var result = installer.Install(source, false);

        Assert.True(result.Succeeded);
        Assert.Equal("app-v2", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Empty(Directory.GetDirectories(_root, "*.staging-*", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetDirectories(_root, "*.backup-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Install_PreservesUserSettingsAcrossTransactionalUpdate()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var installer = new InstallationService(new FakeSetupStartupStore(), destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        File.WriteAllText(Path.Combine(destination, "settings.json"), "user-settings");
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v2");

        Assert.True(installer.Install(source, false).Succeeded);

        Assert.Equal("app-v2", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal("user-settings", File.ReadAllText(Path.Combine(destination, "settings.json")));
    }

    [Fact]
    public void Install_StagingFailureLeavesExistingBundleUntouched()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var installer = new InstallationService(new FakeSetupStartupStore(), destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        var settingsPath = Path.Combine(destination, "settings.json");
        File.WriteAllText(settingsPath, "locked-settings");
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v2");
        using var lockFile = new FileStream(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = installer.Install(source, false);

        Assert.False(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
    }

    [Fact]
    public void Install_StartupFailureRollsBackBundleAndExactStartupCommand()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v2");
        const string previousCommand = "\"C:\\Portable\\AeroControl.exe\"";
        startup.Write(previousCommand);
        startup.ThrowAfterWriteOnce = true;

        var result = installer.Install(source, true);

        Assert.False(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal(previousCommand, startup.Value);
        Assert.Empty(Directory.GetDirectories(_root, "*.staging-*", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetDirectories(_root, "*.backup-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Install_BackupCleanupFailureDoesNotReportSuccess()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v2");
        installer = new InstallationService(
            startup,
            destination,
            () => false,
            deleteDirectory: path => !path.Contains(".backup-", StringComparison.OrdinalIgnoreCase));

        var result = installer.Install(source, false);

        Assert.False(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Empty(Directory.GetDirectories(_root, "*.backup-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Install_RefusesMissingPayloadAndRunningApp()
    {
        var source = CreatePayload();
        File.Delete(Path.Combine(source, "DISCLAIMER.md"));
        var destination = Path.Combine(_root, "installed");
        var installer = new InstallationService(new FakeSetupStartupStore(), destination, () => false);

        Assert.False(installer.Install(source, false).Succeeded);
        Assert.False(Directory.Exists(destination));

        source = CreatePayload("second-source");
        installer = new InstallationService(new FakeSetupStartupStore(), destination, () => true);
        Assert.False(installer.Install(source, false).Succeeded);
    }

    [Fact]
    public void Uninstall_RemovesPayloadAndStartupValue()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, true).Succeeded);

        var result = installer.Uninstall();

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(destination));
        Assert.Null(startup.Value);
    }

    [Fact]
    public void Uninstall_RejectsReparseRootWithoutMutatingFilesOrStartup()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, true).Succeeded);
        var startupBefore = startup.RegistryValue;
        installer = new InstallationService(
            startup,
            destination,
            () => false,
            isReparsePoint: path => string.Equals(path, destination, StringComparison.OrdinalIgnoreCase));

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.True(File.Exists(Path.Combine(destination, InstallationService.OwnershipMarker)));
        Assert.Equal(startupBefore, startup.RegistryValue);
    }

    [Fact]
    public void Uninstall_PreservesStartupValueOwnedByAnotherCopy()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        startup.Write("\"C:\\Portable\\AeroControl.exe\"");

        var result = installer.Uninstall();

        Assert.True(result.Succeeded);
        Assert.Equal("\"C:\\Portable\\AeroControl.exe\"", startup.Value);
        Assert.False(File.Exists(Path.Combine(destination, "AeroControl.exe")));
    }

    [Fact]
    public void Uninstall_LeavesPayloadWhenOwnedStartupRemovalFails()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, true).Succeeded);
        startup.ThrowOnDelete = true;

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(destination, "AeroControl.exe")));
        Assert.NotNull(startup.Value);
    }

    [Fact]
    public void Uninstall_PostDeleteReadFailureRestoresExactStartupValue()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        var ownedCommand = $"\"{Path.Combine(destination, "AeroControl.exe")}\"";
        startup.Write(new SetupStartupRegistryValue(ownedCommand, RegistryValueKind.String));
        startup.ThrowOnReadAfterDeleteOnce = true;

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal(ownedCommand, startup.RegistryValue?.Value);
        Assert.Equal(RegistryValueKind.String, startup.RegistryValue?.Kind);
    }

    [Fact]
    public void Uninstall_CleanupFailureDoesNotReportSuccess()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, true).Succeeded);
        installer = new InstallationService(
            startup,
            destination,
            () => false,
            deleteDirectory: _ => false);

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(destination, "AeroControl.exe")));
        Assert.NotNull(startup.RegistryValue);
        Assert.Empty(Directory.GetDirectories(_root, "*.remove-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Uninstall_RefusesMarkerlessDirectoryWithoutMutatingStartup()
    {
        var destination = Path.Combine(_root, "installed");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "AeroControl.exe"), "foreign-app");
        var startup = new FakeSetupStartupStore();
        startup.Write("\"C:\\Foreign\\AeroControl.exe\"");
        var installer = new InstallationService(startup, destination, () => false);

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal("\"C:\\Foreign\\AeroControl.exe\"", startup.Value);
    }

    [Fact]
    public void Uninstall_PreservesExpandableAndNonStringStartupValues()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        startup.Write(new SetupStartupRegistryValue(
            "\"%LOCALAPPDATA%\\Other\\AeroControl.exe\"",
            RegistryValueKind.ExpandString));

        Assert.True(installer.Uninstall().Succeeded);
        Assert.Equal(RegistryValueKind.ExpandString, startup.RegistryValue?.Kind);
        Assert.Equal("\"%LOCALAPPDATA%\\Other\\AeroControl.exe\"", startup.RegistryValue?.Value);
    }

    [Fact]
    public void Uninstall_PreservationFailureRestoresBundleAndExactStartupValue()
    {
        var source = CreatePayload();
        var destination = Path.Combine(_root, "installed");
        var startup = new FakeSetupStartupStore();
        var installer = new InstallationService(startup, destination, () => false);
        Assert.True(installer.Install(source, false).Succeeded);
        File.WriteAllText(Path.Combine(destination, "settings.json"), "user-settings");
        var ownedCommand = $"\"{Path.Combine(destination, "AeroControl.exe")}\"";
        startup.Write(new SetupStartupRegistryValue(ownedCommand, RegistryValueKind.ExpandString));
        installer = new InstallationService(
            startup,
            destination,
            () => false,
            (_, _) => throw new IOException("Injected preserved-file copy failure."));

        var result = installer.Uninstall();

        Assert.False(result.Succeeded);
        Assert.Equal("app-v1", File.ReadAllText(Path.Combine(destination, "AeroControl.exe")));
        Assert.Equal("user-settings", File.ReadAllText(Path.Combine(destination, "settings.json")));
        Assert.True(File.Exists(Path.Combine(destination, InstallationService.OwnershipMarker)));
        Assert.Equal(ownedCommand, startup.RegistryValue?.Value);
        Assert.Equal(RegistryValueKind.ExpandString, startup.RegistryValue?.Kind);
        Assert.Empty(Directory.GetDirectories(_root, "*.remove-*", SearchOption.TopDirectoryOnly));
    }

    private string CreatePayload(string name = "source")
    {
        var source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "AeroControl.exe"), "app-v1");
        File.WriteAllText(Path.Combine(source, "LICENSE"), "license");
        File.WriteAllText(Path.Combine(source, "DISCLAIMER.md"), "disclaimer");
        return source;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class FakeSetupStartupStore : ISetupStartupStore
    {
        public string? Value => RegistryValue?.Value as string;

        public SetupStartupRegistryValue? RegistryValue { get; private set; }

        public bool ThrowOnDelete { get; set; }

        public bool ThrowAfterWriteOnce { get; set; }

        public bool ThrowOnReadAfterDeleteOnce { get; set; }

        private bool ReadShouldThrow { get; set; }

        public SetupStartupRegistryValue? Read()
        {
            if (ReadShouldThrow)
            {
                ReadShouldThrow = false;
                throw new IOException("Injected post-delete verification failure.");
            }

            return RegistryValue;
        }

        public void Write(string value)
        {
            Write(new SetupStartupRegistryValue(value, RegistryValueKind.String));
        }

        public void Write(SetupStartupRegistryValue value)
        {
            RegistryValue = value;
            if (ThrowAfterWriteOnce)
            {
                ThrowAfterWriteOnce = false;
                throw new UnauthorizedAccessException("Injected startup verification failure.");
            }
        }

        public void Delete()
        {
            if (ThrowOnDelete)
            {
                throw new UnauthorizedAccessException("Injected startup removal failure.");
            }

            RegistryValue = null;
            ReadShouldThrow = ThrowOnReadAfterDeleteOnce;
            ThrowOnReadAfterDeleteOnce = false;
        }
    }
}
