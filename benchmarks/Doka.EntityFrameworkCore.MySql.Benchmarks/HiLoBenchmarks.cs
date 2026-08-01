namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the bounded HiLo state-cache hit path independently of database I/O.
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
        "benchmark-user",
        MySqlConnectionProtocol.Sockets,
        string.Empty);

    [GlobalSetup]
    public void GlobalSetup()
    {
        _ = MySqlHiLoStateCache.GetOrCreate(s_databaseIdentity, SequenceName, BlockSize);
    }

    [Benchmark]
    public object ResolveCachedHiLoState() =>
        MySqlHiLoStateCache.GetOrCreate(s_databaseIdentity, SequenceName, BlockSize);
}

/// <summary>
/// Measures end-to-end HiLo inserts across concurrent contexts against the live
/// benchmark target.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class HiLoBulkInsertBenchmarks : IDisposable
{
    private HiLoBenchmarkDatabase _database = null!;

    [GlobalSetup]
    public void GlobalSetup() => _database = new HiLoBenchmarkDatabase();

    [IterationSetup]
    public void IterationSetup() => _database.Reset();

    [GlobalCleanup]
    public void GlobalCleanup() => Dispose();

    [Benchmark]
    public Task BulkInsertAcrossTenContexts() => _database.InsertAsync(
        contextCount: 10,
        rowCount: 100,
        CancellationToken.None);

    public void Dispose()
    {
        _database?.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class HiLoBenchmarkDatabase : IDisposable
{
    private const string SequenceName = "bench_hilo_bulk_seq";
    private const string TableName = "BenchHiLoItems";

    private readonly string _connectionString;
    private bool _disposed;

    public HiLoBenchmarkDatabase()
    {
        BenchmarkEnvironment.EnsureInitialized();
        _connectionString = BenchmarkEnvironment.CreateConnectionString(BenchmarkEnvironment.DatabaseNameValue);
        PrepareSchemaAsync()
            .GetAwaiter()
            .GetResult();
    }

    public void Reset()
    {
        MySqlHiLoStateCache.ResetForTesting();
        ResetSchemaAsync()
            .GetAwaiter()
            .GetResult();
    }

    public void Insert(
        int contextCount,
        int rowCount
    )
    {
        ValidateShape(contextCount, rowCount);
        var rowsPerContext = rowCount / contextCount;

        Parallel.For(
            0,
            contextCount,
            contextIndex =>
            {
                using var context = new HiLoBenchContext(BuildOptions());

                for (var rowIndex = 0; rowIndex < rowsPerContext; rowIndex++)
                {
                    context.Items.Add(
                        new HiLoBenchEntity
                        {
                            Name = $"row-{contextIndex}-{rowIndex}",
                        });
                    context.SaveChanges();
                }
            });
    }

    public async Task InsertAsync(
        int contextCount,
        int rowCount,
        CancellationToken cancellationToken
    )
    {
        ValidateShape(contextCount, rowCount);
        var rowsPerContext = rowCount / contextCount;

        await Parallel
            .ForEachAsync(
                Enumerable.Range(0, contextCount),
                cancellationToken,
                async (
                    contextIndex,
                    token
                ) =>
                {
                    await using var context = new HiLoBenchContext(BuildOptions());

                    for (var rowIndex = 0; rowIndex < rowsPerContext; rowIndex++)
                    {
                        context.Items.Add(
                            new HiLoBenchEntity
                            {
                                Name = $"row-{contextIndex}-{rowIndex}",
                            });
                        await context
                            .SaveChangesAsync(token)
                            .ConfigureAwait(false);
                    }
                })
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        TearDownSchemaAsync()
            .GetAwaiter()
            .GetResult();
        _disposed = true;
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
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;"
            + CreateSequenceSql()
            + $"CREATE TABLE `{TableName}` ("
            + "  `Id` INT NOT NULL,"
            + "  `Name` VARCHAR(64) NOT NULL,"
            + "  PRIMARY KEY (`Id`)"
            + ") CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private async Task ResetSchemaAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE `{TableName}`;"
            + ResetSequenceSql();
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
            + DropSequenceSql();
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static string CreateSequenceSql() => BenchmarkEnvironment.SupportsNativeSequencesValue
        ? $"DROP SEQUENCE IF EXISTS `{SequenceName}`;"
            + $"CREATE SEQUENCE `{SequenceName}` START WITH 1 INCREMENT BY 10 MINVALUE 1 NOCACHE;"
        : $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;"
            + $"CREATE TABLE `__efsequence_{SequenceName}` ("
            + "  `id` TINYINT UNSIGNED NOT NULL,"
            + "  `value` BIGINT NOT NULL,"
            + "  `is_called` BOOLEAN NOT NULL,"
            + "  PRIMARY KEY (`id`),"
            + "  CHECK (`id` = 1)"
            + ") ENGINE=InnoDB;"
            + $"INSERT INTO `__efsequence_{SequenceName}` (`id`, `value`, `is_called`) VALUES (1, 1, FALSE);";

    private static string ResetSequenceSql() => BenchmarkEnvironment.SupportsNativeSequencesValue
        ? $"ALTER SEQUENCE `{SequenceName}` RESTART WITH 1;"
        : $"UPDATE `__efsequence_{SequenceName}` SET `value` = 1, `is_called` = FALSE WHERE `id` = 1;";

    private static string DropSequenceSql() => BenchmarkEnvironment.SupportsNativeSequencesValue
        ? $"DROP SEQUENCE IF EXISTS `{SequenceName}`;"
        : $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;";

    private static void ValidateShape(
        int contextCount,
        int rowCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);

        if (rowCount % contextCount != 0)
        {
            throw new ArgumentException("The row count must be divisible by the context count.");
        }
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
                builder.HasKey(entity => entity.Id);
                builder
                    .Property(entity => entity.Id)
                    .UseHiLo(SequenceName);
                builder
                    .Property(entity => entity.Name)
                    .HasMaxLength(64);
            });
        }
    }
}
