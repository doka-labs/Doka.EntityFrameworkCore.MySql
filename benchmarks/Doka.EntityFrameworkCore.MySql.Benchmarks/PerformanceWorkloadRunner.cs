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
        var runId = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUN_ID");
        var commit = RequiredEnvironmentVariable("DOKA_BENCHMARK_COMMIT");
        var sourceHash = RequiredEnvironmentVariable("DOKA_BENCHMARK_SOURCE_HASH");
        var runnerClass = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUNNER_CLASS");
        var checkpointDirectory = workloadId is null
            ? Environment.GetEnvironmentVariable("DOKA_BENCHMARK_CHECKPOINT_DIRECTORY")
            : null;
        using var runTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        runTimeoutSource.CancelAfter(
            TimeSpan.FromSeconds(profile.MaximumWorkloadMatrixDurationSeconds));
        var runCancellationToken = runTimeoutSource.Token;

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
            runCancellationToken.ThrowIfCancellationRequested();

            if (TryLoadCheckpoint(
                    checkpointDirectory,
                    contract.ContractVersion,
                    runId,
                    profileName,
                    commit,
                    sourceHash,
                    runnerClass,
                    definition,
                    out var checkpointResult))
            {
                Console.WriteLine($"Reusing completed performance workload {definition.Id}.");
                results.Add(checkpointResult);
                continue;
            }

            Console.WriteLine($"Running performance workload {definition.Id}...");

            var workload = catalog.Workloads[definition.Id];
            var profileSampleCount = string.Equals(definition.Cost, "expensive", StringComparison.Ordinal)
                ? profile.ExpensiveMeasurementSamples
                : profile.MeasurementSamples;
            // Tail-sensitive, allocation-free workloads can require a larger
            // contract-owned population than stateful database operations.
            var sampleCount = definition.MeasurementSamples ?? profileSampleCount;
            var warmupSamples = GetWarmupSampleCount(definition, profile.WarmupSamples);
            var calibrationKind = PerformanceCalibration.ResolveKind(
                contract.Calibration,
                definition.Family);
            using var workloadTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                runCancellationToken);
            workloadTimeoutSource.CancelAfter(
                TimeSpan.FromSeconds(profile.MaximumWorkloadDurationSeconds));
            var result = await MeasureAsync(
                    workload,
                    definition,
                    warmupSamples,
                    sampleCount,
                    profile.MinimumMeasurementDurationMilliseconds,
                    calibrationKind,
                    profile.CalibrationSamplesPerPulse,
                    profile.CalibrationIntervalSamples,
                    profile.MaximumCalibrationRelativeStandardError,
                    workloadTimeoutSource.Token)
                .ConfigureAwait(false);
            results.Add(result);

            await WriteCheckpointAsync(
                    checkpointDirectory,
                    contract.ContractVersion,
                    runId,
                    profileName,
                    commit,
                    sourceHash,
                    runnerClass,
                    result)
                .ConfigureAwait(false);
        }

        var report = new PerformanceRunReport
        {
            Kind = workloadId is null
                ? "performance-workloads"
                : "performance-workload-diagnostic",
            ContractVersion = contract.ContractVersion,
            RunId = runId,
            Target = BenchmarkEnvironment.TargetIdValue,
            Profile = profileName,
            Commit = commit,
            SourceHash = sourceHash,
            RunnerClass = runnerClass,
            GeneratedUtc = DateTimeOffset.UtcNow,
            StopwatchFrequency = Stopwatch.Frequency,
            Environment = new PerformanceEnvironmentEvidence
            {
                EngineFamily = BenchmarkEnvironment.EngineFamilyValue,
                ServerVersion = await BenchmarkEnvironment
                    .ReadServerVersionAsync(runCancellationToken)
                    .ConfigureAwait(false),
                ServerImage = RequiredEnvironmentVariable("DOKA_BENCHMARK_SERVER_IMAGE"),
            },
            Workloads = results,
        };

        await PerformanceReportWriter
            .WriteAsync(outputPath, report, runCancellationToken)
            .ConfigureAwait(false);

        return 0;
    }

    private static int GetWarmupSampleCount(
        PerformanceWorkloadDefinition definition,
        int profileWarmupSamples
    )
    {
        if (definition.MinimumWarmupOperations is not int minimumWarmupOperations)
        {
            return profileWarmupSamples;
        }

        if (minimumWarmupOperations <= 0)
        {
            throw new InvalidDataException(
                $"Workload '{definition.Id}' has a non-positive minimum warmup operation count.");
        }

        // Batched in-memory workloads can exhaust the profile warmup before
        // tiered JIT promotes their hot paths. The contract-owned operation
        // floor keeps that transition outside the measured tail percentiles.
        var operationBoundSamples = checked((minimumWarmupOperations + definition.OperationsPerSample - 1)
            / definition.OperationsPerSample);

        return Math.Max(profileWarmupSamples, operationBoundSamples);
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
        int minimumSampleCount,
        int minimumMeasurementDurationMilliseconds,
        string calibrationKind,
        int calibrationSamplesPerPulse,
        int calibrationIntervalSamples,
        double maximumCalibrationRelativeStandardError,
        CancellationToken cancellationToken
    )
    {
        if (minimumSampleCount <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive sample count.");
        }

        if (minimumMeasurementDurationMilliseconds < 0)
        {
            throw new InvalidDataException(
                $"Workload '{workload.Id}' has a negative minimum measurement duration.");
        }

        if (definition.OperationsPerSample <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive operations-per-sample value.");
        }

        if (calibrationSamplesPerPulse <= 0
            || calibrationIntervalSamples <= 0)
        {
            throw new InvalidDataException(
                $"Workload '{workload.Id}' has an invalid calibration profile.");
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
        var samples = new List<double>(minimumSampleCount);
        var calibrationSamples = new List<double>(minimumSampleCount);
        var calibrationPulses = new List<double>();
        var calibrationPulseIndices = new List<int>(minimumSampleCount);
        var normalizedSamples = new List<double>(minimumSampleCount);
        var minimumMeasurementTicks = checked(
            (long)Math.Ceiling(
                minimumMeasurementDurationMilliseconds
                * Stopwatch.Frequency
                / 1000d));
        long measuredTicks = 0;
        long allocatedBytes = 0;
        var gen0Collections = 0;
        var gen1Collections = 0;
        var gen2Collections = 0;
        long checksum = 0;
        double currentCalibrationNanoseconds = 0;

        while (samples.Count < minimumSampleCount
               || measuredTicks < minimumMeasurementTicks)
        {
            if (samples.Count % calibrationIntervalSamples == 0)
            {
                currentCalibrationNanoseconds = await PerformanceCalibration
                    .MeasurePulseAsync(
                        calibrationKind,
                        calibrationSamplesPerPulse,
                        cancellationToken)
                    .ConfigureAwait(false);
                calibrationPulses.Add(currentCalibrationNanoseconds);
            }

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
            measuredTicks = checked(measuredTicks + elapsed);
            var nanoseconds = elapsed * (1_000_000_000d / Stopwatch.Frequency) / definition.OperationsPerSample;

            if (!double.IsFinite(nanoseconds)
                || nanoseconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Workload '{workload.Id}' produced an invalid elapsed time of {nanoseconds} ns.");
            }

            samples.Add(nanoseconds);
            calibrationSamples.Add(currentCalibrationNanoseconds);
            calibrationPulseIndices.Add(calibrationPulses.Count - 1);
            normalizedSamples.Add(nanoseconds / currentCalibrationNanoseconds);
        }

        // Use the same finalizer-draining boundary on both sides. Otherwise,
        // pending finalizable objects can be misreported as retained workload
        // memory even though their owners were disposed by CleanupAsync.
        var retainedAfter = ReadManagedHeapSizeAfterFullCollection();
        var retainedBytes = Math.Max(0, retainedAfter - retainedBefore);
        var measuredSampleCount = samples.Count;
        var measuredOperations = checked((long)measuredSampleCount * definition.OperationsPerSample);
        var sortedSamples = samples
            .Order()
            .ToArray();
        var sortedCalibrationPulses = calibrationPulses
            .Order()
            .ToArray();
        var sortedNormalizedSamples = normalizedSamples
            .Order()
            .ToArray();
        var calibrationMedian = Percentile(sortedCalibrationPulses, 0.5);
        var calibrationStandardError = StandardError(calibrationPulses);
        var calibrationRelativeStandardError = calibrationStandardError / calibrationMedian;

        if (calibrationRelativeStandardError > maximumCalibrationRelativeStandardError)
        {
            throw new InvalidOperationException(
                $"Workload '{workload.Id}' calibration relative standard error "
                + $"{calibrationRelativeStandardError:F6} exceeds "
                + $"{maximumCalibrationRelativeStandardError:F6}.");
        }

        return new PerformanceWorkloadResult
        {
            Id = definition.Id,
            Family = definition.Family,
            WarmupSamples = warmupSamples,
            SampleCount = measuredSampleCount,
            OperationsPerSample = definition.OperationsPerSample,
            Checksum = checksum,
            MeasuredUtc = DateTimeOffset.UtcNow,
            MedianNanoseconds = Percentile(sortedSamples, 0.5),
            P95Nanoseconds = Percentile(sortedSamples, 0.95),
            P99Nanoseconds = Percentile(sortedSamples, 0.99),
            StandardErrorNanoseconds = StandardError(samples),
            CalibrationKind = calibrationKind,
            CalibrationMedianNanoseconds = calibrationMedian,
            CalibrationStandardErrorNanoseconds = calibrationStandardError,
            NormalizedMedian = Percentile(sortedNormalizedSamples, 0.5),
            NormalizedP95 = Percentile(sortedNormalizedSamples, 0.95),
            NormalizedP99 = Percentile(sortedNormalizedSamples, 0.99),
            AllocatedBytesPerOperation = allocatedBytes / measuredOperations,
            RetainedBytes = retainedBytes,
            Gen0CollectionsPer1000 = gen0Collections * 1000d / measuredOperations,
            Gen1CollectionsPer1000 = gen1Collections * 1000d / measuredOperations,
            Gen2CollectionsPer1000 = gen2Collections * 1000d / measuredOperations,
            SamplesNanoseconds = samples,
            CalibrationNanoseconds = calibrationSamples,
            CalibrationPulseNanoseconds = calibrationPulses,
            CalibrationPulseIndices = calibrationPulseIndices,
            NormalizedSamples = normalizedSamples,
        };
    }

    private static bool TryLoadCheckpoint(
        string? checkpointDirectory,
        string contractVersion,
        string runId,
        string profile,
        string commit,
        string sourceHash,
        string runnerClass,
        PerformanceWorkloadDefinition definition,
        out PerformanceWorkloadResult result
    )
    {
        result = new PerformanceWorkloadResult();

        if (string.IsNullOrWhiteSpace(checkpointDirectory))
        {
            return false;
        }

        var path = GetCheckpointPath(checkpointDirectory, definition.Id);
        if (!File.Exists(path))
        {
            return false;
        }

        PerformanceWorkloadCheckpoint checkpoint;

        try
        {
            checkpoint = PerformanceReportWriter.Read<PerformanceWorkloadCheckpoint>(path);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException(
                $"Performance checkpoint '{path}' cannot be read.",
                exception);
        }

        if (checkpoint.SchemaVersion != 1
            || !string.Equals(
                checkpoint.Kind,
                "performance-workload-checkpoint",
                StringComparison.Ordinal)
            || !string.Equals(checkpoint.ContractVersion, contractVersion, StringComparison.Ordinal)
            || !string.Equals(checkpoint.RunId, runId, StringComparison.Ordinal)
            || !string.Equals(checkpoint.Target, BenchmarkEnvironment.TargetIdValue, StringComparison.Ordinal)
            || !string.Equals(checkpoint.Profile, profile, StringComparison.Ordinal)
            || !string.Equals(checkpoint.Commit, commit, StringComparison.Ordinal)
            || !string.Equals(checkpoint.SourceHash, sourceHash, StringComparison.Ordinal)
            || !string.Equals(checkpoint.RunnerClass, runnerClass, StringComparison.Ordinal)
            || !string.Equals(checkpoint.Workload.Id, definition.Id, StringComparison.Ordinal)
            || !string.Equals(checkpoint.Workload.Family, definition.Family, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Performance checkpoint '{path}' does not match the current run identity.");
        }

        result = checkpoint.Workload;
        return true;
    }

    private static async Task WriteCheckpointAsync(
        string? checkpointDirectory,
        string contractVersion,
        string runId,
        string profile,
        string commit,
        string sourceHash,
        string runnerClass,
        PerformanceWorkloadResult result
    )
    {
        if (string.IsNullOrWhiteSpace(checkpointDirectory))
        {
            return;
        }

        var checkpoint = new PerformanceWorkloadCheckpoint
        {
            ContractVersion = contractVersion,
            RunId = runId,
            Target = BenchmarkEnvironment.TargetIdValue,
            Profile = profile,
            Commit = commit,
            SourceHash = sourceHash,
            RunnerClass = runnerClass,
            Workload = result,
        };
        var path = GetCheckpointPath(checkpointDirectory, result.Id);

        // Persisting a completed result is intentionally independent from the
        // elapsed run deadline. The small atomic write makes the next run lose
        // at most the workload that was still executing at cancellation time.
        await PerformanceReportWriter
            .WriteAsync(path, checkpoint, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static string GetCheckpointPath(
        string checkpointDirectory,
        string workloadId
    )
    {
        var digest = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(workloadId));
        var fileName = $"{Convert.ToHexStringLower(digest)}.json";

        return Path.Combine(checkpointDirectory, fileName);
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
