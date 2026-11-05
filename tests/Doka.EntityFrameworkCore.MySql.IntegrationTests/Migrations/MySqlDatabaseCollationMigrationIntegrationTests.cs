namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies database-default collation migrations against every supported server.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlDatabaseCollationMigrationIntegrationTests
{
    /// <summary>
    /// Verifies my sql84 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MySql84, CancellationToken.None);

    /// <summary>
    /// Verifies my sql97 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MySql97, CancellationToken.None);

    /// <summary>
    /// Verifies maria db1011 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb1011, CancellationToken.None);

    /// <summary>
    /// Verifies maria db114 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb114, CancellationToken.None);

    /// <summary>
    /// Verifies maria db118 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    /// <summary>
    /// Verifies maria db123 preserves database collation migration contract.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    /// <summary>
    /// Verifies database collation migration for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task AssertDatabaseCollationMigrationAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var databaseName = $"DokaCollation_{Guid.NewGuid():N}";
        var adminConnectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            Database = string.Empty,
            Pooling = false,
        }.ConnectionString;

        await using var adminConnection = new MySqlConnection(adminConnectionString);
        await adminConnection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                adminConnection,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;",
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var databaseConnectionString = new MySqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
                GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            }.ConnectionString;

            await using var connection = new MySqlConnection(databaseConnectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await CreateExistingPrincipalAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await AssertCharsetOnlyDatabaseChangeCannotSatisfyBrownfieldForeignKeyAsync(
                    connection,
                    target,
                    cancellationToken)
                .ConfigureAwait(false);

            await ResetFailedMigrationStateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await AssertCompleteDatabaseOptionsMigrateAsync(connection, target, cancellationToken)
                .ConfigureAwait(false);

            await AssertIncompatibleForeignKeyIsRejectedAsync(connection, target, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await ExecuteNonQueryAsync(
                    adminConnection,
                    $"DROP DATABASE IF EXISTS `{databaseName}`;",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies charset only database change cannot satisfy brownfield foreign key for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task AssertCharsetOnlyDatabaseChangeCannotSatisfyBrownfieldForeignKeyAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var context = new IncompleteDatabaseCollationMigrationContext(
            CreateOptions<IncompleteDatabaseCollationMigrationContext>(
                connection,
                target,
                DatabaseCollationMigrationContract.IncompleteHistoryTable));

        var migrator = context.GetService<IMigrator>();

        // Act
        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => migrator.MigrateAsync(cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(GetExpectedForeignKeyDefinitionError(target), exception.Number);
        Assert.Empty(
            await context
                .Database
                .GetAppliedMigrationsAsync(cancellationToken)
                .ConfigureAwait(false));

        var databaseCollation = await ReadDatabaseCollationAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        Assert.NotEqual(
            DatabaseCollationMigrationContract.RequiredCollation,
            databaseCollation);

        Assert.NotEqual(DatabaseCollationMigrationContract.InitialCollation, databaseCollation);
    }

    /// <summary>
    /// Verifies complete database options migrate for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task AssertCompleteDatabaseOptionsMigrateAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var context = new DatabaseCollationMigrationContext(
            CreateOptions<DatabaseCollationMigrationContext>(
                connection,
                target,
                DatabaseCollationMigrationContract.HistoryTable));

        var migrator = context.GetService<IMigrator>();
        var initiallyApplied = await context
            .Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        // WHY: The repeated migration verifies the history produced by this same application.
        // Act
        await migrator
            .MigrateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var databaseCollation = await ReadDatabaseCollationAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var tableCollation = await ReadTableCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                cancellationToken)
            .ConfigureAwait(false);

        var columnCollation = await ReadColumnCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                "PrincipalId",
                cancellationToken)
            .ConfigureAwait(false);

        var appliedAfterFirstMigration = await context
            .Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        await AssertTableDefaultTransitionDoesNotConvertColumnsAsync(context, connection, cancellationToken)
            .ConfigureAwait(false);

        await migrator
            .MigrateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var appliedAfterRepeatMigration = await context
            .Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Assert
        Assert.Empty(initiallyApplied);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, databaseCollation);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, tableCollation);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, columnCollation);
        Assert.Equal([DatabaseCollationMigrationContract.MigrationId], appliedAfterFirstMigration);
        Assert.Equal([DatabaseCollationMigrationContract.MigrationId], appliedAfterRepeatMigration);
    }

    /// <summary>
    /// Verifies table default transition does not convert columns for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="context">The context used for the database operation.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task AssertTableDefaultTransitionDoesNotConvertColumnsAsync(
        DbContext context,
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var setExplicitDefault = new AlterTableOperation
        {
            Name = DatabaseCollationMigrationContract.DependentTable,
        };

        setExplicitDefault.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        setExplicitDefault.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        setExplicitDefault.OldTable.SetAnnotation(MySqlAnnotationNames.CharSet, "latin1");
        setExplicitDefault.OldTable.SetAnnotation(
            RelationalAnnotationNames.Collation,
            "latin1_swedish_ci");

        // Act
        var setCommand = Assert.Single(generator.Generate([setExplicitDefault], context.Model));

        await ExecuteNonQueryAsync(connection, setCommand.CommandText, cancellationToken)
            .ConfigureAwait(false);

        var explicitTableCollation = await ReadTableCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                cancellationToken)
            .ConfigureAwait(false);

        var explicitColumnCollation = await ReadColumnCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                "PrincipalId",
                cancellationToken)
            .ConfigureAwait(false);

        var resetToDatabaseDefault = new AlterTableOperation
        {
            Name = DatabaseCollationMigrationContract.DependentTable,
        };

        resetToDatabaseDefault.SetAnnotation(
            RelationalAnnotationNames.Collation,
            DatabaseCollationMigrationContract.RequiredCollation);

        resetToDatabaseDefault.OldTable.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        var resetCommand = Assert.Single(generator.Generate([resetToDatabaseDefault], context.Model));

        await ExecuteNonQueryAsync(connection, resetCommand.CommandText, cancellationToken)
            .ConfigureAwait(false);

        var resetTableCollation = await ReadTableCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                cancellationToken)
            .ConfigureAwait(false);

        var resetColumnCollation = await ReadColumnCollationAsync(
                connection,
                DatabaseCollationMigrationContract.DependentTable,
                "PrincipalId",
                cancellationToken)
            .ConfigureAwait(false);

        // Assert
        Assert.DoesNotContain("CONVERT TO CHARACTER SET", setCommand.CommandText, StringComparison.Ordinal);
        Assert.Equal("utf8mb4_bin", explicitTableCollation);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, explicitColumnCollation);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, resetTableCollation);
        Assert.Equal(DatabaseCollationMigrationContract.RequiredCollation, resetColumnCollation);
    }

    /// <summary>
    /// Verifies incompatible foreign key is rejected for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task AssertIncompatibleForeignKeyIsRejectedAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
                   CREATE TABLE `{DatabaseCollationMigrationContract.IncompatibleTable}` (
                       `Id` int NOT NULL,
                       `PrincipalId` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
                       PRIMARY KEY (`Id`),
                       CONSTRAINT `FK_DokaCollationIncompatible_Principal`
                           FOREIGN KEY (`PrincipalId`)
                           REFERENCES `{DatabaseCollationMigrationContract.PrincipalTable}` (`Id`)
                   ) ENGINE = InnoDB;

                   """;

        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => ExecuteNonQueryAsync(connection, sql, cancellationToken))
            .ConfigureAwait(false);

        Assert.Equal(GetExpectedForeignKeyDefinitionError(target), exception.Number);
    }

    /// <summary>
    /// Creates existing principal for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task CreateExistingPrincipalAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
                   CREATE TABLE `{DatabaseCollationMigrationContract.PrincipalTable}` (
                       `Id` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                       PRIMARY KEY (`Id`)
                   ) ENGINE = InnoDB;

                   """;

        await ExecuteNonQueryAsync(connection, sql, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reset failed migration state for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task ResetFailedMigrationStateAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await ExecuteNonQueryAsync(
                connection,
                $"DROP TABLE IF EXISTS `{DatabaseCollationMigrationContract.DependentTable}`;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                $"DROP TABLE IF EXISTS `{DatabaseCollationMigrationContract.IncompleteHistoryTable}`;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
                connection,
                "ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = "
                + DatabaseCollationMigrationContract.InitialCollation
                + ";",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        string historyTable
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .UseMySql(
            connection,
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options
                .MigrationsAssembly(typeof(MySqlDatabaseCollationMigrationIntegrationTests).Assembly.FullName!)
                .MigrationsHistoryTable(historyTable))
        .Options;

    /// <summary>
    /// Reads database collation for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task<string> ReadDatabaseCollationAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = DATABASE();",
            cancellationToken)
        .ConfigureAwait(false);

    /// <summary>
    /// Reads table collation for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="tableName">The table name.</param>
    private static async Task<string> ReadTableCollationAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT TABLE_COLLATION FROM information_schema.TABLES "
            + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';",
            cancellationToken)
        .ConfigureAwait(false);

    /// <summary>
    /// Reads column collation for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    private static async Task<string> ReadColumnCollationAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT COLLATION_NAME FROM information_schema.COLUMNS "
            + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}';",
            cancellationToken)
        .ConfigureAwait(false);

    /// <summary>
    /// Executes scalar string for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sql">The sql.</param>
    private static async Task<string> ExecuteScalarStringAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Assert.IsType<string>(result);
    }

    /// <summary>
    /// Executes non query for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sql">The sql.</param>
    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static int GetExpectedForeignKeyDefinitionError(
        IntegrationDatabaseTarget target
    )
    {
        // Pin the engine-specific FK error so an earlier syntax or transport
        // failure cannot satisfy the production regression scenario.

        return target switch
        {
            IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MySql97 => 3780,
            IntegrationDatabaseTarget.MariaDb1011
                or IntegrationDatabaseTarget.MariaDb114
                or IntegrationDatabaseTarget.MariaDb118
                or IntegrationDatabaseTarget.MariaDb123 => 1005,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }
}

