namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Provides the statistical decisions shared by workload measurement and its
/// deterministic regression tests.
/// </summary>
internal static class PerformanceSampling
{
    /// <summary>
    /// Calculates a linearly interpolated percentile from an ordered sample.
    /// </summary>
    public static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile
    )
    {
        ArgumentNullException.ThrowIfNull(sortedValues);

        if (sortedValues.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        }

        if (!double.IsFinite(percentile)
            || percentile < 0
            || percentile > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var position = (sortedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;

        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    /// <summary>
    /// Calculates sample standard error using Bessel-corrected variance.
    /// </summary>
    public static double StandardError(
        IReadOnlyCollection<double> values
    )
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count <= 1)
        {
            return 0;
        }

        var mean = values.Average();
        var sumOfSquares = values.Sum(value => Math.Pow(value - mean, 2));
        var sampleVariance = sumOfSquares / (values.Count - 1);

        return Math.Sqrt(sampleVariance) / Math.Sqrt(values.Count);
    }

    /// <summary>
    /// Calculates standard error relative to the sample median.
    /// </summary>
    public static double RelativeStandardError(
        IReadOnlyCollection<double> values
    )
    {
        ArgumentNullException.ThrowIfNull(values);

        var sortedValues = values
            .Order()
            .ToArray();

        var median = Percentile(sortedValues, 0.5);

        if (!double.IsFinite(median)
            || median <= 0)
        {
            throw new ArgumentException("Relative standard error requires a finite, positive median.", nameof(values));
        }

        return StandardError(values) / median;
    }

    /// <summary>
    /// Reports whether another sample may be collected.
    /// </summary>
    /// <remarks>
    /// The cap bounds the loop unconditionally. Both measurement targets sit
    /// behind it, so neither the required sample count nor the minimum duration
    /// can drive the population past the configured maximum.
    /// </remarks>
    public static bool ShouldCollectAnotherSample(
        int sampleCount,
        int requiredSampleCount,
        long measuredTicks,
        long minimumMeasurementTicks,
        int maximumSampleCount
    ) => (sampleCount < requiredSampleCount || measuredTicks < minimumMeasurementTicks)
        && sampleCount < maximumSampleCount;

    /// <summary>
    /// Classifies why sampling stopped and whether the duration target was met.
    /// </summary>
    /// <remarks>
    /// The cap counts as the reason only when a target was still unmet. A run
    /// that satisfied both targets exactly at the cap is a precise measurement,
    /// not a truncated one.
    /// </remarks>
    public static (string TerminationReason, bool MinimumDurationReached) ClassifyTermination(
        int sampleCount,
        int maximumSampleCount,
        long measuredTicks,
        long minimumMeasurementTicks,
        double relativeStandardError,
        double maximumRelativeStandardError
    )
    {
        var minimumDurationReached = measuredTicks >= minimumMeasurementTicks;
        var precisionReached = relativeStandardError <= maximumRelativeStandardError;
        var cappedShort = sampleCount >= maximumSampleCount
            && (!minimumDurationReached || !precisionReached);

        return (
            cappedShort
                ? PerformanceWorkloadRunner.SampleCapReached
                : PerformanceWorkloadRunner.PrecisionReached,
            minimumDurationReached);
    }

    /// <summary>
    /// Selects the next bounded, calibration-aligned sample target.
    /// </summary>
    public static int NextSampleTarget(
        int currentSampleCount,
        int maximumSampleCount,
        int sampleBlockSize,
        double relativeStandardError,
        double maximumRelativeStandardError
    )
    {
        if (currentSampleCount <= 0
            || maximumSampleCount < currentSampleCount
            || sampleBlockSize <= 0
            || !double.IsFinite(relativeStandardError)
            || relativeStandardError < 0
            || !double.IsFinite(maximumRelativeStandardError)
            || maximumRelativeStandardError < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSampleCount),
                "The adaptive sampling decision contains an invalid bound or statistic.");
        }

        if (relativeStandardError <= maximumRelativeStandardError
            || currentSampleCount == maximumSampleCount)
        {
            return currentSampleCount;
        }

        return Math.Min(maximumSampleCount, checked(currentSampleCount + sampleBlockSize));
    }
}
