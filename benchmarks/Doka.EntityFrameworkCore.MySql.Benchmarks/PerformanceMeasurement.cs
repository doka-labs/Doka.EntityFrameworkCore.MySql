using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal sealed class PerformanceContract
{
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public int SchemaVersion { get; init; }

    public string ContractVersion { get; init; } = string.Empty;

    public Dictionary<string, PerformanceTargetContract> RequiredTargets { get; init; } = [];

    public Dictionary<string, PerformanceProfileContract> Profiles { get; init; } = [];

    public Dictionary<string, PerformanceTimeoutPolicyContract> TimeoutPolicies { get; init; } = [];

    public PerformanceCalibrationContract Calibration { get; init; } = new();

    public List<PerformanceWorkloadDefinition> Workloads { get; init; } = [];

    public SoakBudgetContract SoakBudgets { get; init; } = new();

    public static PerformanceContract Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "performance-contract.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The performance contract was not copied to the benchmark output.", path);
        }

        using var stream = File.OpenRead(path);
        var contract = JsonSerializer.Deserialize<PerformanceContract>(stream, s_serializerOptions);

        return contract ?? throw new InvalidDataException($"Performance contract '{path}' is empty.");
    }
}

internal sealed class PerformanceTargetContract
{
    public string EngineFamily { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;

    public string ServerImage { get; init; } = string.Empty;
}

internal sealed class PerformanceProfileContract
{
    public int WarmupSamples { get; init; }

    public int MeasurementSamples { get; init; }

    public int ExpensiveMeasurementSamples { get; init; }

    public int MinimumMeasurementDurationMilliseconds { get; init; }

    public int MinimumValidSamples { get; init; }

    public int MinimumBenchmarkDotNetSamples { get; init; }

    public int MaximumMeasurementSampleMultiplier { get; init; }

    /// <summary>
    /// Whether the runner uses a pilot measurement to size each sample.
    /// </summary>
    public bool AdaptiveOperationsPerSample { get; init; }

    /// <summary>
    /// Gets the percentage of the minimum measurement duration that adaptive
    /// batches plan across the starting sample population.
    /// </summary>
    public int OperationBatchingDurationHeadroomPercent { get; init; }

    /// <summary>
    /// Gets the pilot observations used to reject one-off scheduler
    /// stalls when sizing a fast workload.
    /// </summary>
    public int OperationBatchingPilotSamples { get; init; }

    /// <summary>
    /// Gets the maximum factor by which pilot sizing may grow a workload's
    /// configured operations per sample.
    /// </summary>
    public int MaximumOperationsPerSampleMultiplier { get; init; }

    public int CalibrationSamplesPerPulse { get; init; }

    public int CalibrationIntervalSamples { get; init; }

    public int MaximumWorkloadMatrixDurationSeconds { get; init; }

    public int MaximumTotalDurationSeconds { get; init; }

    public int MaximumWorkloadDurationSeconds { get; init; }

    public double MaximumRelativeStandardError { get; init; }

    public double MaximumCalibrationRelativeStandardError { get; init; }

    /// <summary>
    /// Whether a measurement-quality shortfall stops the run or is recorded.
    /// </summary>
    /// <remarks>
    /// `observe` records the shortfall and lets the evidence carry it, so the
    /// validator decides; `enforce` stops the run. The distinction already
    /// governed the sample cap on the validation side while the driver applied
    /// neither, which made a calibration shortfall an unhandled exception no
    /// policy could soften.
    /// </remarks>
    public string MeasurementQualityPolicy { get; init; } = "enforce";

    public int SoakIterations { get; init; }

    public int SoakConcurrency { get; init; }

    public bool BaselineRequired { get; init; }

    public bool SoakRequired { get; init; }
}

internal sealed class PerformanceCalibrationContract
{
    public List<string> CpuFamilies { get; init; } = [];

    public List<string> DatabaseFamilies { get; init; } = [];
}

internal sealed class PerformanceTimeoutPolicyContract
{
    public int MinimumWorkloadTimeoutSeconds { get; init; }
}

internal sealed class PerformanceWorkloadDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Family { get; init; } = string.Empty;

