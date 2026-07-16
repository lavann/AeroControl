using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;
using AeroControl.Core.Services;
using AeroControl.ViewModels;

namespace AeroControl.Core.Tests;

public sealed class MainViewModelBatteryIsolationTests
{
    [Fact]
    public async Task Initialize_BatteryFailureDoesNotSuppressCoolingSnapshot()
    {
        using var viewModel = new MainViewModel(
            new DemoHardwareService(),
            new ThrowingBatteryService(),
            true,
            false);

        await viewModel.InitializeAsync();

        Assert.Equal("4,637 RPM", viewModel.Fan1Speed);
        Assert.Equal("4,597 RPM", viewModel.Fan2Speed);
        Assert.Equal("Battery unavailable", viewModel.BatteryConnectionState);
        Assert.Contains("Injected battery failure", viewModel.BatteryStatusMessage);
    }

    [Fact]
    public async Task FanCommand_RemainsSuccessfulWhenBatteryRefreshFails()
    {
        using var viewModel = new MainViewModel(
            new DemoHardwareService(),
            new ThrowingBatteryService(),
            true,
            false);
        await viewModel.InitializeAsync();

        var result = await viewModel.SetFanPercentAsync(100);

        Assert.True(result.Succeeded);
        Assert.True(result.RequiresAutomaticRestore);
        Assert.Equal("5,404 RPM", viewModel.Fan1Speed);
        Assert.Equal("Battery unavailable", viewModel.BatteryConnectionState);
    }

    [Fact]
    public async Task FanCommand_DoesNotWaitForBatteryRefresh()
    {
        var battery = new BlockingBatteryService();
        using var viewModel = new MainViewModel(
            new DemoHardwareService(),
            battery,
            true,
            false);

        var result = await viewModel.SetFanPercentAsync(100).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(result.Succeeded);
        Assert.Equal("5,404 RPM", viewModel.Fan1Speed);
        Assert.Equal(0, battery.CallCount);
    }

    [Fact]
    public async Task InFlightCombinedRefresh_CannotOverwritePostCommandCoolingState()
    {
        var hardware = new SequencedHardwareService();
        var battery = new ControlledBatteryService();
        using var viewModel = new MainViewModel(hardware, battery, true, false);

        var combinedRefresh = viewModel.RefreshAsync();
        var command = viewModel.SetFanPercentAsync(100);
        hardware.FirstSnapshot.SetResult(CreateHardwareSnapshot(
            FanControlMode.Automatic,
            3000,
            2950,
            null));

        var result = await command.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(result.Succeeded);
        Assert.Equal("5,404 RPM", viewModel.Fan1Speed);
        Assert.Equal("FIXED", viewModel.FanMode);

        battery.Complete(BatterySnapshot.Unavailable("No battery."));
        await combinedRefresh.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("5,404 RPM", viewModel.Fan1Speed);
        Assert.Equal("FIXED", viewModel.FanMode);
    }

    private sealed class ThrowingBatteryService : IBatteryService
    {
        public Task<BatterySnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<BatterySnapshot>(
                new InvalidOperationException("Injected battery failure."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingBatteryService : IBatteryService
    {
        public int CallCount { get; private set; }

        public Task<BatterySnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ContinueWith(
                    _ => BatterySnapshot.Unavailable("Canceled."),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlledBatteryService : IBatteryService
    {
        private readonly TaskCompletionSource<BatterySnapshot> _snapshot = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BatterySnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            _snapshot.Task.WaitAsync(cancellationToken);

        public void Complete(BatterySnapshot snapshot)
        {
            _snapshot.TrySetResult(snapshot);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SequencedHardwareService : IAeroHardwareService
    {
        private bool _fixed;
        private int _snapshotCalls;

        public TaskCompletionSource<HardwareSnapshot> FirstSnapshot { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DeviceIdentity> GetDeviceIdentityAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HardwareCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HardwareSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _snapshotCalls) == 1)
            {
                return FirstSnapshot.Task.WaitAsync(cancellationToken);
            }

            return Task.FromResult(CreateHardwareSnapshot(
                _fixed ? FanControlMode.Fixed : FanControlMode.Automatic,
                _fixed ? 5404 : 3000,
                _fixed ? 5310 : 2950,
                _fixed ? 100 : null));
        }

        public Task<ControlResult> SetFixedFanPercentAsync(
            int percent,
            CancellationToken cancellationToken = default)
        {
            _fixed = true;
            return Task.FromResult(new ControlResult(
                true,
                "Fixed mode set.",
                percent,
                percent,
                true));
        }

        public Task<ControlResult> RestoreAutomaticFanControlAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static HardwareSnapshot CreateHardwareSnapshot(
        FanControlMode mode,
        int fan1Rpm,
        int fan2Rpm,
        int? fixedPercent) => new(
            DateTimeOffset.Now,
            70,
            60,
            fan1Rpm,
            fan2Rpm,
            fixedPercent,
            fixedPercent,
            true,
            mode,
            fixedPercent,
            true,
            null);
}
