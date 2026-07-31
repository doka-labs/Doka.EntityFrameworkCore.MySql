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

    public int MinimumValidSamples { get; init; }

    public int MinimumBenchmarkDotNetSamples { get; init; }

    public int SoakIterations { get; init; }

    public int SoakConcurrency { get; init; }

    public bool BaselineRequired { get; init; }

    public bool SoakRequired { get; init; }
}

internal sealed class PerformanceWorkloadDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Family { get; init; } = string.Empty;

    public string Cost { get; init; } = "standard";

    public bool Smoke { get; init; }

    public int OperationsPerSample { get; init; } = 1;
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
    public int SchemaVersion { get; init; } = 2;

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

    public string EngineFamily { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;

    public string ServerImage { get; init; } = string.Empty;
}

internal sealed class PerformanceWorkloadResult
{
    public string Id { get; init; } = string.Empty;

    public string Family { get; init; } = string.Empty;

    public int WarmupSamples { get; init; }

    public int SampleCount { get; init; }

    public int OperationsPerSample { get; init; }

    public long Checksum { get; init; }

    public double MedianNanoseconds { get; init; }

    public double P95Nanoseconds { get; init; }

    public double P99Nanoseconds { get; init; }

    public double StandardErrorNanoseconds { get; init; }

    public long AllocatedBytesPerOperation { get; init; }

    public long RetainedBytes { get; init; }

    public double Gen0CollectionsPer1000 { get; init; }

    public double Gen1CollectionsPer1000 { get; init; }

    public double Gen2CollectionsPer1000 { get; init; }

    public List<double> SamplesNanoseconds { get; init; } = [];
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

        await using var stream = File.Create(outputPath);
        await JsonSerializer
            .SerializeAsync(stream, value, s_serializerOptions, cancellationToken)
            .ConfigureAwait(false);
        await stream
            .WriteAsync("\n"u8.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }
}
