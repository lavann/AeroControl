using AeroControl.Core.Models;
using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class AlertEvaluatorTests
{
    [Fact]
    public void Evaluate_RequiresThreeConsecutiveHotSamples()
    {
        var evaluator = new AlertEvaluator();
        var thresholds = new AlertThresholds(90, 85, true);
        var now = DateTimeOffset.UtcNow;

        Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now));
        Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(2)));
        var alert = Assert.Single(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(4)));

        Assert.Equal("cpu-hot", alert.Key);
    }

    [Fact]
    public void Evaluate_NormalSampleResetsConsecutiveCounter()
    {
        var evaluator = new AlertEvaluator();
        var thresholds = new AlertThresholds(90, 85, true);
        var now = DateTimeOffset.UtcNow;
        evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now);
        evaluator.Evaluate(Snapshot(cpu: 70), thresholds, now.AddSeconds(2));

        Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(4)));
        Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(6)));
        Assert.Single(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(8)));
    }

    [Fact]
    public void Evaluate_FanStallRequiresFixedSubstantialDuty()
    {
        var evaluator = new AlertEvaluator();
        var thresholds = new AlertThresholds(90, 85, true);
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < 3; index++)
        {
            Assert.Empty(evaluator.Evaluate(
                Snapshot(fan1: 0, mode: FanControlMode.Automatic, fixedPercent: null),
                thresholds,
                now.AddSeconds(index * 2)));
        }

        evaluator.Evaluate(Snapshot(fan1: 0), thresholds, now.AddSeconds(10));
        evaluator.Evaluate(Snapshot(fan1: 0), thresholds, now.AddSeconds(12));
        var alert = Assert.Single(evaluator.Evaluate(Snapshot(fan1: 0), thresholds, now.AddSeconds(14)));
        Assert.Equal("fan1-stall", alert.Key);
    }

    [Fact]
    public void Evaluate_EnforcesCooldown()
    {
        var evaluator = new AlertEvaluator();
        var thresholds = new AlertThresholds(90, 85, true);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 3; index++)
        {
            evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(index));
        }

        for (var index = 0; index < 6; index++)
        {
            Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddMinutes(1).AddSeconds(index)));
        }
    }

    [Fact]
    public void Reset_DiscardsPartialAbnormalSequence()
    {
        var evaluator = new AlertEvaluator();
        var thresholds = new AlertThresholds(90, 85, true);
        var now = DateTimeOffset.UtcNow;
        evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now);
        evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(1));

        evaluator.Reset();

        Assert.Empty(evaluator.Evaluate(Snapshot(cpu: 95), thresholds, now.AddSeconds(2)));
    }

    private static HardwareSnapshot Snapshot(
        int cpu = 70,
        int gpu = 60,
        int fan1 = 4_000,
        int fan2 = 4_000,
        FanControlMode mode = FanControlMode.Fixed,
        int? fixedPercent = 80) => new(
        DateTimeOffset.Now,
        cpu,
        gpu,
        fan1,
        fan2,
        fixedPercent,
        fixedPercent,
        true,
        mode,
        fixedPercent,
        true,
        null);
}
