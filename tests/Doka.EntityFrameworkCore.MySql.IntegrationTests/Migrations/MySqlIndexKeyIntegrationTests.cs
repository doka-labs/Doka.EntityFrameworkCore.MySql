namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies index-width safety and provider-owned prefix evolution.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlIndexKeyIntegrationTests
{
    /// <summary>
    /// Verifies my sql84 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MySql84_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MySql84, scenario);

    /// <summary>
    /// Verifies my sql97 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql97 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MySql97_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MySql97, scenario);

    /// <summary>
    /// Verifies maria db1011 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb1011 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MariaDb1011_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb1011, scenario);

    /// <summary>
    /// Verifies maria db114 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb114 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MariaDb114_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb114, scenario);

    /// <summary>
    /// Verifies maria db118 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb118 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MariaDb118_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb118, scenario);

    /// <summary>
    /// Verifies maria db123 validates index key contracts.
    /// </summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MariaDb123 })]
    [InlineData(IndexKeyScenario.Overlong)]
    [InlineData(IndexKeyScenario.Boundary)]
    [InlineData(IndexKeyScenario.PrefixEvolution)]
    public Task MariaDb123_validates_index_key_contracts(
        IndexKeyScenario scenario
    ) => AssertIndexKeyContractAsync(IntegrationDatabaseTarget.MariaDb123, scenario);

    /// <summary>
    /// Verifies maria db118 sync migration rejects silent index shortening.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_sync_migration_rejects_silent_index_shortening() =>
        AssertSyncWarningContractAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>Identifies independent physical-index migration contracts.</summary>
    public enum IndexKeyScenario
    {
        /// <summary>Rejects a key exceeding the server limit without advancing history.</summary>
        Overlong,

        /// <summary>Preserves the complete indexed value at the byte boundary.</summary>
        Boundary,

        /// <summary>Rebuilds an existing prefix while preserving rows.</summary>
        PrefixEvolution,
    }

    private static async Task AssertIndexKeyContractAsync(
        IntegrationDatabaseTarget target,
        IndexKeyScenario scenario
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
            await (scenario switch
            {
                IndexKeyScenario.Overlong => AssertOverlongMigrationFailsWithoutHistoryAsync(connection, serverVersion),
                IndexKeyScenario.Boundary => AssertBoundaryMigrationPreservesFullIndexAsync(connection, serverVersion),
                IndexKeyScenario.PrefixEvolution => AssertPrefixLengthEvolutionAsync(connection, serverVersion),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
            }).ConfigureAwait(false);
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
        // Arrange
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
            await using var context = new OverlongIndexMigrationContext(
                CreateOptions<OverlongIndexMigrationContext>(
                    connection,
                    serverVersion,
                    IndexKeyMigrationContract.OverlongHistoryTable));

            // Act
            // WHY: This case exercises the synchronous migration warning path independently of MigrateAsync.
            // ReSharper disable once MethodHasAsyncOverload
            var exception = Assert.Throws<InvalidOperationException>(context.Database.Migrate);

            // Assert
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
        // Arrange
        await using var context = new OverlongIndexMigrationContext(
            CreateOptions<OverlongIndexMigrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.OverlongHistoryTable));

        // Act
        var exception = await Record.ExceptionAsync(() =>
            context.Database.MigrateAsync(CancellationToken.None));

        // Assert
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
        // Arrange
        await using var context = new BoundaryIndexMigrationContext(
            CreateOptions<BoundaryIndexMigrationContext>(
                connection,
                serverVersion,
                IndexKeyMigrationContract.BoundaryHistoryTable));

        // Act
        await context.Database.MigrateAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
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
        // Arrange
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

        // Act
        var hasDifferences = differ.HasDifferences(sourceModel.GetRelationalModel(), targetModel.GetRelationalModel());
        var operations = differ.GetDifferences(
            sourceModel.GetRelationalModel(),
            targetModel.GetRelationalModel());

        var generator = target.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = target.GetService<IRelationalConnection>();

        foreach (var command in generator.Generate(operations, targetModel))
        {
            _ = await command
                .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        var physicalPrefixLength = await ReadIndexPrefixLengthAsync(
                    connection,
                    IndexKeyMigrationContract.EvolutionTable,
                    IndexKeyMigrationContract.EvolutionIndex,
                    "Code")
                .ConfigureAwait(false);

        await using var verify = connection.CreateCommand();
        verify.CommandText = $"SELECT COUNT(*) FROM `{IndexKeyMigrationContract.EvolutionTable}` "
            + "WHERE `Id` = 1 AND `TenantId` = 7 AND `Code` = 'preserved';";
        var preservedRows = Convert.ToInt64(
                await verify.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

        // Assert
        Assert.True(hasDifferences);
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.Equal(IndexKeyMigrationContract.EvolutionIndex, drop.Name);
        Assert.Equal([0, 48], create.GetMySqlMigrationMetadata().IndexPrefixLengths);
        Assert.Equal(48, physicalPrefixLength);
        Assert.Equal(1L, preservedRows);
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
    /// <summary>
    /// The overlong migration used by this migration fixture.
    /// </summary>
    public const string OverlongMigration = "20260831000000_OverlongIndex";

    /// <summary>
    /// The boundary migration used by this migration fixture.
    /// </summary>
    public const string BoundaryMigration = "20260831000100_BoundaryIndex";

    /// <summary>
    /// The overlong history table used by this migration fixture.
    /// </summary>
    public const string OverlongHistoryTable = "__DokaOverlongIndexHistory";

    /// <summary>
    /// The boundary history table used by this migration fixture.
    /// </summary>
    public const string BoundaryHistoryTable = "__DokaBoundaryIndexHistory";

    /// <summary>
    /// The evolution history table used by this migration fixture.
    /// </summary>
    public const string EvolutionHistoryTable = "__DokaIndexEvolutionHistory";

    /// <summary>
    /// The overlong table used by this migration fixture.
    /// </summary>
    public const string OverlongTable = "DokaOverlongIndexProbe";

    /// <summary>
    /// The boundary table used by this migration fixture.
    /// </summary>
    public const string BoundaryTable = "DokaBoundaryIndexProbe";

    /// <summary>
    /// The evolution table used by this migration fixture.
    /// </summary>
    public const string EvolutionTable = "DokaIndexEvolutionProbe";

    /// <summary>
    /// The overlong index used by this migration fixture.
    /// </summary>
    public const string OverlongIndex = "IX_DokaOverlongIndexProbe_Value";

    /// <summary>
    /// The boundary index used by this migration fixture.
    /// </summary>
    public const string BoundaryIndex = "IX_DokaBoundaryIndexProbe_Value";

    /// <summary>
    /// The evolution index used by this migration fixture.
    /// </summary>
    public const string EvolutionIndex = "IX_DokaIndexEvolutionProbe_TenantId_Code";
}

internal sealed class OverlongIndexMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the OverlongIndexMigrationContext fixture.
    /// </summary>
    public OverlongIndexMigrationContext(
        DbContextOptions<OverlongIndexMigrationContext> options
    ) : base(options) { }
}

internal sealed class BoundaryIndexMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the BoundaryIndexMigrationContext fixture.
    /// </summary>
    public BoundaryIndexMigrationContext(
        DbContextOptions<BoundaryIndexMigrationContext> options
    ) : base(options) { }
}

