using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

/// <summary>
/// Pins the sampling termination rule the measurement loop consumes.
/// </summary>
/// <remarks>
/// The original defect lived here: the minimum measurement duration could
/// drive the population past the configured cap, and the adaptive decision
/// then rejected the very count the loop had produced. These tests exercise
/// the rule the loop actually calls, so the regression cannot return through
/// a duplicated condition.
/// </remarks>
public sealed class PerformanceSamplingTerminationTests
{
    private const long MinimumTicks = 2_000;

    [Fact]
    public void Sampling_stops_at_the_cap_even_when_the_duration_is_unmet()
    {
        var shouldContinue = PerformanceSampling.ShouldCollectAnotherSample(
            sampleCount: 1024,
            requiredSampleCount: 256,
            measuredTicks: 1,
            minimumMeasurementTicks: MinimumTicks,
            maximumSampleCount: 1024);

        Assert.False(shouldContinue);
    }

    [Fact]
    public void Sampling_continues_below_the_cap_while_the_duration_is_unmet()
    {
        var shouldContinue = PerformanceSampling.ShouldCollectAnotherSample(
            sampleCount: 300,
            requiredSampleCount: 256,
            measuredTicks: 1,
            minimumMeasurementTicks: MinimumTicks,
            maximumSampleCount: 1024);

        Assert.True(shouldContinue);
    }

    [Fact]
    public void Sampling_stops_once_both_targets_are_satisfied()
    {
        var shouldContinue = PerformanceSampling.ShouldCollectAnotherSample(
            sampleCount: 256,
            requiredSampleCount: 256,
            measuredTicks: MinimumTicks,
            minimumMeasurementTicks: MinimumTicks,
            maximumSampleCount: 1024);

        Assert.False(shouldContinue);
    }

    [Fact]
    public void Sampling_never_exceeds_the_cap_across_a_full_fast_workload_run()
    {
        // A workload so fast that the duration target is unreachable within the
        // cap is exactly the shape that produced the original crash.
        var sampleCount = 0;
        long measuredTicks = 0;

        while (PerformanceSampling.ShouldCollectAnotherSample(
                   sampleCount,
                   requiredSampleCount: 256,
                   measuredTicks,
                   minimumMeasurementTicks: MinimumTicks,
                   maximumSampleCount: 1024))
        {
            sampleCount++;
            measuredTicks += 1;
        }

        Assert.Equal(1024, sampleCount);
        Assert.True(measuredTicks < MinimumTicks);

        // The count the loop produced must remain a legal input to the
        // adaptive decision that follows it.
        var target = PerformanceSampling.NextSampleTarget(
            sampleCount,
            maximumSampleCount: 1024,
            sampleBlockSize: 32,
            relativeStandardError: 4.0,
            maximumRelativeStandardError: 0.25);

        Assert.Equal(1024, target);
    }

    [Fact]
    public void Cap_reached_before_the_duration_is_classified_as_capped()
    {
        var (reason, minimumDurationReached) = PerformanceSampling.ClassifyTermination(
            sampleCount: 1024,
            maximumSampleCount: 1024,
            measuredTicks: 1,
            minimumMeasurementTicks: MinimumTicks,
            relativeStandardError: 0.1,
            maximumRelativeStandardError: 0.25);

        Assert.Equal("sample_cap_reached", reason);
        Assert.False(minimumDurationReached);
    }

    [Fact]
    public void Cap_reached_before_the_precision_target_is_classified_as_capped()
    {
        var (reason, minimumDurationReached) = PerformanceSampling.ClassifyTermination(
            sampleCount: 1024,
            maximumSampleCount: 1024,
            measuredTicks: MinimumTicks,
            minimumMeasurementTicks: MinimumTicks,
            relativeStandardError: 4.0,
            maximumRelativeStandardError: 0.25);

        Assert.Equal("sample_cap_reached", reason);
        Assert.True(minimumDurationReached);
    }

    [Fact]
    public void Meeting_both_targets_exactly_at_the_cap_stays_precise()
    {
        var (reason, minimumDurationReached) = PerformanceSampling.ClassifyTermination(
            sampleCount: 1024,
            maximumSampleCount: 1024,
            measuredTicks: MinimumTicks,
            minimumMeasurementTicks: MinimumTicks,
            relativeStandardError: 0.1,
            maximumRelativeStandardError: 0.25);

        Assert.Equal("precision_reached", reason);
        Assert.True(minimumDurationReached);
    }

    [Fact]
    public void Converging_below_the_cap_is_precise()
    {
        var (reason, minimumDurationReached) = PerformanceSampling.ClassifyTermination(
            sampleCount: 512,
            maximumSampleCount: 1024,
            measuredTicks: MinimumTicks,
            minimumMeasurementTicks: MinimumTicks,
            relativeStandardError: 0.1,
            maximumRelativeStandardError: 0.25);

        Assert.Equal("precision_reached", reason);
        Assert.True(minimumDurationReached);
    }
}