    public string Cost { get; init; } = "standard";

    public bool Smoke { get; init; }

    public int OperationsPerSample { get; init; } = 1;

    public int? MinimumWarmupOperations { get; init; }

    public int? MeasurementSamples { get; init; }

    public string? TimeoutPolicy { get; init; }
}

internal sealed class SoakBudgetContract
{
    public int HiloCacheMaximumEntries { get; init; }

    public int PooledBufferMaximumOutstanding { get; init; }

    public int ConnectionMaximumDelta { get; init; }

    public int MigrationLockMaximumHeld { get; init; }

    public long WorkingSetMaximumGrowthBytes { get; init; }

    public long ManagedHeapMaximumGrowthBytes { get; init; }

    public double MinimumThroughputRetentionRatio { get; init; }
}

internal sealed class PerformanceRunReport
{
    // Version 5 binds adaptive batch sizing to its pilot observations. Earlier
    // reports cannot prove why their operations-per-sample value was selected.
    public int SchemaVersion { get; init; } = 5;

    public string Kind { get; init; } = "performance-workloads";

    public string ContractVersion { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public string SourceHash { get; init; } = string.Empty;

    public string RunnerClass { get; init; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; init; }

    public long StopwatchFrequency { get; init; }

    public PerformanceEnvironmentEvidence Environment { get; init; } = new();

    public List<PerformanceWorkloadResult> Workloads { get; init; } = [];
}

internal sealed class PerformanceEnvironmentEvidence
{
    public string FrameworkDescription { get; init; } = RuntimeInformation.FrameworkDescription;

    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;

    public string OsArchitecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();

    public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();

    public string Processor { get; init; } = Environment.GetEnvironmentVariable("DOKA_BENCHMARK_PROCESSOR")
        ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
        ?? RuntimeInformation.ProcessArchitecture.ToString();

    public int ProcessorCount { get; init; } = Environment.ProcessorCount;

