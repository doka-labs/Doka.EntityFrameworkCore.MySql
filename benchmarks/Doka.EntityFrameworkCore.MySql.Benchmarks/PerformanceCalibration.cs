namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures a small current-run control beside provider workloads so historical
/// latency comparisons can remove ordinary host and local database contention.
/// </summary>
internal static class PerformanceCalibration
{
    private const int CpuIterations = 1_000_000;

    private static long s_cpuChecksum;

    public static string ResolveKind(
        PerformanceCalibrationContract contract,
        string family
    )
    {
        var isCpu = contract.CpuFamilies.Contains(family, StringComparer.Ordinal);
        var isDatabase = contract.DatabaseFamilies.Contains(family, StringComparer.Ordinal);

        return (isCpu, isDatabase) switch
        {
            (true, false) => "cpu",
            (false, true) => "database",
            _ => throw new InvalidDataException(
                $"Performance family '{family}' must have exactly one calibration kind."),
        };
    }

    public static async Task<double> MeasurePulseAsync(
        string kind,
        int sampleCount,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);

        var samples = string.Equals(kind, "cpu", StringComparison.Ordinal)
            ? MeasureCpuSamples(sampleCount, cancellationToken)
            : string.Equals(kind, "database", StringComparison.Ordinal)
                ? await MeasureDatabaseSamplesAsync(sampleCount, cancellationToken)
                    .ConfigureAwait(false)
                : throw new InvalidDataException(
                    $"Unknown performance calibration kind '{kind}'.");

        Array.Sort(samples);
        return Percentile(samples, 0.5);
    }

    private static double[] MeasureCpuSamples(
        int sampleCount,
        CancellationToken cancellationToken
    )
    {
        _ = ExecuteCpuControl();
        var samples = new double[sampleCount];

        for (var sample = 0; sample < samples.Length; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var checksum = ExecuteCpuControl();
            var elapsed = Stopwatch.GetTimestamp() - started;

            Interlocked.Exchange(ref s_cpuChecksum, checksum);
            samples[sample] = ToNanoseconds(elapsed);
        }

        return samples;
    }

    private static async Task<double[]> MeasureDatabaseSamplesAsync(
        int sampleCount,
        CancellationToken cancellationToken
    )
    {
        var connectionString = BenchmarkEnvironment.CreateConnectionString(
            BenchmarkEnvironment.DatabaseNameValue);

        await using var connection = new MySqlConnection(connectionString);

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        _ = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        var samples = new double[sampleCount];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var started = Stopwatch.GetTimestamp();
            _ = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
            var elapsed = Stopwatch.GetTimestamp() - started;

            samples[sample] = ToNanoseconds(elapsed);
        }

        return samples;
    }

    private static long ExecuteCpuControl()
    {
        ulong state = 1469598103934665603;

        for (var index = 0; index < CpuIterations; index++)
        {
            state ^= (uint)index;
            state = ((state << 13) | (state >> 51)) * 1099511628211;
        }

        return unchecked((long)state);
    }

    private static double ToNanoseconds(
        long elapsedTicks
    )
    {
        var nanoseconds = elapsedTicks * (1_000_000_000d / Stopwatch.Frequency);

        return double.IsFinite(nanoseconds) && nanoseconds > 0
            ? nanoseconds
            : throw new InvalidOperationException(
                $"Performance calibration produced an invalid duration of {nanoseconds} ns.");
    }

    private static double Percentile(
        double[] sortedValues,
        double percentile
    )
    {
        var position = (sortedValues.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex]
            + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }
}
