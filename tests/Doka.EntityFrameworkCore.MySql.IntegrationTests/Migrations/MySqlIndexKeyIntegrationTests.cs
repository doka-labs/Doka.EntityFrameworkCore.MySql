namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies index-width safety and provider-owned prefix evolution.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlIndexKeyIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_validates_index_key_contracts() =>
        AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_sync_migration_rejects_silent_index_shortening() =>
        AssertSyncWarningContractAsync(IntegrationDatabaseTarget.MariaDb118);

    private static async Task AssertIndexKeyContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        };
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            await AssertOverlongMigrationFailsWithoutHistoryAsync(connection, serverVersion)
                .ConfigureAwait(false);
            await CleanupAsync(connection).ConfigureAwait(false);
            await AssertBoundaryMigrationPreservesFullIndexAsync(connection, serverVersion)
                .ConfigureAwait(false);
            await CleanupAsync(connection).ConfigureAwait(false);
            await AssertPrefixLengthEvolutionAsync(connection, serverVersion)
                .ConfigureAwait(false);
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task AssertSyncWarningContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        };
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            using var context = new OverlongIndexMigrationContext(
                CreateOptions<OverlongIndexMigrationContext>(
                    connection,
                    serverVersion,
                    IndexKeyMigrationContract.OverlongHistoryTable));

            var exception = Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());

            Assert.Contains("server code 1071", exception.Message, StringComparison.Ordinal);
            Assert.Empty(context.Database.GetAppliedMigrations());
            Assert.Equal(
                768L,
                await ReadIndexPrefixLengthAsync(
                        connection,
                        IndexKeyMigrationContract.OverlongTable,
                        IndexKeyMigrationContract.OverlongIndex)
                    .ConfigureAwait(false));
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task AssertOverlongMigrationFailsWithoutHistoryAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        await using var context = new OverlongIndexMigrationContext(
            CreateOptions<OverlongIndexMigrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.OverlongHistoryTable));

        var exception = await Record.ExceptionAsync(() =>
            context.Database.MigrateAsync(CancellationToken.None));

        Assert.NotNull(exception);
        if (serverVersion.IsMariaDb)
        {
            var invalidOperation = Assert.IsType<InvalidOperationException>(exception);

            Assert.Contains("server code 1071", invalidOperation.Message, StringComparison.Ordinal);
            Assert.Equal(
                768L,
                await ReadIndexPrefixLengthAsync(
                        connection,
                        IndexKeyMigrationContract.OverlongTable,
                        IndexKeyMigrationContract.OverlongIndex)
                    .ConfigureAwait(false));
        }
        else
        {
            var mySqlException = Assert.IsType<MySqlException>(exception);

            Assert.Equal((int)MySqlErrorCode.TooLongKey, mySqlException.Number);
        }

        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(CancellationToken.None).ConfigureAwait(false));
    }

    private static async Task AssertBoundaryMigrationPreservesFullIndexAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        await using var context = new BoundaryIndexMigrationContext(
            CreateOptions<BoundaryIndexMigrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.BoundaryHistoryTable));

        await context.Database.MigrateAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(
            [IndexKeyMigrationContract.BoundaryMigration],
            await context.Database.GetAppliedMigrationsAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Null(
            await ReadIndexPrefixLengthAsync(
                    connection,
                    IndexKeyMigrationContract.BoundaryTable,
                    IndexKeyMigrationContract.BoundaryIndex)
                .ConfigureAwait(false));
    }

    private static async Task AssertPrefixLengthEvolutionAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion
    )
    {
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = $"CREATE TABLE `{IndexKeyMigrationContract.EvolutionTable}` ("
                + "`Id` int NOT NULL, `TenantId` int NOT NULL, `Code` varchar(96) NOT NULL, "
                + "PRIMARY KEY (`Id`), "
                + $"INDEX `{IndexKeyMigrationContract.EvolutionIndex}` (`TenantId`, `Code`(24))); "
                + $"INSERT INTO `{IndexKeyMigrationContract.EvolutionTable}` (`Id`, `TenantId`, `Code`) "
                + "VALUES (1, 7, 'preserved');";
            _ = await setup.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using var source = new SourceIndexPrefixIntegrationContext(
            CreateOptions<SourceIndexPrefixIntegrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.EvolutionHistoryTable));
        await using var target = new TargetIndexPrefixIntegrationContext(
            CreateOptions<TargetIndexPrefixIntegrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.EvolutionHistoryTable));
        var sourceModel = source.GetService<IDesignTimeModel>().Model;
        var targetModel = target.GetService<IDesignTimeModel>().Model;
        var differ = target.GetService<IMigrationsModelDiffer>();

        Assert.True(differ.HasDifferences(sourceModel.GetRelationalModel(), targetModel.GetRelationalModel()));

        var operations = differ.GetDifferences(
            sourceModel.GetRelationalModel(),
            targetModel.GetRelationalModel());
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Equal(IndexKeyMigrationContract.EvolutionIndex, drop.Name);
        Assert.Equal([0, 48], create.GetMySqlMigrationMetadata().IndexPrefixLengths);

        var generator = target.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = target.GetService<IRelationalConnection>();

        foreach (var command in generator.Generate(operations, targetModel))
        {
            _ = await command
                .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        Assert.Equal(
            48,
            await ReadIndexPrefixLengthAsync(
                    connection,
                    IndexKeyMigrationContract.EvolutionTable,
                    IndexKeyMigrationContract.EvolutionIndex,
                    "Code")
                .ConfigureAwait(false));

        await using var verify = connection.CreateCommand();
        verify.CommandText = $"SELECT COUNT(*) FROM `{IndexKeyMigrationContract.EvolutionTable}` "
            + "WHERE `Id` = 1 AND `TenantId` = 7 AND `Code` = 'preserved';";
        Assert.Equal(
            1L,
            Convert.ToInt64(
                await verify.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlConnection connection,
        MySqlServerVersion serverVersion,
        string historyTable
    )
        where TContext : DbContext
    {
        return IntegrationTestDbContextOptions
            .Create<TContext>()
            .UseMySql(
                connection,
                serverVersion,
                options => options
                    .MigrationsAssembly(typeof(MySqlIndexKeyIntegrationTests).Assembly.FullName!)
                    .MigrationsHistoryTable(historyTable))
            .Options;
    }

    private static async Task<long?> ReadIndexPrefixLengthAsync(
        MySqlConnection connection,
        string table,
        string index,
        string? column = null
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `SUB_PART` FROM information_schema.STATISTICS "
            + "WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = @table AND `INDEX_NAME` = @index "
            + "AND (@column IS NULL OR `COLUMN_NAME` = @column) ORDER BY `SEQ_IN_INDEX`;";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@index", index);
        command.Parameters.AddWithValue("@column", column);

        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));

        return await reader.IsDBNullAsync(0, CancellationToken.None).ConfigureAwait(false)
            ? null
            : reader.GetInt64(0);
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.OverlongTable}`; "
            + $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.BoundaryTable}`; "
            + $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.EvolutionTable}`; "
            + $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.OverlongHistoryTable}`; "
            + $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.BoundaryHistoryTable}`; "
            + $"DROP TABLE IF EXISTS `{IndexKeyMigrationContract.EvolutionHistoryTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

internal static class IndexKeyMigrationContract
{
    public const string OverlongMigration = "20260831000000_OverlongIndex";
    public const string BoundaryMigration = "20260831000100_BoundaryIndex";
    public const string OverlongHistoryTable = "__DokaOverlongIndexHistory";
    public const string BoundaryHistoryTable = "__DokaBoundaryIndexHistory";
    public const string EvolutionHistoryTable = "__DokaIndexEvolutionHistory";
    public const string OverlongTable = "DokaOverlongIndexProbe";
    public const string BoundaryTable = "DokaBoundaryIndexProbe";
    public const string EvolutionTable = "DokaIndexEvolutionProbe";
    public const string OverlongIndex = "IX_DokaOverlongIndexProbe_Value";
    public const string BoundaryIndex = "IX_DokaBoundaryIndexProbe_Value";
    public const string EvolutionIndex = "IX_DokaIndexEvolutionProbe_TenantId_Code";
}

internal sealed class OverlongIndexMigrationContext : DbContext
{
    public OverlongIndexMigrationContext(
        DbContextOptions<OverlongIndexMigrationContext> options
    ) : base(options) { }
}

internal sealed class BoundaryIndexMigrationContext : DbContext
{
    public BoundaryIndexMigrationContext(
        DbContextOptions<BoundaryIndexMigrationContext> options
    ) : base(options) { }
}

internal sealed class SourceIndexPrefixIntegrationContext : DbContext
{
    public SourceIndexPrefixIntegrationContext(
        DbContextOptions<SourceIndexPrefixIntegrationContext> options
    ) : base(options) { }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => ConfigureIndex(modelBuilder, 24);

    internal static void ConfigureIndex(
        ModelBuilder modelBuilder,
        int prefixLength
    )
    {
        modelBuilder.Entity<IndexPrefixIntegrationRecord>(entity =>
        {
            entity.ToTable(IndexKeyMigrationContract.EvolutionTable);
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Code)
                .HasMaxLength(96);
            entity
                .HasIndex(record => new
                {
                    record.TenantId,
                    record.Code,
                })
                .HasDatabaseName(IndexKeyMigrationContract.EvolutionIndex)
                .HasPrefixLength(0, prefixLength);
        });
    }
}

