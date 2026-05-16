namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the end-to-end throughput delta between the per-row INSERT path (SaveChanges
/// after every Add) and the multi-row INSERT path (single SaveChanges after AddRange).
/// The multi-row path is the provider's default since D-007; this benchmark exists to
/// guard against regressions and to surface the engine-specific delta between MariaDB
/// (single-statement INSERT ... RETURNING) and MySQL (single-statement INSERT plus
/// per-row read-back fallback). Run against a live container; pairs with
/// MySqlBulkInsertReturningTests for correctness coverage.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class BulkInsertBenchmark
{
    private const string TableName = "BenchBulkItems";
    private const int RowsPerIteration = 1000;

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

    [Benchmark(Baseline = true)]
    public async Task PerRowSaveChanges()
    {
        await using var context = new BulkBenchContext(BuildOptions());
        for (var index = 0; index < RowsPerIteration; index++)
        {
            context.Items.Add(
                new BulkBenchEntity
                {
                    Name = $"per-row-{index}",
                    Score = index,
                });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);
        }
    }

    [Benchmark]
    public async Task MultiRowAddRangeSaveChanges()
    {
        await using var context = new BulkBenchContext(BuildOptions());
        var batch = Enumerable
            .Range(0, RowsPerIteration)
            .Select(index => new BulkBenchEntity
            {
                Name = $"bulk-{index}",
                Score = index,
            })
            .ToList();

        context.Items.AddRange(batch);
        await context
            .SaveChangesAsync()
            .ConfigureAwait(false);
    }

    private DbContextOptions<BulkBenchContext> BuildOptions()
    {
        var builder = new DbContextOptionsBuilder<BulkBenchContext>();
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
            + $"CREATE TABLE `{TableName}` ("
            + "  `Id` INT NOT NULL AUTO_INCREMENT,"
            + "  `Name` VARCHAR(64) NOT NULL,"
            + "  `Score` INT NOT NULL,"
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
        command.CommandText = $"TRUNCATE TABLE `{TableName}`;";
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
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class BulkBenchEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    private sealed class BulkBenchContext : DbContext
    {
        public BulkBenchContext(
            DbContextOptions<BulkBenchContext> options
        ) : base(options) { }

        public DbSet<BulkBenchEntity> Items => Set<BulkBenchEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BulkBenchEntity>(builder =>
            {
                builder.ToTable(TableName);
                builder.HasKey(e => e.Id);
                builder
                    .Property(e => e.Name)
                    .HasMaxLength(64);
            });
        }
    }
}