internal static class DatabaseCollationMigrationContract
{
    /// <summary>
    /// The required collation used by this migration fixture.
    /// </summary>
    public const string RequiredCollation = "utf8mb4_unicode_ci";

    /// <summary>
    /// The initial collation used by this migration fixture.
    /// </summary>
    public const string InitialCollation = "utf8mb4_bin";

    /// <summary>
    /// The principal table used by this migration fixture.
    /// </summary>
    public const string PrincipalTable = "DokaCollationPrincipals";

    /// <summary>
    /// The dependent table used by this migration fixture.
    /// </summary>
    public const string DependentTable = "DokaCollationDependents";

    /// <summary>
    /// The incompatible table used by this migration fixture.
    /// </summary>
    public const string IncompatibleTable = "DokaCollationIncompatible";

    /// <summary>
    /// The history table used by this migration fixture.
    /// </summary>
    public const string HistoryTable = "__DokaCollationHistory";

    /// <summary>
    /// The incomplete history table used by this migration fixture.
    /// </summary>
    public const string IncompleteHistoryTable = "__DokaIncompleteCollationHistory";

    /// <summary>
    /// The migration id used by this migration fixture.
    /// </summary>
    public const string MigrationId = "20260913000000_DatabaseCollation";

    /// <summary>
    /// The incomplete migration id used by this migration fixture.
    /// </summary>
    public const string IncompleteMigrationId = "20260913000000_IncompleteDatabaseCollation";
}

