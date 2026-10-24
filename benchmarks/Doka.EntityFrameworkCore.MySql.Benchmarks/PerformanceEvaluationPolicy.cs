namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Identifies which measured budgets may reject a benchmark run.</summary>
internal enum PerformanceEvaluationMode
{
    /// <summary>Enforces every reviewed budget for controlled-host investigations.</summary>
    Full,

    /// <summary>Enforces allocations and resource correctness while retaining host-sensitive diagnostics.</summary>
    AllocationsOnly,
}

/// <summary>Separates reproducible allocation checks from host-sensitive diagnostics.</summary>
internal static class PerformanceEvaluationPolicy
{
    /// <summary>Resolves the command-line process policy without permitting hosted timing gates.</summary>
    internal static PerformanceEvaluationMode Resolve(
        string? requested,
        string? githubActions
    )
    {
        var mode = requested switch
        {
            null or "" or "full" => PerformanceEvaluationMode.Full,
            "allocations-only" => PerformanceEvaluationMode.AllocationsOnly,
            _ => throw new ArgumentException("Benchmark evaluation must be full or allocations-only.", nameof(requested)),
        };

        // WHY: Hosted CPU, memory and scheduling characteristics vary even under
        // one runner label. A full-mode override must not restore timing gates.
        return string.Equals(githubActions, "true", StringComparison.OrdinalIgnoreCase)
            ? PerformanceEvaluationMode.AllocationsOnly
            : mode;
    }

    /// <summary>Resolves the policy shared by benchmark execution and report evaluation.</summary>
    internal static PerformanceEvaluationMode Current => Resolve(
        Environment.GetEnvironmentVariable("DOKA_BENCHMARK_EVALUATION"),
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    /// <summary>Determines whether a known metric is diagnostic rather than rejecting.</summary>
    internal static bool IsDiagnostic(
        PerformanceEvaluationMode mode,
        string metric
    )
    {
        if (mode is not (PerformanceEvaluationMode.Full or PerformanceEvaluationMode.AllocationsOnly))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return metric switch
        {
            "allocatedBytes" or "allocationRatio" or "cacheEntries" or "outstandingBuffers"
                or "connectionDelta" or "heldLocks" => false,
            "medianNanoseconds" or "p95Nanoseconds" or "p99Nanoseconds" or "meanRatio"
                or "gen2CollectionsPer1000" or "workingSetGrowthBytes" or "managedHeapGrowthBytes"
                or "throughputRetentionRatio" => mode == PerformanceEvaluationMode.AllocationsOnly,
            _ => throw new InvalidDataException($"Performance metric '{metric}' has no evaluation policy."),
        };
    }
}
