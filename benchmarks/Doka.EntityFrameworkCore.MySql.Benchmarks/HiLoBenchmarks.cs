namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the Hi/Lo state-cache hot path in isolation: every call site that resolves
/// a Hi/Lo generator goes through GetOrCreate, so the per-call cost of the dictionary
/// lookup is a regression-sensitive number even before any database round-trip enters
/// the picture. Pairs with HiLoBulkInsertBenchmarks which measures the end-to-end
/// throughput against a live MySQL container.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class HiLoStateCacheBenchmarks
{
    private const string SequenceName = "bench_hilo_seq";
    private const int BlockSize = 10;

    private static readonly MySqlDatabaseIdentity s_databaseIdentity = new(
        "benchmark-server",
        3306,
        "benchmark-database",
        "benchmark-user");

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Prime the cache so the benchmark only measures hit-path cost.
        _ = MySqlHiLoStateCache.GetOrCreate(s_databaseIdentity, SequenceName, BlockSize);
    }

    [Benchmark]
    public object ResolveCachedHiLoState() =>
        MySqlHiLoStateCache.GetOrCreate(s_databaseIdentity, SequenceName, BlockSize);
}

/// <summary>
/// Measures the end-to-end Hi/Lo bulk-insert throughput against a live MySQL container.
/// The shared state cache is what makes this fast: without the cache, every DbContext
/// would round-trip to the sequence on its first insert; with the cache, ten contexts
/// share the same block window and the round-trip count collapses by an order of
/// magnitude. Run before and after a code change to verify the throughput delta.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class HiLoBulkInsertBenchmarks
{
    private const string SequenceName = "bench_hilo_bulk_seq";
    private const string TableName = "BenchHiLoItems";
    private const int InsertsPerIteration = 100;

    private string _connectionString = string.Empty;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        BenchmarkEnvironment.EnsureInitialized();
        _connectionString = BenchmarkEnvironment.CreateConnectionString(BenchmarkEnvironment.DatabaseNameValue);

        await PrepareSchemaAsync()
            .ConfigureAwait(false);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        MySqlHiLoStateCache.ResetForTesting();
        TruncateTableAsync()
            .GetAwaiter()
            .GetResult();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await TearDownSchemaAsync()
            .ConfigureAwait(false);
    }

    [Benchmark]
    public async Task BulkInsertAcrossTenContexts()
    {
        await Parallel
            .ForEachAsync(
                Enumerable.Range(0, 10),
                async (_, cancellationToken) =>
                {
                    await using var context = new HiLoBenchContext(BuildOptions());

                    for (var index = 0; index < InsertsPerIteration / 10; index++)
                    {
                        context.Items.Add(new HiLoBenchEntity { Name = $"row-{index}" });
                        await context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                })
            .ConfigureAwait(false);
    }

    private DbContextOptions<HiLoBenchContext> BuildOptions()
    {
        var builder = new DbContextOptionsBuilder<HiLoBenchContext>();
        builder.UseMySql(_connectionString, BenchmarkEnvironment.ServerVersionValue);
        return builder.Options;
    }

    private async Task PrepareSchemaAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP TABLE IF EXISTS `{TableName}`;"
            + $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;"
            + $"CREATE TABLE `__efsequence_{SequenceName}` ("
            + "  `id` TINYINT UNSIGNED NOT NULL,"
            + "  `value` BIGINT NOT NULL,"
            + "  `is_called` BOOLEAN NOT NULL,"
            + "  PRIMARY KEY (`id`),"
            + "  CHECK (`id` = 1)"
            + ") ENGINE=InnoDB;"
            + $"INSERT INTO `__efsequence_{SequenceName}` (`id`, `value`, `is_called`) VALUES (1, 1, FALSE);"
            + $"CREATE TABLE `{TableName}` ("
            + "  `Id` INT NOT NULL,"
            + "  `Name` VARCHAR(64) NOT NULL,"
            + "  PRIMARY KEY (`Id`)"
            + ") CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private async Task TruncateTableAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE `{TableName}`;"
            + $"UPDATE `__efsequence_{SequenceName}` SET `value` = 1, `is_called` = FALSE WHERE `id` = 1;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private async Task TearDownSchemaAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;"
            + $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class HiLoBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class HiLoBenchContext : DbContext
    {
        public HiLoBenchContext(
            DbContextOptions<HiLoBenchContext> options
        ) : base(options) { }

        public DbSet<HiLoBenchEntity> Items => Set<HiLoBenchEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoBenchEntity>(builder =>
            {
                builder.ToTable(TableName);
                builder.HasKey(e => e.Id);
                builder
                    .Property(e => e.Id)
                    .UseHiLo(SequenceName);
                builder
                    .Property(e => e.Name)
                    .HasMaxLength(64);
            });
        }
    }
}
