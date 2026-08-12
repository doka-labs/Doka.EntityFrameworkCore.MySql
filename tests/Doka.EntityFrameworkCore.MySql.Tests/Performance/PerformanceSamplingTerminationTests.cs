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
    public void Pilot_uses_the_least_contended_observation()
    {
        var elapsedTicks = PerformanceSampling.ResolvePilotElapsedTicks(
            [300, 100, 200]);

        Assert.Equal(100, elapsedTicks);
    }

    [Fact]
    public void Pilot_rejects_a_non_positive_observation()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PerformanceSampling.ResolvePilotElapsedTicks(
                [300, 0, 200]));

        Assert.Equal("pilotSamplesElapsedTicks", exception.ParamName);
    }

    [Theory]
    [InlineData(0, 1_000_000_000, 120, 16, "minimumMeasurementDurationMilliseconds")]
    [InlineData(2000, 0, 120, 16, "stopwatchFrequency")]
    [InlineData(2000, 1_000_000_000, 99, 16, "durationHeadroomPercent")]
    [InlineData(2000, 1_000_000_000, 120, 0, "startingSampleCount")]
    public void Pilot_target_reports_the_invalid_parameter(
        int minimumMeasurementDurationMilliseconds,
        long stopwatchFrequency,
        int durationHeadroomPercent,
        int startingSampleCount,
        string expectedParameter
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PerformanceSampling.ResolveTargetSampleTicks(
                minimumMeasurementDurationMilliseconds,
                stopwatchFrequency,
                durationHeadroomPercent,
                startingSampleCount));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Theory]
    [InlineData(0, 100, 1000, 64, "configuredOperationsPerSample")]
    [InlineData(4, 0, 1000, 64, "pilotElapsedTicks")]
    [InlineData(4, 100, 0, 64, "targetSampleTicks")]
    [InlineData(4, 100, 1000, 0, "maximumOperationsPerSampleMultiplier")]
    public void Pilot_batch_reports_the_invalid_parameter(
        int configuredOperationsPerSample,
        long pilotElapsedTicks,
        long targetSampleTicks,
        int maximumOperationsPerSampleMultiplier,
        string expectedParameter
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PerformanceSampling.ResolveOperationsPerSample(
                configuredOperationsPerSample,
                pilotElapsedTicks,
                targetSampleTicks,
                maximumOperationsPerSampleMultiplier));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Theory]
    [InlineData(16, 150_000_000)]
    [InlineData(1024, 2_343_750)]
    [InlineData(8192, 292_969)]
    public void Pilot_target_distributes_the_duration_over_the_starting_population(
        int startingSampleCount,
        long expectedTargetTicks
    )
    {
        var targetTicks = PerformanceSampling.ResolveTargetSampleTicks(
            minimumMeasurementDurationMilliseconds: 2000,
            stopwatchFrequency: 1_000_000_000,
            durationHeadroomPercent: 120,
            startingSampleCount);

        Assert.Equal(expectedTargetTicks, targetTicks);
        Assert.True(startingSampleCount * targetTicks >= 2_400_000_000);
    }

    [Fact]
    public void Pilot_scales_operations_instead_of_consuming_the_sample_cap()
    {
        var operations = PerformanceSampling.ResolveOperationsPerSample(
            configuredOperationsPerSample: 4,
            pilotElapsedTicks: 100,
            targetSampleTicks: 1_000,
            maximumOperationsPerSampleMultiplier: 64);

        Assert.Equal(40, operations);
    }

    [Fact]
    public void Pilot_preserves_the_configured_batch_when_it_meets_the_target()
    {
        var operations = PerformanceSampling.ResolveOperationsPerSample(
            configuredOperationsPerSample: 32,
            pilotElapsedTicks: 1_000,
            targetSampleTicks: 1_000,
            maximumOperationsPerSampleMultiplier: 64);

        Assert.Equal(32, operations);
    }

    [Fact]
    public void Pilot_growth_is_bounded_independently_from_the_sample_cap()
    {
        var operations = PerformanceSampling.ResolveOperationsPerSample(
            configuredOperationsPerSample: 16,
            pilotElapsedTicks: 1,
            targetSampleTicks: 10_000,
            maximumOperationsPerSampleMultiplier: 128);

        Assert.Equal(2_048, operations);
    }

    [Fact]
    public void Fast_workload_reaches_the_duration_floor_without_consuming_the_sample_cap()
    {
        const long pilotElapsedTicks = 300_000;
        const long targetSampleTicks = 150_000_000;

        var operations = PerformanceSampling.ResolveOperationsPerSample(
            configuredOperationsPerSample: 1,
            pilotElapsedTicks,
            targetSampleTicks,
            maximumOperationsPerSampleMultiplier: 1024);

        var plannedMeasurementTicks = 16L * operations * pilotElapsedTicks;

        Assert.Equal(500, operations);
        Assert.True(plannedMeasurementTicks >= 2_000_000_000);
    }

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