internal sealed class SourceIndexPrefixIntegrationContext : DbContext
{
    /// <summary>
    /// Initializes the SourceIndexPrefixIntegrationContext fixture.
    /// </summary>
    public SourceIndexPrefixIntegrationContext(
        DbContextOptions<SourceIndexPrefixIntegrationContext> options
    ) : base(options) { }

    /// <inheritdoc />
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
    /// <summary>
    /// Initializes the TargetIndexPrefixIntegrationContext fixture.
    /// </summary>
    public TargetIndexPrefixIntegrationContext(
        DbContextOptions<TargetIndexPrefixIntegrationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => SourceIndexPrefixIntegrationContext.ConfigureIndex(modelBuilder, 48);
}

internal sealed class IndexPrefixIntegrationRecord
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant id.
    /// </summary>
    public int TenantId { get; set; }

    /// <summary>
    /// Gets or sets the code.
    /// </summary>
    public string Code { get; set; } = string.Empty;
}

[DbContext(typeof(OverlongIndexMigrationContext))]
[Migration(IndexKeyMigrationContract.OverlongMigration)]
internal sealed class OverlongIndexMigration : Migration
{
    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(IndexKeyMigrationContract.OverlongTable);
}

[DbContext(typeof(BoundaryIndexMigrationContext))]
[Migration(IndexKeyMigrationContract.BoundaryMigration)]
internal sealed class BoundaryIndexMigration : Migration
{
    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(IndexKeyMigrationContract.BoundaryTable);
}
