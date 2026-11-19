namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Retains accepted evaluation identities for the common allocation/resource policy.</summary>
internal enum PerformanceEvaluationMode
{
    /// <summary>Retains the legacy policy identity without enabling host-sensitive rejection.</summary>
    Full,

    /// <summary>Enforces allocations and resource correctness while retaining host-sensitive diagnostics.</summary>
    AllocationsOnly,
}

/// <summary>Separates reproducible allocation checks from host-sensitive diagnostics.</summary>
internal static class PerformanceEvaluationPolicy
{
    /// <summary>Resolves the policy shared by benchmark execution and report evaluation.</summary>
    internal static PerformanceEvaluationMode Current => Resolve(
        Environment.GetEnvironmentVariable("DOKA_BENCHMARK_EVALUATION"));

    /// <summary>Resolves legacy command-line inputs to the same hardware-independent enforcement.</summary>
    internal static PerformanceEvaluationMode Resolve(
        string? requested
    )
    {
        // WHY: Existing operator scripts may still request full. Keep that input
        // compatible, but local CPU, scheduling and GC variance must not change
        // the verdict any more than hosted variance may.
        return requested switch
        {
            null or "" or "full" or "allocations-only" => PerformanceEvaluationMode.AllocationsOnly,
            _ => throw new ArgumentException(
                "Benchmark evaluation must be full or allocations-only.",
                nameof(requested)),
        };
    }

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
            "allocatedBytes"
                or "allocationRatio"
                or "cacheEntries"
                or "rentCount"
                or "returnCount"
                or "outstandingBuffers"
                or "threadsConnectedBefore"
                or "threadsConnectedAfter"
                or "connectionDelta"
                or "heldLocks" => false,
            "medianNanoseconds"
                or "p95Nanoseconds"
                or "p99Nanoseconds"
                or "meanRatio"
                or "gen2CollectionsPer1000"
                or "workingSetGrowthBytes"
                or "workingSetFirstBytes"
                or "workingSetLastBytes"
                or "managedHeapGrowthBytes"
                or "managedHeapFirstBytes"
                or "managedHeapLastBytes"
                or "initialOperationsPerSecond"
                or "finalOperationsPerSecond"
                or "throughputRetentionRatio" => true,
            _ => throw new InvalidDataException($"Performance metric '{metric}' has no evaluation policy."),
        };
    }
}
