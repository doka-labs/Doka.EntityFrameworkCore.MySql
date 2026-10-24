namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
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

    private static string ParameterizedCollectionControlsJson()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));

        var controls = document.RootElement
            .GetProperty("benchmarkDotNetControls")
            .EnumerateArray()
            .Where(control => control.GetProperty("type").GetString()
                == nameof(Benchmarks.ParameterizedCollectionBenchmark))
            .ToArray();

        return JsonSerializer.Serialize(controls);
    }

    private static object[] ParameterizedCollectionObservations(
        double binaryJsonMean,
        double charJsonAllocatedBytes
    ) =>
    [
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.JsonParameterBinary16),
            binaryJsonMean,
            allocatedBytes: 0.2,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.MultipleParametersBinary16),
            mean: 1,
            allocatedBytes: 1,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.ConstantBinary16),
            mean: 1,
            allocatedBytes: 0.4,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.JsonParameterChar36),
            mean: 0.4,
            allocatedBytes: charJsonAllocatedBytes,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.MultipleParametersChar36),
            mean: 1,
            allocatedBytes: 1,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
        PerformanceGateFixture.ControlObservation(
            nameof(Benchmarks.ParameterizedCollectionBenchmark.ConstantChar36),
            mean: 1,
            allocatedBytes: 0.5,
            type: nameof(Benchmarks.ParameterizedCollectionBenchmark)),
    ];

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
            JsonElement? requiredTargets = null,
            double timeMultiplier = 1,
            double workloadAllocatedBytes = 20,
            double gen2Collections = 0
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
                                [10d * timeMultiplier, 12d * timeMultiplier, 11d * timeMultiplier],
                                allocatedBytes: workloadAllocatedBytes,
                                sampleCountOffset: sampleCountOffset,
                                gen2Collections: gen2Collections),
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
                        success = managedHeapGrowthBytes <= 1024,
                        scenarios = new object[]
                        {
                            Scenario("soak.hilo-cache-bound", "cacheEntries", 0),
                            Scenario("soak.pooled-buffer-return", "outstandingBuffers", 0),
                            Scenario("soak.connection-cleanup", "connectionDelta", 0),
                            Scenario("soak.migration-lock-cleanup", "heldLocks", 0),
                            new
                            {
                                id = "soak.working-set-stabilization",
                                success = managedHeapGrowthBytes <= 1024,
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
            int sampleCountOffset = 0,
            double gen2Collections = 0
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
                Gen2Collections = gen2Collections,
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
