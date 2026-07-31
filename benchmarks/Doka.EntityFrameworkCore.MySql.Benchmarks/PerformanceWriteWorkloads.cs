namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class PerformanceWriteWorkloads
{
    public static void Register(
        PerformanceWorkloadCatalog catalog
    )
    {
        foreach (var rowCount in new[]
                 {
                     10,
                     100,
                     1000,
                     10000,
                 })
        {
            AddWrite(catalog, rowCount, useAsync: false, maxBatchSize: null);
            AddWrite(catalog, rowCount, useAsync: true, maxBatchSize: null);
        }

        AddWrite(catalog, rowCount: 1000, useAsync: true, maxBatchSize: 32);
        AddWrite(catalog, rowCount: 1000, useAsync: true, maxBatchSize: 256);

        var hiLoDatabase = catalog.Own(new HiLoBenchmarkDatabase());

        AddHiLo(catalog, hiLoDatabase, contextCount: 1, rowCount: 100, useAsync: false);
        AddHiLo(catalog, hiLoDatabase, contextCount: 1, rowCount: 100, useAsync: true);

        AddHiLo(catalog, hiLoDatabase, contextCount: 10, rowCount: 1000, useAsync: false);
        AddHiLo(catalog, hiLoDatabase, contextCount: 10, rowCount: 1000, useAsync: true);
    }

    private static void AddWrite(
        PerformanceWorkloadCatalog catalog,
        int rowCount,
        bool useAsync,
        int? maxBatchSize
    )
    {
        var execution = useAsync ? "async" : "sync";
        var batch = maxBatchSize?.ToString(CultureInfo.InvariantCulture) ?? "default";
        var id = $"write.savechanges.{execution}.rows-{rowCount}.batch-{batch}";
        var options = BenchmarkEnvironment.CreateOptions<BenchmarkContext>(
            maxBatchSize: maxBatchSize);

        catalog.Add(
            new PerformanceWorkload(
                id,
                cancellationToken => SaveChangesAsync(options, rowCount, useAsync, cancellationToken),
                cancellationToken => new ValueTask(
                    BenchmarkEnvironment.ResetSaveChangesTableAsync(cancellationToken))));
    }

    private static async ValueTask<long> SaveChangesAsync(
        DbContextOptions<BenchmarkContext> options,
        int rowCount,
        bool useAsync,
        CancellationToken cancellationToken
    )
    {
        if (!useAsync)
        {
            using var context = new BenchmarkContext(options);

            AddEntities(context, rowCount);

            // This workload intentionally measures EF Core's synchronous write path.
            // ReSharper disable once MethodHasAsyncOverloadWithCancellation
            return context.SaveChanges();
        }

        await using var asyncContext = new BenchmarkContext(options);

        AddEntities(asyncContext, rowCount);

        return await asyncContext
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AddEntities(
        BenchmarkContext context,
        int rowCount
    )
    {
        for (var index = 0; index < rowCount; index++)
        {
            context.SaveChangeEntities.Add(
                new SaveChangeBenchmarkEntity
                {
                    Name = $"savechanges-{index}",
                });
        }
    }

    private static void AddHiLo(
        PerformanceWorkloadCatalog catalog,
        HiLoBenchmarkDatabase database,
        int contextCount,
        int rowCount,
        bool useAsync
    )
    {
        var execution = useAsync ? "async" : "sync";
        var id = $"hilo.insert.{execution}.contexts-{contextCount}.rows-{rowCount}";

        catalog.Add(
            new PerformanceWorkload(
                id,
                async cancellationToken =>
                {
                    if (useAsync)
                    {
                        await database
                            .InsertAsync(contextCount, rowCount, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        database.Insert(contextCount, rowCount);
                    }

                    return rowCount;
                },
                _ =>
                {
                    database.Reset();
                    return ValueTask.CompletedTask;
                }));
    }
}
