namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class PerformanceQueryWorkloads
{
    private static readonly Func<BenchmarkContext, int, IEnumerable<BasicBenchmarkEntity>> s_compiledQuery =
        EF.CompileQuery((
            BenchmarkContext context,
            int rowCount
        ) => context
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Take(rowCount));

    private static readonly Func<BenchmarkContext, int, IAsyncEnumerable<BasicBenchmarkEntity>> s_compiledAsyncQuery =
        EF.CompileAsyncQuery((
            BenchmarkContext context,
            int rowCount
        ) => context
            .BasicEntities.AsNoTracking()
            .OrderBy(entity => entity.Id)
            .Take(rowCount));

    public static void Register(
        PerformanceWorkloadCatalog catalog
    )
    {
        var poolBoth = new PerformanceContextSource(
            contextPooling: true,
            connectionPooling: true,
            retryOnFailure: false);

        var poolNone = new PerformanceContextSource(
            contextPooling: false,
            connectionPooling: false,
            retryOnFailure: false);

        var poolContext = new PerformanceContextSource(
            contextPooling: true,
            connectionPooling: false,
            retryOnFailure: false);

        var poolConnection = new PerformanceContextSource(
            contextPooling: false,
            connectionPooling: true,
            retryOnFailure: false);

        AddQuery(catalog, "query.materialize.sync.dynamic.rows-100.pool-both", poolBoth, 100, false, false);
        AddQuery(catalog, "query.materialize.async.dynamic.rows-100.pool-both", poolBoth, 100, true, false);

        AddQuery(catalog, "query.materialize.sync.compiled.rows-100.pool-both", poolBoth, 100, false, true);
        AddQuery(catalog, "query.materialize.async.compiled.rows-100.pool-both", poolBoth, 100, true, true);
        AddQuery(catalog, "query.materialize.async.compiled.rows-100.pool-none", poolNone, 100, true, true);
        AddQuery(catalog, "query.materialize.async.compiled.rows-100.pool-context", poolContext, 100, true, true);
        AddQuery(catalog, "query.materialize.async.compiled.rows-100.pool-connection", poolConnection, 100, true, true);

        AddQuery(catalog, "query.materialize.async.compiled.rows-1.pool-both", poolBoth, 1, true, true);
        AddQuery(catalog, "query.materialize.async.compiled.rows-1000.pool-both", poolBoth, 1000, true, true);

        AddResilienceWorkloads(catalog);
        AddConcurrencyWorkloads(catalog, poolBoth, poolNone);
        AddProjectionWorkloads(catalog, poolBoth);
    }

    private static void AddQuery(
        PerformanceWorkloadCatalog catalog,
        string id,
        PerformanceContextSource source,
        int rowCount,
        bool useAsync,
        bool compiled
    )
    {
        catalog.Add(
            new PerformanceWorkload(
                id,
                cancellationToken => ExecuteQueryAsync(source, rowCount, useAsync, compiled, cancellationToken)));
    }

    private static void AddResilienceWorkloads(
        PerformanceWorkloadCatalog catalog
    )
    {
        var retryOff = new PerformanceContextSource(
            contextPooling: true,
            connectionPooling: true,
            retryOnFailure: false);

        var retryOn = new PerformanceContextSource(contextPooling: true, connectionPooling: true, retryOnFailure: true);

        AddResilience(catalog, "resilience.query.async.retry-off.listener-off", retryOff, listenerEnabled: false);
        AddResilience(catalog, "resilience.query.async.retry-on.listener-off", retryOn, listenerEnabled: false);
        AddResilience(catalog, "resilience.query.async.retry-off.listener-on", retryOff, listenerEnabled: true);
        AddResilience(catalog, "resilience.query.async.retry-on.listener-on", retryOn, listenerEnabled: true);
    }

    private static void AddResilience(
        PerformanceWorkloadCatalog catalog,
        string id,
        PerformanceContextSource source,
        bool listenerEnabled
    )
    {
        EfDiagnosticSubscription? subscription = null;

        catalog.Add(
            new PerformanceWorkload(
                id,
                cancellationToken => ExecuteQueryAsync(
                    source,
                    rowCount: 100,
                    useAsync: true,
                    compiled: false,
                    cancellationToken),
                _ =>
                {
                    if (listenerEnabled)
                    {
                        subscription = new EfDiagnosticSubscription();
                    }

                    return ValueTask.CompletedTask;
                },
                _ =>
                {
                    subscription?.Dispose();
                    subscription = null;
                    return ValueTask.CompletedTask;
                }));
    }

    private static void AddConcurrencyWorkloads(
        PerformanceWorkloadCatalog catalog,
        PerformanceContextSource poolBoth,
        PerformanceContextSource poolNone
    )
    {
        AddConcurrency(catalog, "concurrency.query.async.contexts-1.pool-both", poolBoth, 1);
        AddConcurrency(catalog, "concurrency.query.async.contexts-4.pool-both", poolBoth, 4);
        AddConcurrency(catalog, "concurrency.query.async.contexts-16.pool-both", poolBoth, 16);
        AddConcurrency(catalog, "concurrency.query.async.contexts-16.pool-none", poolNone, 16);
    }

    private static void AddConcurrency(
        PerformanceWorkloadCatalog catalog,
        string id,
        PerformanceContextSource source,
        int contextCount
    )
    {
        catalog.Add(
            new PerformanceWorkload(
                id,
                async cancellationToken =>
                {
                    var tasks = Enumerable
                        .Range(0, contextCount)
                        .Select(_ => ExecuteQueryAsync(
                                source,
                                rowCount: 100,
                                useAsync: true,
                                compiled: true,
                                cancellationToken)
                            .AsTask())
                        .ToArray();

                    var checksums = await Task
                        .WhenAll(tasks)
                        .ConfigureAwait(false);

                    return checksums.Sum();
                }));
    }

    private static void AddProjectionWorkloads(
        PerformanceWorkloadCatalog catalog,
        PerformanceContextSource source
    )
    {
        catalog.Add(
            new PerformanceWorkload(
                "projection.full.sync.rows-100",
                _ =>
                {
                    using var context = source.CreateContext();
                    var rows = context
                        .BasicEntities.AsNoTracking()
                        .OrderBy(entity => entity.Id)
                        .Take(100)
                        .ToList();

                    return ValueTask.FromResult(Checksum(rows));
                }));

        catalog.Add(
            new PerformanceWorkload(
                "projection.anonymous.async.rows-100",
                async cancellationToken =>
                {
                    await using var context = source.CreateContext();

                    var rows = await context
                        .BasicEntities.AsNoTracking()
                        .OrderBy(entity => entity.Id)
                        .Select(entity => new BenchmarkProjection
                        {
                            Id = entity.Id,
                            Name = entity.Name,
                        })
                        .Take(100)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return rows.Sum(row => (long)row.Id + row.Name.Length);
                }));
    }

    private static async ValueTask<long> ExecuteQueryAsync(
        PerformanceContextSource source,
        int rowCount,
        bool useAsync,
        bool compiled,
        CancellationToken cancellationToken
    )
    {
        if (!useAsync)
        {
            using var context = source.CreateContext();

            var rows = compiled
                ? s_compiledQuery(context, rowCount)
                    .ToList()
                : context
                    .BasicEntities.AsNoTracking()
                    .OrderBy(entity => entity.Id)
                    .Take(rowCount)
                    .ToList();

            return Checksum(rows);
        }

        await using var asyncContext = source.CreateContext();

        if (!compiled)
        {
            var rows = await asyncContext
                .BasicEntities.AsNoTracking()
                .OrderBy(entity => entity.Id)
                .Take(rowCount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Checksum(rows);
        }

        long checksum = 0;
        await foreach (var entity in s_compiledAsyncQuery(asyncContext, rowCount)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            checksum = unchecked(checksum + entity.Id + entity.Name.Length);
        }

        return checksum;
    }

    private static long Checksum(
        IEnumerable<BasicBenchmarkEntity> entities
    ) => entities.Sum(entity => (long)entity.Id + entity.Name.Length);

    private sealed class PerformanceContextSource
    {
        private readonly DbContextOptions<BenchmarkContext> _options;
        private readonly PooledDbContextFactory<BenchmarkContext>? _pooledFactory;

        public PerformanceContextSource(
            bool contextPooling,
            bool connectionPooling,
            bool retryOnFailure
        )
        {
            _options = BenchmarkEnvironment.CreateOptions<BenchmarkContext>(connectionPooling, retryOnFailure);

            if (contextPooling)
            {
                _pooledFactory = new PooledDbContextFactory<BenchmarkContext>(_options, poolSize: 128);
            }
        }

        public BenchmarkContext CreateContext() => _pooledFactory is null
            ? new BenchmarkContext(_options)
            : _pooledFactory.CreateDbContext();
    }

    private sealed class EfDiagnosticSubscription : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListenersSubscription;

        public EfDiagnosticSubscription()
        {
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(
            DiagnosticListener value
        )
        {
            if (value.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }

        public void OnNext(
            KeyValuePair<string, object?> value
        )
        {
            _ = value.Key;
        }

        public void OnCompleted() { }

        public void OnError(
            Exception error
        )
        {
            _ = error;
        }

        public void Dispose()
        {
            _allListenersSubscription.Dispose();

            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
