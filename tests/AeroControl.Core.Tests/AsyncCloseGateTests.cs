using AeroControl.Services;

namespace AeroControl.Core.Tests;

public sealed class AsyncCloseGateTests
{
    [Fact]
    public async Task TryBegin_AllowsOnlyOneInFlightShutdown()
    {
        var gate = new AsyncCloseGate();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;

        Assert.True(gate.TryBegin(
            () =>
            {
                Interlocked.Increment(ref starts);
                return completion.Task;
            },
            out var first));
        Assert.False(gate.TryBegin(
            () =>
            {
                Interlocked.Increment(ref starts);
                return Task.FromResult(true);
            },
            out var repeated));

        Assert.Same(first, repeated);
        Assert.Equal(1, starts);
        completion.SetResult(true);
        Assert.True(await first);
    }

    [Fact]
    public void Reset_AllowsRetryAfterCanceledShutdown()
    {
        var gate = new AsyncCloseGate();
        Assert.True(gate.TryBegin(
            () => Task.FromResult(false),
            out var first));

        gate.Reset(first);

        Assert.True(gate.TryBegin(
            () => Task.FromResult(true),
            out _));
    }
}
