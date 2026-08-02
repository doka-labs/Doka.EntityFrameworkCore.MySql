namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Executes named provider workloads outside BenchmarkDotNet so release evidence
/// retains per-operation-normalized samples for median, p95, and p99
/// evaluation. Fixed contract-owned batches amortize timer overhead for fast
/// idempotent workloads.
/// </summary>
internal static class PerformanceWorkloadRunner
{
    public static async Task<int> RunAsync(
        string outputPath,
        CancellationToken cancellationToken = default
    ) => await RunAsync(
            outputPath,
            workloadId: null,
            cancellationToken)
        .ConfigureAwait(false);

    /// <summary>
    /// Measures one contract workload for root-cause analysis. The resulting
    /// diagnostic report has a distinct kind and cannot satisfy the complete
    /// scorecard validator.
    /// </summary>
    public static async Task<int> RunDiagnosticAsync(
        string outputPath,
        string workloadId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadId);

        return await RunAsync(outputPath, workloadId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(
        string outputPath,
        string? workloadId,
        CancellationToken cancellationToken
    )
    {
        var contract = PerformanceContract.Load();
        var profileName = BenchmarkProfiles.Current;
        var profile = GetProfile(contract, profileName);

        BenchmarkEnvironment.EnsureInitialized();

        using var catalog = PerformanceWorkloadCatalog.Create();
        var definitions = ApplicableDefinitions(contract, profileName);
        ValidateCatalog(contract.Workloads, catalog.Workloads);

        if (workloadId is not null)
        {
            definitions =
            [
                definitions.SingleOrDefault(definition =>
                    string.Equals(definition.Id, workloadId, StringComparison.Ordinal))
                ?? throw new InvalidDataException(
                    $"Performance contract does not define applicable workload '{workloadId}'."),
            ];
        }

        var results = new List<PerformanceWorkloadResult>(definitions.Count);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Running performance workload {definition.Id}...");

            var workload = catalog.Workloads[definition.Id];
            var sampleCount = string.Equals(definition.Cost, "expensive", StringComparison.Ordinal)
                ? profile.ExpensiveMeasurementSamples
                : profile.MeasurementSamples;
            var result = await MeasureAsync(workload, definition, profile.WarmupSamples, sampleCount, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);
        }

        var report = new PerformanceRunReport
        {
            Kind = workloadId is null
                ? "performance-workloads"
                : "performance-workload-diagnostic",
            ContractVersion = contract.ContractVersion,
            RunId = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUN_ID"),
            Target = BenchmarkEnvironment.TargetIdValue,
            Profile = profileName,
            Commit = RequiredEnvironmentVariable("DOKA_BENCHMARK_COMMIT"),
            SourceHash = RequiredEnvironmentVariable("DOKA_BENCHMARK_SOURCE_HASH"),
            RunnerClass = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUNNER_CLASS"),
            GeneratedUtc = DateTimeOffset.UtcNow,
            StopwatchFrequency = Stopwatch.Frequency,
            Environment = new PerformanceEnvironmentEvidence
            {
                EngineFamily = BenchmarkEnvironment.EngineFamilyValue,
                ServerVersion = await BenchmarkEnvironment
                    .ReadServerVersionAsync(cancellationToken)
                    .ConfigureAwait(false),
                ServerImage = RequiredEnvironmentVariable("DOKA_BENCHMARK_SERVER_IMAGE"),
            },
            Workloads = results,
        };

        await PerformanceReportWriter
            .WriteAsync(outputPath, report, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }

    public static void WriteApplicableWorkloadIds(
        TextWriter writer
    )
    {
        var contract = PerformanceContract.Load();
        var definitions = ApplicableDefinitions(contract, BenchmarkProfiles.Current);

        foreach (var definition in definitions)
        {
            writer.WriteLine(definition.Id);
        }
    }

    private static async Task<PerformanceWorkloadResult> MeasureAsync(
        PerformanceWorkload workload,
        PerformanceWorkloadDefinition definition,
        int warmupSamples,
        int sampleCount,
        CancellationToken cancellationToken
    )
    {
        if (sampleCount <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive sample count.");
        }

        if (definition.OperationsPerSample <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive operations-per-sample value.");
        }

        for (var index = 0; index < warmupSamples; index++)
        {
            await PrepareAsync(workload, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                for (var operation = 0; operation < definition.OperationsPerSample; operation++)
                {
                    _ = await workload
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await CleanupAsync(workload, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var retainedBefore = ReadManagedHeapSizeAfterFullCollection();
        var samples = new List<double>(sampleCount);
        long allocatedBytes = 0;
        var gen0Collections = 0;
        var gen1Collections = 0;
        var gen2Collections = 0;
        long checksum = 0;

        for (var index = 0; index < sampleCount; index++)
        {
            await PrepareAsync(workload, cancellationToken)
                .ConfigureAwait(false);

            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var gen0Before = GC.CollectionCount(0);
            var gen1Before = GC.CollectionCount(1);
            var gen2Before = GC.CollectionCount(2);
            var started = Stopwatch.GetTimestamp();
            long sampleChecksum = 0;
            long elapsed;

            try
            {
                for (var operation = 0; operation < definition.OperationsPerSample; operation++)
                {
                    sampleChecksum = unchecked(sampleChecksum
                        + await workload
                            .ExecuteAsync(cancellationToken)
                            .ConfigureAwait(false));
                }

                elapsed = Stopwatch.GetTimestamp() - started;
                allocatedBytes += GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                gen0Collections += GC.CollectionCount(0) - gen0Before;
                gen1Collections += GC.CollectionCount(1) - gen1Before;
                gen2Collections += GC.CollectionCount(2) - gen2Before;
            }
            finally
            {
                await CleanupAsync(workload, cancellationToken)
                    .ConfigureAwait(false);
            }

            checksum = unchecked(checksum + sampleChecksum);
            var nanoseconds = elapsed * (1_000_000_000d / Stopwatch.Frequency) / definition.OperationsPerSample;

            if (!double.IsFinite(nanoseconds)
                || nanoseconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Workload '{workload.Id}' produced an invalid elapsed time of {nanoseconds} ns.");
            }

            samples.Add(nanoseconds);
        }

        // Use the same finalizer-draining boundary on both sides. Otherwise,
        // pending finalizable objects can be misreported as retained workload
        // memory even though their owners were disposed by CleanupAsync.
        var retainedAfter = ReadManagedHeapSizeAfterFullCollection();
        var retainedBytes = Math.Max(0, retainedAfter - retainedBefore);
        var measuredOperations = checked((long)sampleCount * definition.OperationsPerSample);
        var sortedSamples = samples
            .Order()
            .ToArray();

        return new PerformanceWorkloadResult
        {
            Id = definition.Id,
            Family = definition.Family,
            WarmupSamples = warmupSamples,
            SampleCount = sampleCount,
            OperationsPerSample = definition.OperationsPerSample,
            Checksum = checksum,
            MedianNanoseconds = Percentile(sortedSamples, 0.5),
            P95Nanoseconds = Percentile(sortedSamples, 0.95),
            P99Nanoseconds = Percentile(sortedSamples, 0.99),
            StandardErrorNanoseconds = StandardError(samples),
            AllocatedBytesPerOperation = allocatedBytes / measuredOperations,
            RetainedBytes = retainedBytes,
            Gen0CollectionsPer1000 = gen0Collections * 1000d / measuredOperations,
            Gen1CollectionsPer1000 = gen1Collections * 1000d / measuredOperations,
            Gen2CollectionsPer1000 = gen2Collections * 1000d / measuredOperations,
            SamplesNanoseconds = samples,
        };
    }

    private static ValueTask PrepareAsync(
        PerformanceWorkload workload,
        CancellationToken cancellationToken
    ) => workload.PrepareAsync?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;

    private static ValueTask CleanupAsync(
        PerformanceWorkload workload,
        CancellationToken cancellationToken
    ) => workload.CleanupAsync?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;

    private static double Percentile(
        double[] sortedValues,
        double percentile
    )
    {
        if (sortedValues.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        }

        var position = (sortedValues.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    private static double StandardError(
        List<double> values
    )
    {
        if (values.Count <= 1)
        {
            return 0;
        }

        var mean = values.Average();
        var sumOfSquares = values.Sum(value => Math.Pow(value - mean, 2));
        var sampleVariance = sumOfSquares / (values.Count - 1);

        return Math.Sqrt(sampleVariance) / Math.Sqrt(values.Count);
    }

    private static List<PerformanceWorkloadDefinition> ApplicableDefinitions(
        PerformanceContract contract,
        string profileName
    )
    {
        var definitions = contract
            .Workloads.Where(definition =>
                !string.Equals(profileName, "smoke", StringComparison.Ordinal) || definition.Smoke)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToList();

        var duplicate = definitions
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException($"Performance contract contains duplicate workload '{duplicate.Key}'.");
        }

        return definitions;
    }

    private static void ValidateCatalog(
        IReadOnlyCollection<PerformanceWorkloadDefinition> definitions,
        IReadOnlyDictionary<string, PerformanceWorkload> workloads
    )
    {
        var expected = definitions
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var actual = workloads.Keys.ToHashSet(StringComparer.Ordinal);
        var missing = expected
            .Except(actual, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unknown = actual
            .Except(expected, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0
            && unknown.Length == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"Performance workload catalog drift. Missing: [{string.Join(", ", missing)}]. "
            + $"Unknown: [{string.Join(", ", unknown)}].");
    }

    private static PerformanceProfileContract GetProfile(
        PerformanceContract contract,
        string profileName
    ) => contract.Profiles.TryGetValue(profileName, out var profile)
        ? profile
        : throw new InvalidDataException($"Performance contract does not define profile '{profileName}'.");

    private static string RequiredEnvironmentVariable(
        string name
    )
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required environment variable '{name}' is not set.")
            : value;
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long ReadManagedHeapSizeAfterFullCollection()
    {
        ForceFullCollection();
        return GC.GetTotalMemory(forceFullCollection: false);
    }
}
