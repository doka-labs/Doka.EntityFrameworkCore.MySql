namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Executes named provider workloads outside BenchmarkDotNet so release evidence
/// retains per-operation-normalized samples for median, p95, and p99
/// evaluation. Contract-owned batches amortize timer overhead; paired runs use
/// a bounded pilot to keep sample duration independent from runner speed.
/// </summary>
internal static class PerformanceWorkloadRunner
{
    // Termination reasons are part of the evidence contract that the Python
    // policy layer reads, so they are string constants rather than an enum
    // whose serialized form could drift.
    internal const string PrecisionReached = "precision_reached";

    internal const string SampleCapReached = "sample_cap_reached";

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
            var calibrationKind = PerformanceCalibration.ResolveKind(
                contract.Calibration,
                definition.Family);
            // Named policies keep hang detection centrally reviewable. These
            // deadlines never replace the post-measurement performance budgets.
            var timeoutPolicySeconds = definition.TimeoutPolicy is null
                ? 0
                : contract.TimeoutPolicies.TryGetValue(
                    definition.TimeoutPolicy,
                    out var timeoutPolicy)
                    ? timeoutPolicy.MinimumWorkloadTimeoutSeconds
                    : throw new InvalidDataException(
                        $"Performance workload '{definition.Id}' references unknown "
                        + $"timeout policy '{definition.TimeoutPolicy}'.");
            var workloadTimeoutSeconds = Math.Max(
                profile.MaximumWorkloadDurationSeconds,
                timeoutPolicySeconds);
            using var workloadTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                runCancellationToken);
            workloadTimeoutSource.CancelAfter(
                TimeSpan.FromSeconds(workloadTimeoutSeconds));
            PerformanceWorkloadResult result;

            try
            {
                result = await MeasureAsync(
                        workload,
                        definition,
                        profile.WarmupSamples,
                        sampleCount,
                        profile.MinimumMeasurementDurationMilliseconds,
                        profile.MaximumMeasurementSampleMultiplier,
                        profile.AdaptiveOperationsPerSample,
                        profile.OperationBatchingDurationHeadroomPercent,
                        profile.OperationBatchingPilotSamples,
                        profile.MaximumOperationsPerSampleMultiplier,
                        profile.MaximumRelativeStandardError,
                        calibrationKind,
                        profile.CalibrationSamplesPerPulse,
                        profile.CalibrationIntervalSamples,
                        profile.MaximumCalibrationRelativeStandardError,
                        profile.MeasurementQualityPolicy,
                        workloadTimeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                if (runTimeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Performance workload matrix exceeded its "
                        + $"{profile.MaximumWorkloadMatrixDurationSeconds}-second deadline while running "
                        + $"'{definition.Id}'.",
                        exception);
                }

                if (workloadTimeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Performance workload '{definition.Id}' exceeded its "
                        + $"{workloadTimeoutSeconds}-second deadline for profile '{profileName}'.",
                        exception);
                }

                throw;
            }

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

    internal static async Task<PerformanceWorkloadResult> MeasureAsync(
        PerformanceWorkload workload,
        PerformanceWorkloadDefinition definition,
        int profileWarmupSamples,
        int minimumSampleCount,
        int minimumMeasurementDurationMilliseconds,
        int maximumMeasurementSampleMultiplier,
        bool adaptiveOperationsPerSample,
        int operationBatchingDurationHeadroomPercent,
        int operationBatchingPilotSamples,
        int maximumOperationsPerSampleMultiplier,
        double maximumRelativeStandardError,
        string calibrationKind,
        int calibrationSamplesPerPulse,
        int calibrationIntervalSamples,
        double maximumCalibrationRelativeStandardError,
        string measurementQualityPolicy,
        CancellationToken cancellationToken,
        IPerformanceMeasurementSource? measurementSource = null
    )
    {
        measurementSource ??= RuntimePerformanceMeasurementSource.Instance;

        if (minimumSampleCount <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive sample count.");
        }

        if (minimumMeasurementDurationMilliseconds < 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a negative minimum measurement duration.");
        }

        if (maximumMeasurementSampleMultiplier <= 0
            || maximumRelativeStandardError < 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has an invalid adaptive sampling profile.");
        }

        if (definition.OperationsPerSample <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has a non-positive operations-per-sample value.");
        }

        if (adaptiveOperationsPerSample
            && (operationBatchingDurationHeadroomPercent < 100
                || operationBatchingPilotSamples <= 0
                || maximumOperationsPerSampleMultiplier <= 0))
        {
            throw new InvalidDataException(
                $"Workload '{workload.Id}' has an invalid adaptive operation-batching profile.");
        }

        var warmupSamples = GetWarmupSampleCount(
            definition,
            profileWarmupSamples);

        if (calibrationSamplesPerPulse <= 0
            || calibrationIntervalSamples <= 0)
        {
            throw new InvalidDataException($"Workload '{workload.Id}' has an invalid calibration profile.");
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

        var operationsPerSample = definition.OperationsPerSample;
        var pilotSamplesElapsedTicks = Array.Empty<long>();
        var operationBatchingMode = "fixed";

        if (adaptiveOperationsPerSample)
        {
            // Pilot only after warmup. Measuring the first invocation would
            // size steady-state samples from JIT and cold-start work instead.
            var targetSampleTicks = PerformanceSampling.ResolveTargetSampleTicks(
                minimumMeasurementDurationMilliseconds,
                measurementSource.TimestampFrequency,
                operationBatchingDurationHeadroomPercent,
                minimumSampleCount);
            pilotSamplesElapsedTicks = await MeasurePilotSamplesAsync(
                    workload,
                    definition.OperationsPerSample,
                    operationBatchingPilotSamples,
                    measurementSource,
                    cancellationToken)
                .ConfigureAwait(false);
            operationsPerSample = PerformanceSampling.ResolveOperationsPerSample(
                definition.OperationsPerSample,
                PerformanceSampling.ResolvePilotElapsedTicks(pilotSamplesElapsedTicks),
                targetSampleTicks,
                maximumOperationsPerSampleMultiplier);
            operationBatchingMode = "pilot";
        }

        var retainedBefore = ReadManagedHeapSizeAfterFullCollection();
        var samples = new List<double>(minimumSampleCount);
        var calibrationSamples = new List<double>(minimumSampleCount);
        var calibrationPulses = new List<double>();
        var calibrationPulseIndices = new List<int>(minimumSampleCount);
        var normalizedSamples = new List<double>(minimumSampleCount);
        var minimumMeasurementTicks =
            checked((long)Math.Ceiling(
                minimumMeasurementDurationMilliseconds * measurementSource.TimestampFrequency / 1000d));

        var maximumSampleCount = checked(minimumSampleCount * maximumMeasurementSampleMultiplier);
        var requiredSampleCount = minimumSampleCount;
        var terminationReason = PrecisionReached;
        var minimumDurationReached = false;
        long measuredTicks = 0;
        long allocatedBytes = 0;
        var gen0Collections = 0;
        var gen1Collections = 0;
        var gen2Collections = 0;
        long checksum = 0;
        double currentCalibrationNanoseconds = 0;

        while (true)
        {
            // The cap bounds sampling unconditionally. Letting the minimum
            // duration push past it produced a sample count the adaptive
            // decision below rejects, which surfaced as a crash instead of as
            // a measurement outcome.
            while (PerformanceSampling.ShouldCollectAnotherSample(
                       samples.Count,
                       requiredSampleCount,
                       measuredTicks,
                       minimumMeasurementTicks,
                       maximumSampleCount))
            {
                if (samples.Count % calibrationIntervalSamples == 0)
                {
                    currentCalibrationNanoseconds = await PerformanceCalibration
                        .MeasurePulseAsync(calibrationKind, calibrationSamplesPerPulse, cancellationToken)
                        .ConfigureAwait(false);
                    calibrationPulses.Add(currentCalibrationNanoseconds);
                }

                await PrepareAsync(workload, cancellationToken)
                    .ConfigureAwait(false);

                var allocatedBefore = measurementSource.GetTotalAllocatedBytes();
                var gen0Before = GC.CollectionCount(0);
                var gen1Before = GC.CollectionCount(1);
                var gen2Before = GC.CollectionCount(2);
                var started = measurementSource.GetTimestamp();
                long sampleChecksum = 0;
                long elapsed;

                try
                {
                    for (var operation = 0; operation < operationsPerSample; operation++)
                    {
                        sampleChecksum = unchecked(sampleChecksum
                            + await workload
                                .ExecuteAsync(cancellationToken)
                                .ConfigureAwait(false));
                    }

                    elapsed = measurementSource.GetTimestamp() - started;
                    allocatedBytes += measurementSource.GetTotalAllocatedBytes() - allocatedBefore;
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
                var nanoseconds = elapsed
                    * (1_000_000_000d / measurementSource.TimestampFrequency)
                    / operationsPerSample;

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

            var relativeStandardError = PerformanceSampling.RelativeStandardError(normalizedSamples);
            (terminationReason, minimumDurationReached) = PerformanceSampling.ClassifyTermination(
                samples.Count,
                maximumSampleCount,
                measuredTicks,
                minimumMeasurementTicks,
                relativeStandardError,
                maximumRelativeStandardError);

            if (samples.Count >= maximumSampleCount)
            {
                break;
            }

            var nextSampleTarget = PerformanceSampling.NextSampleTarget(
                samples.Count,
                maximumSampleCount,
                calibrationIntervalSamples,
                relativeStandardError,
                maximumRelativeStandardError);

            if (nextSampleTarget == samples.Count)
            {
                break;
            }

            // Preserve every observation and extend in calibration-aligned
            // blocks. This improves statistical precision without hiding
            // scheduler or database variance through outlier deletion.
            Console.WriteLine(
                $"Extending workload {workload.Id} from {samples.Count} to "
                + $"{nextSampleTarget} samples because relative standard error "
                + $"{relativeStandardError:F6} exceeds {maximumRelativeStandardError:F6}.");
            requiredSampleCount = nextSampleTarget;
        }

        // Use the same finalizer-draining boundary on both sides. Otherwise,
        // pending finalizable objects can be misreported as retained workload
        // memory even though their owners were disposed by CleanupAsync.
        var retainedAfter = ReadManagedHeapSizeAfterFullCollection();
        var retainedBytes = Math.Max(0, retainedAfter - retainedBefore);
        var measuredSampleCount = samples.Count;
        var measuredOperations = checked((long)measuredSampleCount * operationsPerSample);
        var sortedSamples = samples
            .Order()
            .ToArray();

        var sortedCalibrationPulses = calibrationPulses
            .Order()
            .ToArray();

        var sortedNormalizedSamples = normalizedSamples
            .Order()
            .ToArray();

        var calibrationMedian = PerformanceSampling.Percentile(sortedCalibrationPulses, 0.5);
        var calibrationStandardError = PerformanceSampling.StandardError(calibrationPulses);
        var calibrationRelativeStandardError = calibrationStandardError / calibrationMedian;

        if (calibrationRelativeStandardError > maximumCalibrationRelativeStandardError)
        {
            var diagnostic =
                $"Workload '{workload.Id}' calibration relative standard error "
                + $"{calibrationRelativeStandardError:F6} exceeds "
                + $"{maximumCalibrationRelativeStandardError:F6}.";

            // A calibration that will not settle describes the machine, not the
            // provider. Throwing an ordinary exception here exits 1, which the
            // attempt path classifies as a regression and refuses to retry, so
            // a busy runner could convict a provider it never measured.
            if (string.Equals(measurementQualityPolicy, "enforce", StringComparison.Ordinal))
            {
                throw new MeasurementQualityException(diagnostic);
            }

            Console.Error.WriteLine($"Measurement quality observation: {diagnostic}");
        }

        return new PerformanceWorkloadResult
        {
            Id = definition.Id,
            Family = definition.Family,
            WarmupSamples = warmupSamples,
            SampleCount = measuredSampleCount,
            TerminationReason = terminationReason,
            MinimumDurationReached = minimumDurationReached,
            ConfiguredOperationsPerSample = definition.OperationsPerSample,
            OperationBatchingMode = operationBatchingMode,
            PilotSamplesElapsedTicks = pilotSamplesElapsedTicks,
            OperationsPerSample = operationsPerSample,
            Checksum = checksum,
            MeasuredUtc = DateTimeOffset.UtcNow,
            MedianNanoseconds = PerformanceSampling.Percentile(sortedSamples, 0.5),
            P95Nanoseconds = PerformanceSampling.Percentile(sortedSamples, 0.95),
            P99Nanoseconds = PerformanceSampling.Percentile(sortedSamples, 0.99),
            StandardErrorNanoseconds = PerformanceSampling.StandardError(samples),
            CalibrationKind = calibrationKind,
            CalibrationMedianNanoseconds = calibrationMedian,
            CalibrationStandardErrorNanoseconds = calibrationStandardError,
            NormalizedMedian = PerformanceSampling.Percentile(sortedNormalizedSamples, 0.5),
            NormalizedP95 = PerformanceSampling.Percentile(sortedNormalizedSamples, 0.95),
            NormalizedP99 = PerformanceSampling.Percentile(sortedNormalizedSamples, 0.99),
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

    private static async Task<long[]> MeasurePilotSamplesAsync(
        PerformanceWorkload workload,
        int operationsPerSample,
        int pilotSampleCount,
        IPerformanceMeasurementSource measurementSource,
        CancellationToken cancellationToken
    )
    {
        var samples = new long[pilotSampleCount];

        for (var sample = 0; sample < samples.Length; sample++)
        {
            await PrepareAsync(workload, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var started = measurementSource.GetTimestamp();

                for (var operation = 0; operation < operationsPerSample; operation++)
                {
                    _ = await workload
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                var elapsed = measurementSource.GetTimestamp() - started;

                if (elapsed <= 0)
                {
                    throw new InvalidOperationException(
                        $"Workload '{workload.Id}' produced a non-positive pilot duration.");
                }

                samples[sample] = elapsed;
            }
            finally
            {
                await CleanupAsync(workload, cancellationToken)
                    .ConfigureAwait(false);
            }

        }

        return samples;
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

    private static List<PerformanceWorkloadDefinition> ApplicableDefinitions(
        PerformanceContract contract,
        string profileName
    )
    {
        // Only the smoke profile narrows the workload set. Naming it positively
        // keeps a profile that is not recognized here from silently inheriting
        // the narrow set while its evidence claims the complete matrix.
        var narrowsToSmoke = string.Equals(
            profileName,
            BenchmarkProfiles.SmokeProfile,
            StringComparison.Ordinal);
        var definitions = contract
            .Workloads.Where(definition => !narrowsToSmoke || definition.Smoke)
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
