namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Runs sustained-use resource invariants that latency microbenchmarks cannot
/// prove: bounded caches, buffer ownership, connection and advisory-lock cleanup,
/// working-set stabilization, and concurrent throughput retention.
/// </summary>
internal static class PerformanceSoakRunner
{
    public static async Task<int> RunAsync(
        string outputPath,
        CancellationToken cancellationToken = default
    )
    {
        var contract = PerformanceContract.Load();
        var profileName = BenchmarkProfiles.Current;
        var profile = contract.Profiles.TryGetValue(profileName, out var configuredProfile)
            ? configuredProfile
            : throw new InvalidDataException($"Performance contract does not define profile '{profileName}'.");

        var results = new List<SoakScenarioResult>();

        BenchmarkEnvironment.EnsureInitialized();

        results.Add(
            await RunScenarioAsync(
                    "soak.hilo-cache-bound",
                    () => RunHiLoCacheBound(profile.SoakIterations, contract.SoakBudgets),
                    cancellationToken)
                .ConfigureAwait(false));
        results.Add(
            await RunScenarioAsync(
                    "soak.pooled-buffer-return",
                    () => RunPooledBufferReturn(profile.SoakIterations, contract.SoakBudgets),
                    cancellationToken)
                .ConfigureAwait(false));
        results.Add(
            await RunScenarioAsync(
                    "soak.connection-cleanup",
                    () => RunConnectionCleanupAsync(profile.SoakIterations, contract.SoakBudgets, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false));
        results.Add(
            await RunScenarioAsync(
                    "soak.migration-lock-cleanup",
                    () => RunMigrationLockCleanupAsync(profile.SoakIterations, contract.SoakBudgets, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false));
        results.Add(
            await RunScenarioAsync(
                    "soak.working-set-stabilization",
                    () => RunWorkingSetStabilizationAsync(
                        profile.SoakIterations,
                        contract.SoakBudgets,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false));
        results.Add(
            await RunScenarioAsync(
                    "soak.concurrent-throughput-retention",
                    () => RunThroughputRetentionAsync(
                        profile.SoakIterations,
                        profile.SoakConcurrency,
                        contract.SoakBudgets,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false));

        var report = new SoakRunReport
        {
            ContractVersion = contract.ContractVersion,
            RunId = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUN_ID"),
            Target = BenchmarkEnvironment.TargetIdValue,
            Profile = profileName,
            Commit = RequiredEnvironmentVariable("DOKA_BENCHMARK_COMMIT"),
            SourceHash = RequiredEnvironmentVariable("DOKA_BENCHMARK_SOURCE_HASH"),
            RunnerClass = RequiredEnvironmentVariable("DOKA_BENCHMARK_RUNNER_CLASS"),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Success = results.All(result => result.Success),
            Scenarios = results,
        };

        await PerformanceReportWriter
            .WriteAsync(outputPath, report, cancellationToken)
            .ConfigureAwait(false);

        return report.Success ? 0 : 1;
    }

    private static SoakScenarioResult RunHiLoCacheBound(
        int iterations,
        SoakBudgetContract budgets
    )
    {
        MySqlHiLoStateCache.ResetForTesting();

        for (var index = 0; index < Math.Max(iterations, MySqlHiLoStateCache.Capacity * 2); index++)
        {
            // The soak varies logical databases while retaining one synthetic
            // socket endpoint, so endpoint identity is not part of the workload.
            var identity = new MySqlDatabaseIdentity(
                "benchmark-server",
                3306,
                $"tenant-{index}",
                "benchmark-user",
                MySqlConnectionProtocol.Sockets,
                string.Empty);

            _ = MySqlHiLoStateCache.GetOrCreate(identity, "benchmark-sequence", blockSize: 32);
        }

        var count = MySqlHiLoStateCache.Count;
        MySqlHiLoStateCache.ResetForTesting();

        return Result(
            "soak.hilo-cache-bound",
            count <= budgets.HiloCacheMaximumEntries,
            new Dictionary<string, double>
            {
                ["cacheEntries"] = count,
            },
            new Dictionary<string, double>
            {
                ["maximumCacheEntries"] = budgets.HiloCacheMaximumEntries,
            });
    }

    private static SoakScenarioResult RunPooledBufferReturn(
        int iterations,
        SoakBudgetContract budgets
    )
    {
        var pool = new TrackingArrayPool();

        Parallel.For(
            0,
            iterations,
            index =>
            {
                using var stream = new MySqlJsonValueComparers.PooledByteBufferStream(pool, initialCapacity: 32);
                var bytes = new byte[1024 + (index % 8192)];
                stream.Write(bytes);
            });

        return Result(
            "soak.pooled-buffer-return",
            pool.OutstandingCount <= budgets.PooledBufferMaximumOutstanding && pool.RentCount == pool.ReturnCount,
            new Dictionary<string, double>
            {
                ["rentCount"] = pool.RentCount,
                ["returnCount"] = pool.ReturnCount,
                ["outstandingBuffers"] = pool.OutstandingCount,
            },
            new Dictionary<string, double>
            {
                ["maximumOutstandingBuffers"] = budgets.PooledBufferMaximumOutstanding,
            });
    }

    private static async Task<SoakScenarioResult> RunConnectionCleanupAsync(
        int iterations,
        SoakBudgetContract budgets,
        CancellationToken cancellationToken
    )
    {
        await MySqlConnection
            .ClearAllPoolsAsync(cancellationToken)
            .ConfigureAwait(false);

        var connectionString = BenchmarkEnvironment.CreateConnectionString(
            BenchmarkEnvironment.DatabaseNameValue,
            pooling: false);

        await using var observer = new MySqlConnection(connectionString);

        await observer
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var before = await ReadThreadsConnectedAsync(observer, cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < iterations; index++)
        {
            await using var connection = new MySqlConnection(connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";

            _ = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var after = await ReadThreadsConnectedAsync(observer, cancellationToken)
            .ConfigureAwait(false);

        var delta = after - before;

        return Result(
            "soak.connection-cleanup",
            delta <= budgets.ConnectionMaximumDelta,
            new Dictionary<string, double>
            {
                ["threadsConnectedBefore"] = before,
                ["threadsConnectedAfter"] = after,
                ["connectionDelta"] = delta,
            },
            new Dictionary<string, double>
            {
                ["maximumConnectionDelta"] = budgets.ConnectionMaximumDelta,
            });
    }

    private static async Task<SoakScenarioResult> RunMigrationLockCleanupAsync(
        int iterations,
        SoakBudgetContract budgets,
        CancellationToken cancellationToken
    )
    {
        var connectionString = BenchmarkEnvironment.CreateConnectionString(BenchmarkEnvironment.DatabaseNameValue);
        var lockName = MySqlAdvisoryLockNaming.BuildLockName(connectionString);

        for (var index = 0; index < iterations; index++)
        {
            await using var context = BenchmarkEnvironment.CreateContext();
            var historyRepository = context.GetService<IHistoryRepository>();

            await using var migrationLock = await historyRepository
                .AcquireDatabaseLockAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using var connection = new MySqlConnection(connectionString);

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IS_USED_LOCK(@name);";
        command.Parameters.AddWithValue("@name", lockName);

        var holder = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        var heldLockCount = holder is null || holder is DBNull ? 0 : 1;

        return Result(
            "soak.migration-lock-cleanup",
            heldLockCount <= budgets.MigrationLockMaximumHeld,
            new Dictionary<string, double>
            {
                ["heldLocks"] = heldLockCount,
            },
            new Dictionary<string, double>
            {
                ["maximumHeldLocks"] = budgets.MigrationLockMaximumHeld,
            });
    }

    private static async Task<SoakScenarioResult> RunWorkingSetStabilizationAsync(
        int iterations,
        SoakBudgetContract budgets,
        CancellationToken cancellationToken
    )
    {
        const int windowCount = 8;
        var operationsPerWindow = Math.Max(1, iterations / windowCount);
        var workingSetSamples = new List<long>(windowCount);
        var managedHeapSamples = new List<long>(windowCount);
        var payload = JsonNode.Parse("""{"items":[{"id":1,"value":"benchmark"},{"id":2,"value":"provider"}]}""")
            ?? throw new InvalidDataException("The working-set payload is null.");

        var comparer = MySqlJsonValueComparers.JsonNodeComparer;

        for (var window = 0; window < windowCount; window++)
        {
            for (var operation = 0; operation < operationsPerWindow; operation++)
            {
                _ = comparer.GetHashCode(payload);

                if (operation % 32 == 0)
                {
                    await using var context = BenchmarkEnvironment.CreateContext();
                    _ = await context
                        .BasicEntities.AsNoTracking()
                        .Take(10)
                        .CountAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            ForceFullCollection();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            workingSetSamples.Add(process.WorkingSet64);
            managedHeapSamples.Add(GC.GetTotalMemory(forceFullCollection: false));
        }

        var workingSetGrowth = Math.Max(0, workingSetSamples[^1] - workingSetSamples[0]);
        var managedHeapGrowth = Math.Max(0, managedHeapSamples[^1] - managedHeapSamples[0]);

        return Result(
            "soak.working-set-stabilization",
            workingSetGrowth <= budgets.WorkingSetMaximumGrowthBytes
            && managedHeapGrowth <= budgets.ManagedHeapMaximumGrowthBytes,
            new Dictionary<string, double>
            {
                ["workingSetFirstBytes"] = workingSetSamples[0],
                ["workingSetLastBytes"] = workingSetSamples[^1],
                ["workingSetGrowthBytes"] = workingSetGrowth,
                ["managedHeapFirstBytes"] = managedHeapSamples[0],
                ["managedHeapLastBytes"] = managedHeapSamples[^1],
                ["managedHeapGrowthBytes"] = managedHeapGrowth,
            },
            new Dictionary<string, double>
            {
                ["maximumWorkingSetGrowthBytes"] = budgets.WorkingSetMaximumGrowthBytes,
                ["maximumManagedHeapGrowthBytes"] = budgets.ManagedHeapMaximumGrowthBytes,
            });
    }

    private static async Task<SoakScenarioResult> RunThroughputRetentionAsync(
        int iterations,
        int concurrency,
        SoakBudgetContract budgets,
        CancellationToken cancellationToken
    )
    {
        const int windowCount = 8;
        var operationsPerWindow = Math.Max(concurrency, iterations / windowCount);
        var throughputSamples = new List<double>(windowCount);

        for (var window = 0; window < windowCount; window++)
        {
            var started = Stopwatch.GetTimestamp();

            for (var offset = 0; offset < operationsPerWindow; offset += concurrency)
            {
                var operations = Math.Min(concurrency, operationsPerWindow - offset);
                var tasks = Enumerable
                    .Range(0, operations)
                    .Select(_ => ExecuteConcurrentQueryAsync(cancellationToken))
                    .ToArray();

                await Task
                    .WhenAll(tasks)
                    .ConfigureAwait(false);
            }

            var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            throughputSamples.Add(operationsPerWindow / seconds);
        }

        var initialThroughput = throughputSamples
            .Take(2)
            .Average();

        var finalThroughput = throughputSamples
            .TakeLast(2)
            .Average();

        var retentionRatio = finalThroughput / initialThroughput;

        return Result(
            "soak.concurrent-throughput-retention",
            retentionRatio >= budgets.MinimumThroughputRetentionRatio,
            new Dictionary<string, double>
            {
                ["initialOperationsPerSecond"] = initialThroughput,
                ["finalOperationsPerSecond"] = finalThroughput,
                ["throughputRetentionRatio"] = retentionRatio,
            },
            new Dictionary<string, double>
            {
                ["minimumThroughputRetentionRatio"] = budgets.MinimumThroughputRetentionRatio,
            });
    }

    private static async Task<int> ExecuteConcurrentQueryAsync(
        CancellationToken cancellationToken
    )
    {
        await using var context = BenchmarkEnvironment.CreateContext();
        return await context
            .BasicEntities.AsNoTracking()
            .Where(entity => entity.CreatedAt.Year == 2024)
            .Take(100)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> ReadThreadsConnectedAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW STATUS LIKE 'Threads_connected';";
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException("The server did not report Threads_connected.");
        }

        return int.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
    }

    private static async Task<SoakScenarioResult> RunScenarioAsync(
        string id,
        Func<SoakScenarioResult> scenario,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task
                .Run(scenario, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failed(id, exception);
        }
    }

    private static async Task<SoakScenarioResult> RunScenarioAsync(
        string id,
        Func<Task<SoakScenarioResult>> scenario,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await scenario()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failed(id, exception);
        }
    }

    private static SoakScenarioResult Result(
        string id,
        bool success,
        Dictionary<string, double> metrics,
        Dictionary<string, double> budgets
    ) => new()
    {
        Id = id,
        Success = success,
        Metrics = metrics,
        Budgets = budgets,
        Error = success ? null : "One or more sustained-use budgets were exceeded.",
    };

    private static SoakScenarioResult Failed(
        string id,
        Exception exception
    ) => new()
    {
        Id = id,
        Success = false,
        Error = exception.ToString(),
    };

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

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly ConcurrentDictionary<byte[], byte> _outstanding = new(ReferenceEqualityComparer.Instance);
        private int _rentCount;
        private int _returnCount;

        public int RentCount => Volatile.Read(ref _rentCount);

        public int ReturnCount => Volatile.Read(ref _returnCount);

        public int OutstandingCount => _outstanding.Count;

        public override byte[] Rent(
            int minimumLength
        )
        {
            var buffer = new byte[Math.Max(minimumLength, 1)];

            if (!_outstanding.TryAdd(buffer, 0))
            {
                throw new InvalidOperationException("The tracking pool observed a duplicate rented buffer.");
            }

            _ = Interlocked.Increment(ref _rentCount);

            return buffer;
        }

        public override void Return(
            byte[] array,
            bool clearArray = false
        )
        {
            if (!_outstanding.TryRemove(array, out _))
            {
                throw new InvalidOperationException(
                    "A buffer was returned more than once or did not originate from the tracking pool.");
            }

            _ = Interlocked.Increment(ref _returnCount);
        }
    }
}
