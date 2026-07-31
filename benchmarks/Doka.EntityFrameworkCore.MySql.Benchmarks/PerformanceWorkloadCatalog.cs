namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal sealed class PerformanceWorkloadCatalog : IDisposable
{
    private readonly List<IDisposable> _ownedResources = [];

    private PerformanceWorkloadCatalog() { }

    public Dictionary<string, PerformanceWorkload> Workloads { get; } = new(StringComparer.Ordinal);

    public static PerformanceWorkloadCatalog Create()
    {
        var catalog = new PerformanceWorkloadCatalog();

        PerformanceModelWorkloads.Register(catalog);
        PerformanceQueryWorkloads.Register(catalog);
        PerformanceJsonSpatialWorkloads.Register(catalog);
        PerformanceWriteWorkloads.Register(catalog);

        return catalog;
    }

    public void Add(
        PerformanceWorkload workload
    )
    {
        if (!Workloads.TryAdd(workload.Id, workload))
        {
            throw new InvalidOperationException($"Performance workload '{workload.Id}' is registered more than once.");
        }
    }

    public T Own<T>(
        T resource
    )
        where T : IDisposable
    {
        _ownedResources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        for (var index = _ownedResources.Count - 1; index >= 0; index--)
        {
            _ownedResources[index]
                .Dispose();
        }
    }
}
