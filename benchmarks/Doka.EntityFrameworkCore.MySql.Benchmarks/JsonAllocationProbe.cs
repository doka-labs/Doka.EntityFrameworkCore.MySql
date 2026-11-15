using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Measures the actual JSON comparer in one isolated, GC-validated allocation window.</summary>
internal static class JsonAllocationProbe
{
    private const int Iterations = 1000;

    /// <summary>Warms the selected workload and records one unchanged same-thread allocation delta.</summary>
    /// <param name="scenario">An equality, hash, empty, or deliberate-allocation control.</param>
    /// <returns>The workload identity, result, exact bytes and GC-window evidence.</returns>
    internal static JsonAllocationProof Run(
        string scenario
    )
    {
        var allocate = scenario is "allocate" or "equality-allocate" or "hash-allocate";
        var json = "["
            + string.Join(
                ",",
                Enumerable.Range(0, 100).Select(index => $"{{\"id\":{index},\"value\":\"item-{index:D6}\"}}"))
            + "]";

        var left = JsonNode.Parse(json)!;
        var right = JsonNode.Parse(json)!;
        var comparer = MySqlJsonValueComparers.JsonNodeComparer;
        var expectedHash = comparer.GetHashCode(left);
        Func<bool> iteration = scenario switch
        {
            "empty" or "allocate" => static () => true,
            "equality" or "equality-allocate" => () => comparer.Equals(left, right),
            "hash" or "hash-allocate" => () => comparer.GetHashCode(left) == expectedHash,
            _ => throw new ArgumentException($"Unknown JSON allocation scenario '{scenario}'.", nameof(scenario)),
        };

        Func<bool> workload = () => Execute(iteration, allocate);

        // WHY: DOM materialization, comparer delegates and both measurement methods
        // must be initialized before the single retained steady-state window.
        for (var warmup = 0; warmup < 10; warmup++)
        {
            _ = workload();
        }

        // WHY: Warm the sampler with an empty delegate, then retain exactly one
        // window for the selected workload; no retry or best-of selection occurs.
        _ = Measure(static () => true);
        var measurement = Measure(workload);
        if (!measurement.ResultMatches
            || measurement.AllocatedBytes < 0
            || measurement.Gen0Collections != 0
            || measurement.Gen1Collections != 0
            || measurement.Gen2Collections != 0
            || measurement.BackgroundGcBefore != measurement.BackgroundGcAfter)
        {
            throw new InvalidOperationException("JSON allocation measurement overlapped GC or produced an invalid result.");
        }

        return new JsonAllocationProof(
            1,
            scenario,
            Iterations,
            Environment.ProcessId,
            Environment.CurrentManagedThreadId,
            RuntimeInformation.FrameworkDescription,
            measurement.AllocatedBytes,
            measurement.ResultMatches,
            measurement.Gen0Collections,
            measurement.Gen1Collections,
            measurement.Gen2Collections,
            measurement.BackgroundGcBefore,
            measurement.BackgroundGcAfter);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Execute(
        Func<bool> iteration,
        bool allocate
    )
    {
        var matches = true;
        for (var index = 0; index < Iterations; index++)
        {
            matches &= iteration();
        }

        if (allocate)
        {
            // WHY: One escaping array after the complete workload proves that
            // the retained counter still detects real per-window allocations.
            GC.KeepAlive(new byte[37]);
        }

        return matches;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement Measure(
        Func<bool> workload
    )
    {
        // WHY: Complete collection and finalization before sampling. Background
        // GC in the shared test host can corrupt the runtime's thread counter.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var backgroundBefore = GC.GetGCMemoryInfo(GCKind.Background).Index;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = workload();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return new Measurement(
            allocated,
            matches,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            backgroundBefore,
            GC.GetGCMemoryInfo(GCKind.Background).Index);
    }

    private readonly record struct Measurement(
        long AllocatedBytes,
        bool ResultMatches,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        long BackgroundGcBefore,
        long BackgroundGcAfter);
}

/// <summary>The complete evidence emitted after the allocation window has closed.</summary>
/// <param name="SchemaVersion">The fixed proof format version.</param>
/// <param name="Scenario">The explicitly requested workload.</param>
/// <param name="Iterations">The number of comparer or empty-control calls in the retained window.</param>
/// <param name="ProcessId">The identity of the isolated measurement process.</param>
/// <param name="ManagedThreadId">The identity of the synchronously measured thread.</param>
/// <param name="Runtime">The actual runtime executing the workload.</param>
/// <param name="AllocatedBytes">The exact same-thread allocation delta.</param>
/// <param name="ResultMatches">Whether every invocation preserved the expected comparer result.</param>
/// <param name="Gen0Collections">Generation-zero collections overlapping the window.</param>
/// <param name="Gen1Collections">Generation-one collections overlapping the window.</param>
/// <param name="Gen2Collections">Generation-two collections overlapping the window.</param>
/// <param name="BackgroundGcBefore">The last background-GC index before sampling.</param>
/// <param name="BackgroundGcAfter">The last background-GC index after sampling.</param>
internal sealed record JsonAllocationProof(
    int SchemaVersion,
    string Scenario,
    int Iterations,
    int ProcessId,
    int ManagedThreadId,
    string Runtime,
    long AllocatedBytes,
    bool ResultMatches,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long BackgroundGcBefore,
    long BackgroundGcAfter);
