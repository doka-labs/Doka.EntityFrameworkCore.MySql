namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies database-default collation migrations against every supported server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlDatabaseCollationMigrationIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_database_collation_migration_contract() =>
        AssertDatabaseCollationMigrationAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertDatabaseCollationMigrationAsync(
        IntegrationDatabaseTarget target
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
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
                adminConnection,
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;")
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
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await CreateExistingPrincipalAsync(connection)
                .ConfigureAwait(false);

            await AssertCharsetOnlyDatabaseChangeCannotSatisfyBrownfieldForeignKeyAsync(connection, target)
                .ConfigureAwait(false);
            await ResetFailedMigrationStateAsync(connection)
                .ConfigureAwait(false);
            await AssertCompleteDatabaseOptionsMigrateAsync(connection, target)
                .ConfigureAwait(false);
            await AssertIncompatibleForeignKeyIsRejectedAsync(connection, target)
                .ConfigureAwait(false);
        }
        finally
        {
            await ExecuteNonQueryAsync(adminConnection, $"DROP DATABASE IF EXISTS `{databaseName}`;")
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertCharsetOnlyDatabaseChangeCannotSatisfyBrownfieldForeignKeyAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new IncompleteDatabaseCollationMigrationContext(
            CreateOptions<IncompleteDatabaseCollationMigrationContext>(
                connection,
                target,
                DatabaseCollationMigrationContract.IncompleteHistoryTable));
        var migrator = context.GetService<IMigrator>();

        var exception = await Assert.ThrowsAsync<MySqlException>(
                () => migrator.MigrateAsync(cancellationToken: CancellationToken.None))
            .ConfigureAwait(false);

        Assert.Equal(GetExpectedForeignKeyDefinitionError(target), exception.Number);
        Assert.Empty(
            await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false));
        var databaseCollation = await ReadDatabaseCollationAsync(connection)
            .ConfigureAwait(false);
        Assert.NotEqual(
            DatabaseCollationMigrationContract.RequiredCollation,
            databaseCollation);
        Assert.NotEqual(DatabaseCollationMigrationContract.InitialCollation, databaseCollation);
    }

    private static async Task AssertCompleteDatabaseOptionsMigrateAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new DatabaseCollationMigrationContext(
            CreateOptions<DatabaseCollationMigrationContext>(
                connection,
                target,
                DatabaseCollationMigrationContract.HistoryTable));
        var migrator = context.GetService<IMigrator>();

        Assert.Empty(
            await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false));

        await migrator
            .MigrateAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadDatabaseCollationAsync(connection)
                .ConfigureAwait(false));
        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadTableCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable)
                .ConfigureAwait(false));
        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadColumnCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable,
                    "PrincipalId")
                .ConfigureAwait(false));
        Assert.Equal(
            [DatabaseCollationMigrationContract.MigrationId],
            await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false));

        await AssertTableDefaultTransitionDoesNotConvertColumnsAsync(context, connection)
            .ConfigureAwait(false);

        await migrator
            .MigrateAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            [DatabaseCollationMigrationContract.MigrationId],
            await context.Database
                .GetAppliedMigrationsAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertTableDefaultTransitionDoesNotConvertColumnsAsync(
        DbContext context,
        MySqlConnection connection
    )
    {
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
        var setCommand = Assert.Single(generator.Generate([setExplicitDefault], context.Model));

        Assert.DoesNotContain("CONVERT TO CHARACTER SET", setCommand.CommandText, StringComparison.Ordinal);
        await ExecuteNonQueryAsync(connection, setCommand.CommandText)
            .ConfigureAwait(false);

        Assert.Equal(
            "utf8mb4_bin",
            await ReadTableCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable)
                .ConfigureAwait(false));
        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadColumnCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable,
                    "PrincipalId")
                .ConfigureAwait(false));

        var resetToDatabaseDefault = new AlterTableOperation
        {
            Name = DatabaseCollationMigrationContract.DependentTable,
        };
        resetToDatabaseDefault.SetAnnotation(
            RelationalAnnotationNames.Collation,
            DatabaseCollationMigrationContract.RequiredCollation);
        resetToDatabaseDefault.OldTable.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        var resetCommand = Assert.Single(generator.Generate([resetToDatabaseDefault], context.Model));

        await ExecuteNonQueryAsync(connection, resetCommand.CommandText)
            .ConfigureAwait(false);

        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadTableCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable)
                .ConfigureAwait(false));
        Assert.Equal(
            DatabaseCollationMigrationContract.RequiredCollation,
            await ReadColumnCollationAsync(
                    connection,
                    DatabaseCollationMigrationContract.DependentTable,
                    "PrincipalId")
                .ConfigureAwait(false));
    }

    private static async Task AssertIncompatibleForeignKeyIsRejectedAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
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

        var exception = await Assert.ThrowsAsync<MySqlException>(
                () => ExecuteNonQueryAsync(connection, sql))
            .ConfigureAwait(false);

        Assert.Equal(GetExpectedForeignKeyDefinitionError(target), exception.Number);
    }

    private static async Task CreateExistingPrincipalAsync(
        MySqlConnection connection
    )
    {
        var sql = $"""
            CREATE TABLE `{DatabaseCollationMigrationContract.PrincipalTable}` (
                `Id` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                PRIMARY KEY (`Id`)
            ) ENGINE = InnoDB;
            """;

        await ExecuteNonQueryAsync(connection, sql)
            .ConfigureAwait(false);
    }

    private static async Task ResetFailedMigrationStateAsync(
        MySqlConnection connection
    )
    {
        await ExecuteNonQueryAsync(
                connection,
                $"DROP TABLE IF EXISTS `{DatabaseCollationMigrationContract.DependentTable}`;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
                connection,
                $"DROP TABLE IF EXISTS `{DatabaseCollationMigrationContract.IncompleteHistoryTable}`;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
                connection,
                "ALTER DATABASE CHARACTER SET = utf8mb4 COLLATE = "
                + DatabaseCollationMigrationContract.InitialCollation
                + ";")
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

    private static async Task<string> ReadDatabaseCollationAsync(
        MySqlConnection connection
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = DATABASE();")
        .ConfigureAwait(false);

    private static async Task<string> ReadTableCollationAsync(
        MySqlConnection connection,
        string tableName
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT TABLE_COLLATION FROM information_schema.TABLES "
            + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';")
        .ConfigureAwait(false);

    private static async Task<string> ReadColumnCollationAsync(
        MySqlConnection connection,
        string tableName,
        string columnName
    ) => await ExecuteScalarStringAsync(
            connection,
            "SELECT COLLATION_NAME FROM information_schema.COLUMNS "
            + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}';")
        .ConfigureAwait(false);

    private static async Task<string> ExecuteScalarStringAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command
            .ExecuteScalarAsync(CancellationToken.None)
            .ConfigureAwait(false);

        return Assert.IsType<string>(result);
    }

    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command
            .ExecuteNonQueryAsync(CancellationToken.None)
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
    public const string RequiredCollation = "utf8mb4_unicode_ci";
    public const string InitialCollation = "utf8mb4_bin";
    public const string PrincipalTable = "DokaCollationPrincipals";
    public const string DependentTable = "DokaCollationDependents";
    public const string IncompatibleTable = "DokaCollationIncompatible";
    public const string HistoryTable = "__DokaCollationHistory";
    public const string IncompleteHistoryTable = "__DokaIncompleteCollationHistory";
    public const string MigrationId = "20260913000000_DatabaseCollation";
    public const string IncompleteMigrationId = "20260913000000_IncompleteDatabaseCollation";
}

internal sealed class DatabaseCollationMigrationContext : DbContext
{
    public DatabaseCollationMigrationContext(
        DbContextOptions<DatabaseCollationMigrationContext> options
    ) : base(options) { }
}

internal sealed class IncompleteDatabaseCollationMigrationContext : DbContext
{
    public IncompleteDatabaseCollationMigrationContext(
        DbContextOptions<IncompleteDatabaseCollationMigrationContext> options
    ) : base(options) { }
}

[DbContext(typeof(DatabaseCollationMigrationContext))]
[Migration(DatabaseCollationMigrationContract.MigrationId)]
internal sealed class DatabaseCollationMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .AlterDatabase(collation: DatabaseCollationMigrationContract.RequiredCollation)
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        CreateDependentTable(migrationBuilder);
    }

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
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder
            .AlterDatabase()
            .Annotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        DatabaseCollationMigration.CreateDependentTable(migrationBuilder);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(DatabaseCollationMigrationContract.DependentTable);
}
