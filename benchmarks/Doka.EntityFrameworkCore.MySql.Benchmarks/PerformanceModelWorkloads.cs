namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class PerformanceModelWorkloads
{
    private const int LargeSchemaEntityCount = 256;

    public static void Register(
        PerformanceWorkloadCatalog catalog
    )
    {
        var warmSmallOptions = BenchmarkEnvironment.CreateOptions<BenchmarkContext>();
        var warmLargeOptions = BenchmarkEnvironment.CreateOptions<LargeBenchmarkContext>();

        using (var context = new BenchmarkContext(warmSmallOptions))
        {
            _ = context.Model;
        }

        using (var context = new LargeBenchmarkContext(warmLargeOptions, LargeSchemaEntityCount))
        {
            _ = context.Model;
        }

        catalog.Add(
            new PerformanceWorkload(
                "model.cold.small",
                _ => ValueTask.FromResult(InitializeColdSmallModel())));
        catalog.Add(
            new PerformanceWorkload(
                "model.warm.small",
                _ => ValueTask.FromResult(AccessWarmSmallModel(warmSmallOptions))));

        catalog.Add(
            new PerformanceWorkload(
                "model.cold.large-schema-256",
                _ => ValueTask.FromResult(InitializeColdLargeModel())));
        catalog.Add(
            new PerformanceWorkload(
                "model.warm.large-schema-256",
                _ => ValueTask.FromResult(AccessWarmLargeModel(warmLargeOptions))));

        var translation = new QueryTranslationBenchmarks();
        translation.GlobalSetup();

        catalog.Add(
            new PerformanceWorkload(
                "translation.corpus.full",
                _ => ValueTask.FromResult((long)translation.TranslateRepresentativeCorpus())));

        var migration = new MigrationsSqlGenerationBenchmarks();
        migration.GlobalSetup();

        catalog.Add(
            new PerformanceWorkload(
                "migration.corpus.full",
                _ => ValueTask.FromResult((long)migration.GenerateMigrationSqlCorpus())));
    }

    private static long InitializeColdSmallModel()
    {
        var options = BenchmarkEnvironment.CreateOptions<BenchmarkContext>(serviceProviderCaching: false);
        using var context = new BenchmarkContext(options);

        return context
            .Model.GetEntityTypes()
            .Count();
    }

    private static long AccessWarmSmallModel(
        DbContextOptions<BenchmarkContext> options
    )
    {
        using var context = new BenchmarkContext(options);

        return context
            .Model.GetEntityTypes()
            .Count();
    }

    private static long InitializeColdLargeModel()
    {
        var options = BenchmarkEnvironment.CreateOptions<LargeBenchmarkContext>(serviceProviderCaching: false);
        using var context = new LargeBenchmarkContext(options, LargeSchemaEntityCount);

        return context
            .Model.GetEntityTypes()
            .Count();
    }

    private static long AccessWarmLargeModel(
        DbContextOptions<LargeBenchmarkContext> options
    )
    {
        using var context = new LargeBenchmarkContext(options, LargeSchemaEntityCount);

        return context
            .Model.GetEntityTypes()
            .Count();
    }
}
