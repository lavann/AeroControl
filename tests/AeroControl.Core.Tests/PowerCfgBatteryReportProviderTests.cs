using System.ComponentModel;
using System.Diagnostics;
using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class PowerCfgBatteryReportProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "AeroControl.PowerCfg.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Timeout_KillsReapsDrainsAndDeletesReport()
    {
        var factory = new FakeProcessFactory();
        var provider = CreateProvider(factory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetReportAsync());

        Assert.Equal(1, factory.Process.KillCalls);
        Assert.True(factory.Process.WaitCalls >= 2);
        Assert.True(factory.Process.HasExited);
        Assert.True(factory.Process.Disposed);
        Assert.True(factory.Process.StandardOutput.IsCompletedSuccessfully);
        Assert.True(factory.Process.StandardError.IsCompletedSuccessfully);
        Assert.False(File.Exists(factory.ReportPath));
    }

    [Fact]
    public async Task KillRace_StillReapsDrainsAndDeletesReport()
    {
        var factory = new FakeProcessFactory
        {
            ThrowOnFirstKill = true,
            ExitWhenKillThrows = true
        };
        var provider = CreateProvider(factory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetReportAsync());

        Assert.Equal(1, factory.Process.KillCalls);
        Assert.True(factory.Process.HasExited);
        Assert.True(factory.Process.Disposed);
        Assert.False(File.Exists(factory.ReportPath));
    }

    [Fact]
    public async Task UnreapableChild_FailsWithinBoundAndSanitizesReport()
    {
        var factory = new FakeProcessFactory
        {
            ThrowOnEveryKill = true
        };
        var provider = CreateProvider(factory);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => provider.GetReportAsync());

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains("could not terminate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(factory.Process.Disposed);
        Assert.False(File.Exists(factory.ReportPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }

        GC.SuppressFinalize(this);
    }

    private PowerCfgBatteryReportProvider CreateProvider(FakeProcessFactory factory) => new(
        factory,
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        _directory);

    private sealed class FakeProcessFactory : IBatteryReportProcessFactory
    {
        public FakeProcess Process { get; private set; } = new();

        public string ReportPath { get; private set; } = string.Empty;

        public bool ThrowOnFirstKill { get; init; }

        public bool ThrowOnEveryKill { get; init; }

        public bool ExitWhenKillThrows { get; init; }

        public IBatteryReportProcess Start(string reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, "<BatteryReport><ComputerName>PRIVATE</ComputerName></BatteryReport>");
            ReportPath = reportPath;
            Process = new FakeProcess
            {
                ThrowOnFirstKill = ThrowOnFirstKill,
                ThrowOnEveryKill = ThrowOnEveryKill,
                ExitWhenKillThrows = ExitWhenKillThrows
            };
            return Process;
        }
    }

    private sealed class FakeProcess : IBatteryReportProcess
    {
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _standardOutput = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _standardError = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowOnFirstKill { get; init; }

        public bool ThrowOnEveryKill { get; init; }

        public bool ExitWhenKillThrows { get; init; }

        public int KillCalls { get; private set; }

        public int WaitCalls { get; private set; }

        public bool Disposed { get; private set; }

        public Task<string> StandardOutput => _standardOutput.Task;

        public Task<string> StandardError => _standardError.Task;

        public int ExitCode => 0;

        public bool HasExited => _exit.Task.IsCompleted;

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCalls++;
            return _exit.Task.WaitAsync(cancellationToken);
        }

        public void Kill()
        {
            KillCalls++;
            if (ThrowOnEveryKill || (ThrowOnFirstKill && KillCalls == 1))
            {
                if (ExitWhenKillThrows)
                {
                    CompleteExit();
                }

                throw new Win32Exception("Injected kill failure.");
            }

            CompleteExit();
        }

        public void Dispose()
        {
            Disposed = true;
            _standardOutput.TrySetResult(string.Empty);
            _standardError.TrySetResult(string.Empty);
            GC.SuppressFinalize(this);
        }

        private void CompleteExit()
        {
            _standardOutput.TrySetResult(string.Empty);
            _standardError.TrySetResult(string.Empty);
            _exit.TrySetResult();
        }
    }
}
