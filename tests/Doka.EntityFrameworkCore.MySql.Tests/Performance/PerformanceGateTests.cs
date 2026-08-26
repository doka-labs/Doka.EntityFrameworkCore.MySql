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

    [Theory]
    [InlineData("mysql84", 3, 0)]
    [InlineData("mysql84", 3.01, 1)]
    [InlineData("mysql84", 4, 1)]
    [InlineData("mariadb114", 4, 0)]
    [InlineData("mariadb114", 5, 0)]
    [InlineData("mariadb114", 5.01, 1)]
    public void Gate_uses_the_selected_target_maximum_and_accepts_its_boundary(
        string target,
        double measuredMean,
        int expectedExitCode
    )
    {
        using var fixture = PerformanceGateFixture.Create(
            reportTarget: target,
            controlsJson: RatioControl(""" "maximumByTarget": {"mysql84": 1.5, "mariadb114": 2.5} """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", measuredMean),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        var result = fixture.Evaluate(target);

        Assert.Empty(result.InvalidEvidence);
        Assert.Equal(expectedExitCode, result.Regressions.Count);
        Assert.Equal(expectedExitCode, fixture.Run(target));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(6, 1)]
    public void Gate_detects_slower_execution_without_any_allocation_change(
        double measuredMean,
        int expectedExitCode
    )
    {
        using var fixture = PerformanceGateFixture.Create(
            controlsJson:
            """
            [
                {
                    "id": "allocation-control",
                    "type": "ControlBenchmark",
                    "method": "Measured",
                    "metric": "allocatedBytes",
                    "maximum": 0
                },
                {
                    "id": "ratio-control",
                    "type": "ControlBenchmark",
                    "method": "Measured",
                    "baselineMethod": "Baseline",
                    "metric": "meanRatio",
                    "maximum": 1.5
                }
            ]
            """,
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", measuredMean),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        var result = fixture.Evaluate();

        Assert.Empty(result.InvalidEvidence);
        Assert.Equal(2, result.ControlCount);
        Assert.Equal(expectedExitCode, result.Regressions.Count);
        Assert.All(result.Regressions, error => Assert.Contains("ratio-control meanRatio", error));
        Assert.Equal(expectedExitCode, fixture.Run());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Cache_sliding_controls_enforce_checked_in_target_time_budgets_without_allocation_changes(
        double meanMultiplier,
        int expectedExitCode
    )
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));
        var contract = document.RootElement;
        var controls = contract.GetProperty("benchmarkDotNetControls").EnumerateArray().ToArray();
        var throughput = Assert.Single(controls, control =>
            control.GetProperty("id").GetString() == "cache-parallel-sliding-buffer-throughput");

        var allocation = Assert.Single(controls, control =>
            control.GetProperty("id").GetString() == "cache-parallel-sliding-buffer-allocation");

        var type = nameof(Benchmarks.DistributedCacheBenchmark);
        var measuredMethod = nameof(Benchmarks.DistributedCacheBenchmark.ParallelSlidingBufferReadsAsync);
        var baselineMethod = nameof(Benchmarks.DistributedCacheBenchmark.ParallelBufferReadsAsync);

        Assert.Equal(11, contract.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(type, throughput.GetProperty("type").GetString());
        Assert.Equal(measuredMethod, throughput.GetProperty("method").GetString());
        Assert.Equal(baselineMethod, throughput.GetProperty("baselineMethod").GetString());
        Assert.Equal("meanRatio", throughput.GetProperty("metric").GetString());
        Assert.Equal(type, allocation.GetProperty("type").GetString());
        Assert.Equal(measuredMethod, allocation.GetProperty("method").GetString());
        Assert.Equal("allocatedBytes", allocation.GetProperty("metric").GetString());

        var requiredTargets = contract.GetProperty("requiredTargets");
        foreach (var target in requiredTargets.EnumerateObject())
        {
            var maximum = throughput.GetProperty("maximumByTarget").GetProperty(target.Name).GetDouble();
            using var fixture = PerformanceGateFixture.Create(
                reportTarget: target.Name,
                controlsJson: JsonSerializer.Serialize(new[] { throughput, allocation }),
                controlObservations:
                [
                    PerformanceGateFixture.ControlObservation(measuredMethod, maximum * meanMultiplier, type: type),
                    PerformanceGateFixture.ControlObservation(baselineMethod, 1, type: type),
                ],
                requiredTargets: requiredTargets);

            var result = fixture.Evaluate(target.Name);

            Assert.Empty(result.InvalidEvidence);
            Assert.Equal(2, result.ControlCount);
            Assert.Equal(expectedExitCode, result.Regressions.Count);
            Assert.All(result.Regressions, error => Assert.Contains("cache-parallel-sliding-buffer-throughput", error));
            Assert.Equal(expectedExitCode, fixture.Run(target.Name));
        }
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 1)]
    [InlineData(0, false, 78)]
    public void Generic_like_controls_enforce_actual_allocation_budgets_and_require_evidence(
        int allocationExcess,
        bool includeObservation,
        int expectedExitCode
    )
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));

        var controls = document.RootElement
            .GetProperty("benchmarkDotNetControls")
            .EnumerateArray()
            .Where(control => control.GetProperty("type").GetString() == nameof(Benchmarks.GenericLikeBenchmark))
            .ToArray();

        Assert.Equal(2, controls.Length);

        foreach (var control in controls)
        {
            var id = control.GetProperty("id").GetString();
            var method = control.GetProperty("method").GetString()!;
            var maximum = control.GetProperty("maximum").GetDouble();

            Assert.Equal("allocatedBytes", control.GetProperty("metric").GetString());

            using var fixture = PerformanceGateFixture.Create(
                controlsJson: JsonSerializer.Serialize(new[] { control }),
                controlObservations: includeObservation
                    ? [PerformanceGateFixture.ControlObservation(
                        method,
                        mean: 1,
                        allocatedBytes: maximum + allocationExcess,
                        type: nameof(Benchmarks.GenericLikeBenchmark))]
                    : []);

            var result = fixture.Evaluate();

            Assert.Equal(includeObservation ? 0 : 1, result.InvalidEvidence.Count);
            Assert.Equal(allocationExcess, result.Regressions.Count);
            Assert.All(result.Regressions, error => Assert.Contains($"{id} allocatedBytes", error));
            Assert.Equal(expectedExitCode, fixture.Run());
        }
    }

    [Theory]
    [InlineData("", "exactly one")]
    [InlineData(""" "maximum": 1, "maximumByTarget": {} """, "exactly one")]
    [InlineData(""" "maximum": 1, "maximum": 2 """, "exactly one")]
    [InlineData(""" "maximumByTarget": {}, "maximumByTarget": {} """, "exactly one")]
    [InlineData(""" "maximumByTarget": null """, "must be an object")]
    [InlineData(""" "maximumByTarget": [] """, "must be an object")]
    [InlineData(""" "maximumByTarget": 2 """, "must be an object")]
    [InlineData(""" "maximumByTarget": {} """, "missing target")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1.5} """, "missing target 'mariadb114'")]
    [InlineData(""" "maximumByTarget": {"mariadb114": 2.5} """, "missing target 'mysql84'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "mariadb114": 2.5, "other": 3} """,
        "unknown target 'other'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "MariaDb114": 2.5} """,
        "unknown target 'MariaDb114'")]
    [InlineData(
        """ "maximumByTarget": {"mysql84": 1.5, "mysql84": 100, "mariadb114": 2.5} """,
        "repeats target 'mysql84'")]
    [InlineData(""" "maximumByTarget": {"mysql84": "1.5", "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": true, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": null, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": {}, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1e999, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": -1, "mariadb114": 2.5} """, "finite nonnegative")]
    [InlineData(""" "maximumByTarget": {"mysql84": 1.5, "mariadb114": -1} """, "finite nonnegative")]
    [InlineData(""" "maximum": "1.5" """, "finite nonnegative")]
    [InlineData(""" "maximum": null """, "finite nonnegative")]
    [InlineData(""" "maximum": -1 """, "finite nonnegative")]
    [InlineData(""" "maximum": 1e999 """, "finite nonnegative")]
    public void Gate_rejects_incomplete_ambiguous_or_malformed_control_maxima(
        string maximumProperties,
        string expectedError
    )
    {
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(maximumProperties),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        var result = fixture.Evaluate();

        Assert.Contains(result.InvalidEvidence, error => error.Contains(expectedError, StringComparison.Ordinal));
        Assert.Empty(result.Regressions);
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, fixture.Run());
    }

    [Theory]
    [InlineData("Measured", 0)]
    [InlineData("Measured", 2)]
    [InlineData("Baseline", 0)]
    [InlineData("Baseline", 2)]
    public void Gate_requires_exactly_one_measured_and_baseline_observation(
        string method,
        int count
    )
    {
        var observations = Enumerable.Repeat(
                PerformanceGateFixture.ControlObservation("Measured", 3),
                method == "Measured" ? count : 1)
            .Concat(Enumerable.Repeat(
                PerformanceGateFixture.ControlObservation("Baseline", 2),
                method == "Baseline" ? count : 1))
            .ToArray();

        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1.5 """),
            controlObservations: observations);

        var result = fixture.Evaluate();

        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains($"ControlBenchmark.{method} result, found {count}", StringComparison.Ordinal));
        Assert.Empty(result.Regressions);
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, fixture.Run());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 78)]
    public void Gate_accepts_a_zero_allocation_baseline_only_for_zero_allocations(
        double allocatedBytes,
        int expectedExitCode
    )
    {
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 0 """, metric: "allocationRatio"),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3, allocatedBytes),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        var result = fixture.Evaluate();

        Assert.Equal(expectedExitCode == 0, result.InvalidEvidence.Count == 0);
        Assert.Empty(result.Regressions);
        Assert.Equal(expectedExitCode, fixture.Run());
    }

    [Fact]
    public async Task Driver_rejects_a_zero_mean_baseline_as_invalid_measurement_evidence()
    {
        using var fixture = PerformanceGateFixture.Create(
            controlsJson: RatioControl(""" "maximum": 1.5 """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 0),
            ]);

        var exception = Assert.Throws<InvalidDataException>(() => fixture.Evaluate());

        Assert.Contains("no finite positive samples", exception.Message);
        Assert.Equal(
            BenchmarkPerformanceGate.InvalidEvidenceExitCode,
            await Benchmarks.Program.Main(
                ["--evaluate", fixture.ContractPath, fixture.ReportsPath, "mysql84", "smoke"]));
    }

    [Fact]
    public void Gate_preserves_invalid_evidence_precedence_when_a_budget_also_regresses()
    {
        using var fixture = PerformanceGateFixture.Create(
            medianBudget: 4,
            controlsJson: RatioControl(""" "maximumByTarget": {"mysql84": 1.5} """),
            controlObservations:
            [
                PerformanceGateFixture.ControlObservation("Measured", 3),
                PerformanceGateFixture.ControlObservation("Baseline", 2),
            ]);

        var result = fixture.Evaluate();

        Assert.Contains(
            result.InvalidEvidence,
            error => error.Contains("missing target 'mariadb114'", StringComparison.Ordinal));
        Assert.Contains(
            result.Regressions,
            error => error.Contains("medianNanoseconds", StringComparison.Ordinal));
        Assert.Equal(BenchmarkPerformanceGate.InvalidEvidenceExitCode, fixture.Run());
    }

    private static string RatioControl(
        string maximumProperties,
        string metric = "meanRatio"
    ) => $$"""
        [
            {
                "id": "ratio-control",
                "type": "ControlBenchmark",
                "method": "Measured",
                "baselineMethod": "Baseline",
                {{maximumProperties}}{{(maximumProperties.Length > 0 ? "," : string.Empty)}}
                "metric": "{{metric}}"
            }
        ]
        """;

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
            int sampleCountOffset = 0,
            string? controlsJson = null,
            object[]? controlObservations = null,
            JsonElement? requiredTargets = null
        )
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"doka-performance-gate-{Guid.NewGuid():N}");
            var fixture = new PerformanceGateFixture(path);
            using var controlsDocument = JsonDocument.Parse(controlsJson ??
                """
                [
                    {
                        "id": "allocation-control",
                        "type": "ControlBenchmark",
                        "method": "Execute",
                        "metric": "allocatedBytes",
                        "maximum": 0
                    }
                ]
                """);

            File.WriteAllText(
                fixture.ContractPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 11,
                        contractVersion = "test",
                        requiredTargets = requiredTargets ?? JsonSerializer.SerializeToElement(new
                        {
                            mysql84 = new
                            {
                                displayName = "MySQL 8.4",
                                engineFamily = "MySQL",
                                serverVersion = "8.4.0",
                                hostPort = 33068,
                                serverImage = "mysql:8.4@test",
                            },
                            mariadb114 = new
                            {
                                displayName = "MariaDB 11.4",
                                engineFamily = "MariaDB",
                                serverVersion = "11.4.0",
                                hostPort = 33070,
                                serverImage = "mariadb:11.4@test",
                            },
                        }),
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
                        benchmarkDotNetControls = controlsDocument.RootElement,
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
                                $"Target={reportTarget}&WorkloadId=query.(...)-1 [17]",
                                $"Doka.EntityFrameworkCore.MySql.Benchmarks.ProviderWorkloadBenchmarks.Execute("
                                + $"Target: \"{reportTarget}\", WorkloadId: \"query.sync.rows-1\")",
                                [10d, 12d, 11d],
                                allocatedBytes: 20,
                                sampleCountOffset: sampleCountOffset),
                        }.Concat(controlObservations ?? [ControlObservation("Execute", 1)]).ToArray(),
                    }));
            return fixture;
        }

        public Benchmarks.PerformanceGateResult Evaluate(
            string target = "mysql84"
        ) => BenchmarkPerformanceGate.Evaluate(ContractPath, ReportsPath, target, "smoke", soakPath: null);

        public int Run(
            string target = "mysql84"
        ) => BenchmarkPerformanceGate.Run(ContractPath, ReportsPath, target, "smoke", soakPath: null);

        public static object ControlObservation(
            string method,
            double mean,
            double allocatedBytes = 0,
            string type = "ControlBenchmark"
        ) => Observation(
            type,
            method,
            string.Empty,
            $"{type}.{method}()",
            [mean],
            allocatedBytes);

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
