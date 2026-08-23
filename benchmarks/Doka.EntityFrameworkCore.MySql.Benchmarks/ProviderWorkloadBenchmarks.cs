namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class ProviderWorkloadBenchmarks : IDisposable
{
    private PerformanceWorkloadCatalog? _catalog;
    private PerformanceWorkload? _workload;
    private int _operationsPerInvoke;

    [ParamsSource(nameof(Targets))]
    public string Target { get; set; } = string.Empty;

    [ParamsSource(nameof(WorkloadIds))]
    public string WorkloadId { get; set; } = string.Empty;

    public IEnumerable<string> Targets => [BenchmarkDatabaseTarget.Current.TargetId];

    public IEnumerable<string> WorkloadIds
    {
        get
        {
            var contract = PerformanceContract.Load();
            return ApplicableDefinitions(contract)
                .Select(static definition => definition.Id)
                .Order(StringComparer.Ordinal);
        }
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        BenchmarkEnvironment.EnsureInitialized();
        if (!string.Equals(Target, BenchmarkEnvironment.TargetIdValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Benchmark target parameter '{Target}' does not match the active database target.");
        }

        var contract = PerformanceContract.Load();
        _catalog = PerformanceWorkloadCatalog.Create();
        var registered = _catalog.Workloads.Keys.ToHashSet(StringComparer.Ordinal);
        var declared = contract
            .Workloads
            .Select(static definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (!registered.SetEquals(declared))
        {
            throw new InvalidDataException(
                "The performance workload catalog and contract do not declare the same IDs.");
        }

        var definition = ApplicableDefinitions(contract)
            .Single(candidate => string.Equals(candidate.Id, WorkloadId, StringComparison.Ordinal));

        _workload = _catalog.Workloads[definition.Id];
        _operationsPerInvoke = definition.OperationsPerSample;
        if (_operationsPerInvoke <= 0)
        {
            throw new InvalidDataException($"Performance workload '{definition.Id}' has no positive operation batch.");
        }
    }

    [IterationSetup]
    public void IterationSetup() => InvokeLifecycle(_workload?.PrepareAsync);

    [Benchmark]
    public async ValueTask<long> Execute()
    {
        var workload = _workload
            ?? throw new InvalidOperationException("The provider workload benchmark was not initialized.");

        long checksum = 0;
        for (var operation = 0; operation < _operationsPerInvoke; operation++)
        {
            checksum = unchecked(checksum
                + await workload
                    .ExecuteAsync(CancellationToken.None)
                    .ConfigureAwait(false));
        }

        return checksum;
    }

    [IterationCleanup]
    public void IterationCleanup() => InvokeLifecycle(_workload?.CleanupAsync);

    [GlobalCleanup]
    public void Dispose()
    {
        _catalog?.Dispose();
        _catalog = null;
        _workload = null;
        GC.SuppressFinalize(this);
    }

    private static IEnumerable<PerformanceWorkloadDefinition> ApplicableDefinitions(
        PerformanceContract contract
    ) => string.Equals(BenchmarkProfiles.Current, BenchmarkProfiles.SmokeProfile, StringComparison.Ordinal)
        ? contract.Workloads.Where(static definition => definition.Smoke)
        : contract.Workloads;

    private static void InvokeLifecycle(
        Func<CancellationToken, ValueTask>? lifecycle
    )
    {
        if (lifecycle is null)
        {
            return;
        }

        lifecycle(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}
