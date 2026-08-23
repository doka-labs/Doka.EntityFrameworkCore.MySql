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
            throw new FileNotFoundException(
                "The performance contract was not copied to the benchmark output.",
                path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PerformanceContract>(stream, s_serializerOptions)
            ?? throw new InvalidDataException($"Performance contract '{path}' is empty.");
    }
}

internal sealed class PerformanceTargetContract
{
    public string DisplayName { get; init; } = string.Empty;

    public string EngineFamily { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;

    public int HostPort { get; init; }

    public string ServerImage { get; init; } = string.Empty;
}

internal sealed class PerformanceProfileContract
{
    public int SoakIterations { get; init; }

    public int SoakConcurrency { get; init; }

    public bool SoakRequired { get; init; }
}

internal sealed class PerformanceWorkloadDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Family { get; init; } = string.Empty;

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