    public double HostLoadAverage1Minute { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_LOAD_AVERAGE_1M");

    public double HostLoadAverage5Minutes { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_LOAD_AVERAGE_5M");

    public double HostLoadAverage15Minutes { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_LOAD_AVERAGE_15M");

    public double HostLoadAverage1MinutePerProcessor { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_LOAD_RATIO_1M");

    public string HostAdmissionMetric { get; init; } = RequiredEnvironmentString(
        "DOKA_BENCHMARK_HOST_ADMISSION_METRIC");

    public double AdmittedHostCpuUtilization { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_CPU_UTILIZATION");

    public double MaximumHostCpuUtilization { get; init; } = RequiredEnvironmentNumber(
        "DOKA_BENCHMARK_HOST_MAXIMUM_CPU_UTILIZATION");

    public string EngineFamily { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;

    public string ServerImage { get; init; } = string.Empty;

    private static string RequiredEnvironmentString(
        string name
    )
    {
        var value = Environment.GetEnvironmentVariable(name);

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable '{name}' is empty.");
    }

    private static double RequiredEnvironmentNumber(
        string name
    )
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value)
            || !double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            || !double.IsFinite(parsed)
            || parsed < 0)
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not a finite non-negative number.");
        }

        return parsed;
    }
}

internal sealed class PerformanceWorkloadResult
{
    public string Id { get; init; } = string.Empty;

    public string Family { get; init; } = string.Empty;

    public int WarmupSamples { get; init; }

    public int SampleCount { get; init; }

    /// <summary>
    /// Names why sampling stopped: <c>precision_reached</c> when the relative
    /// standard error met the contract, or <c>sample_cap_reached</c> when the
    /// configured cap bound first. A capped run is a typed result, not an
    /// error; the quality policy decides what may be done with it.
    /// </summary>
    public string TerminationReason { get; init; } = string.Empty;

    /// <summary>
    /// Reports whether the contract's minimum measurement duration was
    /// satisfied. False means the cap stopped sampling first, so the sample is
    /// shorter than the contract intends and cannot be promoted to a baseline.
    /// </summary>
    public bool MinimumDurationReached { get; init; }

    /// <summary>
    /// Gets the workload's contract-owned lower bound before pilot sizing.
    /// </summary>
    public int ConfiguredOperationsPerSample { get; init; }

    /// <summary>
    /// Gets whether the operation batch was fixed or selected by a pilot.
    /// </summary>
    public string OperationBatchingMode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the pilot batch durations in stopwatch ticks, or an empty array for
    /// a fixed batch. Together with the report frequency, these make the
    /// adaptive decision independently reproducible.
    /// </summary>
    public long[] PilotSamplesElapsedTicks { get; init; } = Array.Empty<long>();

    public int OperationsPerSample { get; init; }

    public long Checksum { get; init; }

    public DateTimeOffset MeasuredUtc { get; init; }

    public double MedianNanoseconds { get; init; }

    public double P95Nanoseconds { get; init; }

    public double P99Nanoseconds { get; init; }

    public double StandardErrorNanoseconds { get; init; }

    public string CalibrationKind { get; init; } = string.Empty;

    public double CalibrationMedianNanoseconds { get; init; }

    public double CalibrationStandardErrorNanoseconds { get; init; }

    public double NormalizedMedian { get; init; }

    public double NormalizedP95 { get; init; }

    public double NormalizedP99 { get; init; }

    public long AllocatedBytesPerOperation { get; init; }

    public long RetainedBytes { get; init; }

    public double Gen0CollectionsPer1000 { get; init; }

    public double Gen1CollectionsPer1000 { get; init; }

    public double Gen2CollectionsPer1000 { get; init; }

    public List<double> SamplesNanoseconds { get; init; } = [];

    public List<double> CalibrationNanoseconds { get; init; } = [];

    public List<double> CalibrationPulseNanoseconds { get; init; } = [];

    public List<int> CalibrationPulseIndices { get; init; } = [];

    public List<double> NormalizedSamples { get; init; } = [];
}

internal sealed class PerformanceWorkloadCheckpoint
{
    public int SchemaVersion { get; init; } = 1;

    public string Kind { get; init; } = "performance-workload-checkpoint";

    public string ContractVersion { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public string SourceHash { get; init; } = string.Empty;

    public string RunnerClass { get; init; } = string.Empty;

    public PerformanceWorkloadResult Workload { get; init; } = new();
}

internal sealed class SoakRunReport
{
    public int SchemaVersion { get; init; } = 2;

    public string Kind { get; init; } = "performance-soak";

    public string ContractVersion { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public string SourceHash { get; init; } = string.Empty;

    public string RunnerClass { get; init; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; init; }

    public bool Success { get; init; }

    public List<SoakScenarioResult> Scenarios { get; init; } = [];
}

internal sealed class SoakScenarioResult
{
    public string Id { get; init; } = string.Empty;

    public bool Success { get; init; }

    public Dictionary<string, double> Metrics { get; init; } = [];

    public Dictionary<string, double> Budgets { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

internal sealed record PerformanceWorkload(
    string Id,
    Func<CancellationToken, ValueTask<long>> ExecuteAsync,
    Func<CancellationToken, ValueTask>? PrepareAsync = null,
    Func<CancellationToken, ValueTask>? CleanupAsync = null
);

internal static class PerformanceReportWriter
{
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task WriteAsync<T>(
        string outputPath,
        T value,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The output path must include a directory.", nameof(outputPath));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, value, s_serializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream
                    .WriteAsync("\n"u8.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }

            // A deadline may interrupt serialization. Publishing only by an
            // atomic same-directory rename prevents partial JSON from looking
            // like reusable evidence on the next invocation.
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static T Read<T>(
        string path
    )
    {
        using var stream = File.OpenRead(path);

        return JsonSerializer.Deserialize<T>(stream, s_serializerOptions)
            ?? throw new InvalidDataException($"Performance artifact '{path}' is empty.");
    }
}
