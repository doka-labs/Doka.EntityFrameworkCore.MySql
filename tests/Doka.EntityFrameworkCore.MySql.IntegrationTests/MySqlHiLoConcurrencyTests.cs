namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Live concurrency coverage for the Hi/Lo state cache: many short-lived DbContexts
/// inserting against the same Hi/Lo-backed entity must hand out unique primary keys
/// (correctness). The shared cache lets a freshly resolved context consume the
/// remainder of an existing block instead of round-tripping to the sequence for every
/// insert, but the behavioral guarantee we pin here is the absence of duplicate ids
/// under concurrent inserts; cache-instance sharing itself is covered by the unit
/// tests in MySqlHiLoStateCacheTests.
/// </summary>
public sealed class MySqlHiLoConcurrencyTests
{
    private const string SequenceName = "hilo_concurrency_seq";
    private const string TableName = "HiLoConcurrencyItems";
    private const int ContextCount = 10;
    private const int InsertsPerContext = 25;

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task HiLo_inserts_across_parallel_contexts_yield_unique_ids()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);
        MySqlHiLoStateCache.ResetForTesting();

        try
        {
            var seenIds = new ConcurrentBag<int>();

            await Parallel
                .ForEachAsync(
                    Enumerable.Range(0, ContextCount),
                    async (_, cancellationToken) =>
                    {
                        await using var context = new HiLoContext(BuildOptions(connectionString));

                        for (var insertIndex = 0; insertIndex < InsertsPerContext; insertIndex++)
                        {
                            var entity = new HiLoEntity { Name = $"row-{Guid.NewGuid():N}" };
                            context.Items.Add(entity);
                            await context
                                .SaveChangesAsync(cancellationToken)
                                .ConfigureAwait(false);
                            seenIds.Add(entity.Id);
                        }
                    })
                .ConfigureAwait(false);

            var expectedCount = ContextCount * InsertsPerContext;
            Assert.Equal(expectedCount, seenIds.Count);
            Assert.Equal(expectedCount, seenIds.Distinct().Count());
        }
        finally
        {
            await TearDownSchemaAsync(connectionString)
                .ConfigureAwait(false);
            MySqlHiLoStateCache.ResetForTesting();
        }
    }

    private static DbContextOptions<HiLoContext> BuildOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<HiLoContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static async Task PrepareSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP TABLE IF EXISTS `{TableName}`;"
            + $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;"
            + $"CREATE TABLE `__efsequence_{SequenceName}` (`value` BIGINT NOT NULL) ENGINE=InnoDB;"
            + $"INSERT INTO `__efsequence_{SequenceName}` (`value`) VALUES (0);"
            + $"CREATE TABLE `{TableName}` ("
            + "  `Id` INT NOT NULL,"
            + "  `Name` VARCHAR(64) NOT NULL,"
            + "  PRIMARY KEY (`Id`)"
            + ") CHARACTER SET utf8mb4;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task TearDownSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP TABLE IF EXISTS `{TableName}`;"
            + $"DROP TABLE IF EXISTS `__efsequence_{SequenceName}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class HiLoEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class HiLoContext : DbContext
    {
        public HiLoContext(
            DbContextOptions<HiLoContext> options
        ) : base(options) { }

        public DbSet<HiLoEntity> Items => Set<HiLoEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoEntity>(builder =>
            {
                builder.ToTable(TableName);
                builder.HasKey(e => e.Id);
                builder
                    .Property(e => e.Id)
                    .UseHiLo(SequenceName);
                builder.Property(e => e.Name).HasMaxLength(64);
            });
        }
    }
}