internal sealed class DatabaseCollationMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the DatabaseCollationMigrationContext fixture.
    /// </summary>
    public DatabaseCollationMigrationContext(
        DbContextOptions<DatabaseCollationMigrationContext> options
    ) : base(options) { }
}

internal sealed class IncompleteDatabaseCollationMigrationContext : DbContext
{
    /// <summary>
    /// Initializes the IncompleteDatabaseCollationMigrationContext fixture.
    /// </summary>
    public IncompleteDatabaseCollationMigrationContext(
        DbContextOptions<IncompleteDatabaseCollationMigrationContext> options
    ) : base(options) { }
}

[DbContext(typeof(DatabaseCollationMigrationContext))]
[Migration(DatabaseCollationMigrationContract.MigrationId)]
internal sealed class DatabaseCollationMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .AlterDatabase(collation: DatabaseCollationMigrationContract.RequiredCollation)
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        CreateDependentTable(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.DropTable(DatabaseCollationMigrationContract.DependentTable);
        migrationBuilder
            .AlterDatabase(
                collation: "utf8mb4_bin",
                oldCollation: DatabaseCollationMigrationContract.RequiredCollation)
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4")
            .OldAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
    }

    internal static void CreateDependentTable(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.CreateTable(
        name: DatabaseCollationMigrationContract.DependentTable,
        columns: table => new
        {
            Id = table.Column<int>(type: "int", nullable: false),
            PrincipalId = table.Column<string>(
                type: "char(36)",
                maxLength: 36,
                nullable: false,
                fixedLength: true),
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_DokaCollationDependents", item => item.Id);
            table.ForeignKey(
                "FK_DokaCollationDependents_Principal",
                item => item.PrincipalId,
                DatabaseCollationMigrationContract.PrincipalTable,
                "Id",
                onDelete: ReferentialAction.Cascade);
        });
}

[DbContext(typeof(IncompleteDatabaseCollationMigrationContext))]
[Migration(DatabaseCollationMigrationContract.IncompleteMigrationId)]
internal sealed class IncompleteDatabaseCollationMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .AlterDatabase()
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        DatabaseCollationMigration.CreateDependentTable(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(DatabaseCollationMigrationContract.DependentTable);
}