internal sealed class TargetIndexPrefixIntegrationContext : DbContext
{
    public TargetIndexPrefixIntegrationContext(
        DbContextOptions<TargetIndexPrefixIntegrationContext> options
    ) : base(options) { }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => SourceIndexPrefixIntegrationContext.ConfigureIndex(modelBuilder, 48);
}

internal sealed class IndexPrefixIntegrationRecord
{
    public int Id { get; set; }

    public int TenantId { get; set; }

    public string Code { get; set; } = string.Empty;
}

[DbContext(typeof(OverlongIndexMigrationContext))]
[Migration(IndexKeyMigrationContract.OverlongMigration)]
internal sealed class OverlongIndexMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .CreateTable(
                name: IndexKeyMigrationContract.OverlongTable,
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(800)", maxLength: 800, nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_DokaOverlongIndexProbe", item => item.Id))
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        migrationBuilder.CreateIndex(
            name: IndexKeyMigrationContract.OverlongIndex,
            table: IndexKeyMigrationContract.OverlongTable,
            column: "Value");
    }

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(IndexKeyMigrationContract.OverlongTable);
}

[DbContext(typeof(BoundaryIndexMigrationContext))]
[Migration(IndexKeyMigrationContract.BoundaryMigration)]
internal sealed class BoundaryIndexMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .CreateTable(
                name: IndexKeyMigrationContract.BoundaryTable,
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(768)", maxLength: 768, nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_DokaBoundaryIndexProbe", item => item.Id))
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        migrationBuilder.CreateIndex(
            name: IndexKeyMigrationContract.BoundaryIndex,
            table: IndexKeyMigrationContract.BoundaryTable,
            column: "Value");
    }

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(IndexKeyMigrationContract.BoundaryTable);
}
