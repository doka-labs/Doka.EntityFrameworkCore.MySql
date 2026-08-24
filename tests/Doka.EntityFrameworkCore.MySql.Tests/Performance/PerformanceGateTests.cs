namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class PerformanceGateTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Driver_rejects_unsuccessful_BenchmarkDotNet_reports(
        bool success,
        bool expectedFailure
    )
    {
        var report = new BenchmarkReport(
            success,
            benchmarkCase: null!,
            generateResult: null!,
            buildResult: null!,
            executeResults: [],
            metrics: []);

        Assert.Equal(
            expectedFailure,
            Benchmarks.Program.HasFailedBenchmarkReports([report]));
    }

    [Fact]
    public void Provider_workload_benchmark_is_compatible_with_BenchmarkDotNet() => Assert.False(
        typeof(Benchmarks.ProviderWorkloadBenchmarks).IsSealed);

    [Fact]
    public void Gate_accepts_complete_finite_BenchmarkDotNet_evidence_at_the_budgets()
    {
        using var fixture = PerformanceGateFixture.Create();

        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        Assert.Empty(result.InvalidEvidence);
        Assert.Empty(result.Regressions);
        Assert.Equal(1, result.WorkloadCount);
        Assert.Equal(1, result.ControlCount);
    }

    [Fact]
    public void Gate_reports_a_budget_regression_separately_from_invalid_evidence()
    {
        using var fixture = PerformanceGateFixture.Create(medianBudget: 4);

        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        Assert.Empty(result.InvalidEvidence);
        Assert.Contains(result.Regressions, error => error.Contains("medianNanoseconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_rejects_a_report_bound_to_another_target()
    {
        using var fixture = PerformanceGateFixture.Create(reportTarget: "mariadb114");

        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null);

        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains("expected 'mysql84'", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_rejects_statistics_that_do_not_match_raw_samples()
    {
        using var fixture = PerformanceGateFixture.Create(sampleCountOffset: 1);

        Assert.Throws<InvalidDataException>(() => BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "smoke",
            soakPath: null));
    }

    [Fact]
    public void Gate_recomputes_managed_heap_soak_budget_from_the_raw_metric()
    {
        using var fixture = PerformanceGateFixture.Create();
        var soakPath = fixture.WriteSoak(managedHeapGrowthBytes: 2048);

        var result = BenchmarkPerformanceGate.Evaluate(
            fixture.ContractPath,
            fixture.ReportsPath,
            "mysql84",
            "scorecard",
            soakPath);

        Assert.Empty(result.InvalidEvidence);
        Assert.Contains(
            result.Regressions,
            error => error.Contains("managedHeapGrowthBytes", StringComparison.Ordinal));
    }

    private sealed class PerformanceGateFixture : IDisposable
    {
        private PerformanceGateFixture(
            string path
        )
        {
            Path = path;
            ReportsPath = System.IO.Path.Combine(path, "reports");
            Directory.CreateDirectory(ReportsPath);
        }

        public string Path { get; }

        public string ContractPath => System.IO.Path.Combine(Path, "performance-contract.json");

        public string ReportsPath { get; }

        public static PerformanceGateFixture Create(
            double medianBudget = 10,
            string reportTarget = "mysql84",
            int sampleCountOffset = 0
        )
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"doka-performance-gate-{Guid.NewGuid():N}");
            var fixture = new PerformanceGateFixture(path);
            File.WriteAllText(
                fixture.ContractPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 10,
                        contractVersion = "test",
                        requiredTargets = new
                        {
                            mysql84 = new
                            {
                                displayName = "MySQL 8.4",
                                engineFamily = "MySQL",
                                serverVersion = "8.4.0",
                                hostPort = 33068,
                                serverImage = "mysql:8.4@test",
                            },
                        },
                        profiles = new
                        {
                            smoke = new
                            {
                                soakIterations = 1,
                                soakConcurrency = 1,
                                soakRequired = false,
                            },
                            scorecard = new
                            {
                                soakIterations = 1,
                                soakConcurrency = 1,
                                soakRequired = true,
                            },
                        },
                        workloads = new[]
                        {
                            new
                            {
                                id = "query.sync.rows-1",
                                family = "query",
                                smoke = true,
                                operationsPerSample = 2,
                            },
                        },
                        familyBudgets = new
                        {
                            query = new
                            {
                                medianNanoseconds = medianBudget,
                                p95Nanoseconds = 10,
                                p99Nanoseconds = 10,
                                allocatedBytes = 20,
                                gen2CollectionsPer1000 = 0,
                            },
                        },
                        benchmarkDotNetControls = new[]
                        {
                            new
                            {
                                id = "allocation-control",
                                type = "ControlBenchmark",
                                method = "Execute",
                                metric = "allocatedBytes",
                                maximum = 0,
                            },
                        },
                        soakBudgets = new
                        {
                            hiloCacheMaximumEntries = 1024,
                            pooledBufferMaximumOutstanding = 0,
                            connectionMaximumDelta = 1,
                            migrationLockMaximumHeld = 0,
                            workingSetMaximumGrowthBytes = 1024,
                            managedHeapMaximumGrowthBytes = 1024,
                            minimumThroughputRetentionRatio = 0.7,
                        },
                    }));
            File.WriteAllText(
                System.IO.Path.Combine(fixture.ReportsPath, "fixture-report-full.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        HostEnvironmentInfo = new
                        {
                            BenchmarkDotNetVersion = "0.15.8",
                            OsVersion = "Test OS",
                            ProcessorName = "Test CPU",
                            RuntimeVersion = ".NET 10",
                            Architecture = "X64",
                            Configuration = "RELEASE",
                            HasAttachedDebugger = false,
                        },
                        Benchmarks = new object[]
                        {
                            Observation(
                                "ProviderWorkloadBenchmarks",
                                "Execute",
                                "Target=mysql84&WorkloadId=query.(...)-1 [17]",
                                $"Doka.EntityFrameworkCore.MySql.Benchmarks.ProviderWorkloadBenchmarks.Execute("
                                + $"Target: \"{reportTarget}\", WorkloadId: \"query.sync.rows-1\")",
                                [10d, 12d, 11d],
                                allocatedBytes: 20,
                                sampleCountOffset: sampleCountOffset),
                            Observation(
                                "ControlBenchmark",
                                "Execute",
                                string.Empty,
                                "ControlBenchmark.Execute()",
                                [1d, 1d, 1d],
                                allocatedBytes: 0),
                        },
                    }));
            return fixture;
        }

        public string WriteSoak(
            double managedHeapGrowthBytes
        )
        {
            var soakPath = System.IO.Path.Combine(Path, "soak.json");
            File.WriteAllText(
                soakPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 2,
                        kind = "performance-soak",
                        contractVersion = "test",
                        runId = "test-run",
                        target = "mysql84",
                        profile = "scorecard",
                        commit = "test-commit",
                        sourceHash = "test-source",
                        runnerClass = "test-runner",
                        generatedUtc = DateTimeOffset.UtcNow,
                        success = false,
                        scenarios = new object[]
                        {
                            Scenario("soak.hilo-cache-bound", "cacheEntries", 0),
                            Scenario("soak.pooled-buffer-return", "outstandingBuffers", 0),
                            Scenario("soak.connection-cleanup", "connectionDelta", 0),
                            Scenario("soak.migration-lock-cleanup", "heldLocks", 0),
                            new
                            {
                                id = "soak.working-set-stabilization",
                                success = false,
                                metrics = new Dictionary<string, double>(StringComparer.Ordinal)
                                {
                                    ["workingSetGrowthBytes"] = 0,
                                    ["managedHeapGrowthBytes"] = managedHeapGrowthBytes,
                                },
                                budgets = new Dictionary<string, double>(),
                                error = (string?)null,
                            },
                            Scenario("soak.concurrent-throughput-retention", "throughputRetentionRatio", 1),
                        },
                    }));
            return soakPath;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static object Observation(
            string type,
            string method,
            string parameters,
            string fullName,
            double[] values,
            double allocatedBytes,
            int sampleCountOffset = 0
        ) => new
        {
            Type = type,
            Method = method,
            Parameters = parameters,
            FullName = fullName,
            Statistics = new
            {
                OriginalValues = values,
                N = values.Length + sampleCountOffset,
                Mean = values.Average(),
            },
            Memory = new
            {
                BytesAllocatedPerOperation = allocatedBytes,
                Gen2Collections = 0,
            },
        };

        private static object Scenario(
            string id,
            string metric,
            double value
        ) => new
        {
            id,
            success = true,
            metrics = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [metric] = value,
            },
            budgets = new Dictionary<string, double>(),
            error = (string?)null,
        };
    }
}
