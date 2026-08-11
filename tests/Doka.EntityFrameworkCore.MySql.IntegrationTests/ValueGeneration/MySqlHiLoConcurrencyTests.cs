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
[Collection(IntegrationDatabaseTestGroup.Name)]
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

    /// <summary>
    /// Block-cache efficiency check: when many parallel inserts on the same Hi/Lo-backed
    /// entity share one process-wide block cache, the sequence-update SQL fires once per
    /// drained block rather than once per insert. The default Hi/Lo block size is 10, so
    /// 50 parallel inserts must produce ceil(50 / 10) = 5 sequence round-trips, not 50.
    /// Roundtrips are counted via a server-side AFTER UPDATE trigger on the emulation
    /// table so the assertion measures server-side block leases independently from
    /// diagnostic or interceptor implementation details.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Fifty_parallel_savechanges_use_block_caching_to_minimize_sequence_roundtrips()
    {
        const int totalInserts = 50;
        const int defaultBlockSize = 10;
        const int expectedRoundtrips = totalInserts / defaultBlockSize;

        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);
        await PrepareSequenceAuditAsync(connectionString)
            .ConfigureAwait(false);
        MySqlHiLoStateCache.ResetForTesting();

        try
        {
            var seenIds = new ConcurrentBag<int>();

            await Parallel
                .ForEachAsync(
                    Enumerable.Range(0, totalInserts),
                    async (_, cancellationToken) =>
                    {
                        await using var context = new HiLoContext(BuildOptions(connectionString));
                        var entity = new HiLoEntity { Name = $"row-{Guid.NewGuid():N}" };
                        context.Items.Add(entity);
                        await context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                        seenIds.Add(entity.Id);
                    })
                .ConfigureAwait(false);

            var roundtripCount = await ReadSequenceAuditCountAsync(connectionString)
                .ConfigureAwait(false);

            Assert.Equal(totalInserts, seenIds.Count);
            Assert.Equal(totalInserts, seenIds.Distinct().Count());
            Assert.Equal(expectedRoundtrips, roundtripCount);
        }
        finally
        {
            await TearDownSequenceAuditAsync(connectionString)
                .ConfigureAwait(false);
            await TearDownSchemaAsync(connectionString)
                .ConfigureAwait(false);
            MySqlHiLoStateCache.ResetForTesting();
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task HiLo_uses_configured_data_source_and_leaves_it_usable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);
        MySqlHiLoStateCache.ResetForTesting();

        try
        {
            await using var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
            await using (var context = new HiLoContext(BuildOptions(dataSource)))
            {
                var entity = new HiLoEntity { Name = "data-source" };
                context.Items.Add(entity);
                await context
                    .SaveChangesAsync()
                    .ConfigureAwait(false);

                Assert.Equal(1, entity.Id);
            }

            await using var verificationConnection = await dataSource
                .OpenConnectionAsync()
                .ConfigureAwait(false);

            Assert.Equal(ConnectionState.Open, verificationConnection.State);
        }
        finally
        {
            await TearDownSchemaAsync(connectionString)
                .ConfigureAwait(false);
            MySqlHiLoStateCache.ResetForTesting();
        }
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task HiLo_uses_external_connection_without_taking_ownership()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await PrepareSchemaAsync(connectionString)
            .ConfigureAwait(false);
        MySqlHiLoStateCache.ResetForTesting();

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection
                .OpenAsync()
                .ConfigureAwait(false);

            await using (var context = new HiLoContext(BuildOptions(connection)))
            {
                var entity = new HiLoEntity { Name = "external-connection" };
                context.Items.Add(entity);
                await context
                    .SaveChangesAsync()
                    .ConfigureAwait(false);

                Assert.Equal(1, entity.Id);
            }

            Assert.Equal(ConnectionState.Open, connection.State);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM `{TableName}`;";
            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await command
                        .ExecuteScalarAsync()
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
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
        var builder = IntegrationTestDbContextOptions.Create<HiLoContext>();
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static DbContextOptions<HiLoContext> BuildOptions(
        MySqlDataSource dataSource
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<HiLoContext>();
        builder.UseMySql(dataSource, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private static DbContextOptions<HiLoContext> BuildOptions(
        DbConnection connection
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<HiLoContext>();
        builder.UseMySql(connection, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return builder.Options;
    }

    private const string SequenceAuditTable = "hilo_sequence_audit";
    private const string SequenceAuditTrigger = "trg_hilo_sequence_audit";

    private static async Task PrepareSequenceAuditAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TRIGGER IF EXISTS `{SequenceAuditTrigger}`;"
            + $"DROP TABLE IF EXISTS `{SequenceAuditTable}`;"
            + $"CREATE TABLE `{SequenceAuditTable}` ("
            + "  `id` INT NOT NULL AUTO_INCREMENT,"
            + "  `at` TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),"
            + "  PRIMARY KEY (`id`)"
            + ") ENGINE=InnoDB CHARACTER SET utf8mb4;"
            + $"CREATE TRIGGER `{SequenceAuditTrigger}` AFTER UPDATE ON `__efsequence_{SequenceName}`"
            + $"  FOR EACH ROW INSERT INTO `{SequenceAuditTable}` (`at`) VALUES (CURRENT_TIMESTAMP(6));";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task<int> ReadSequenceAuditCountAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{SequenceAuditTable}`;";
        var result = await command
            .ExecuteScalarAsync()
            .ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task TearDownSequenceAuditAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TRIGGER IF EXISTS `{SequenceAuditTrigger}`;"
            + $"DROP TABLE IF EXISTS `{SequenceAuditTable}`;";
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
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
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;"
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

    private static async Task TearDownSchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
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
                builder
                    .Property(e => e.Name)
                    .HasMaxLength(64);
            });
        }
    }
}
