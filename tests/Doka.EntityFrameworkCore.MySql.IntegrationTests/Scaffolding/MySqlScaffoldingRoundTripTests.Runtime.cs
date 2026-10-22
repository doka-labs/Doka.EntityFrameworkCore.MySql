namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Shares the runtime responsibility without changing the test fixture.</summary>
public sealed partial class MySqlScaffoldingRoundTripTests
{
    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MySql84)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MySql97)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb1011)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb114)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb118)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies compiled generated-code execution and schema reconstruction on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_generated_context_compiles_and_executes()
    {
        await RunGeneratedContextRuntimeAsync(IntegrationDatabaseTarget.MariaDb123)
            .ConfigureAwait(false);
    }

    private static async Task RunGeneratedContextRuntimeAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await CreateSchemaAsync(connectionString)
            .ConfigureAwait(false);
        await CreateIndexAndStoreSchemaAsync(connectionString, target)
            .ConfigureAwait(false);

        try
        {
            var serverVersionText = await ReadServerVersionAsync(connectionString)
                .ConfigureAwait(false);

            await using var serviceProvider =
                ScaffoldingTestServices.CreateDesignTimeServiceProvider(
                    includeSpatialServices: true);

            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions(
                IsMariaDb(target)
                    ?
                    [
                        ParentTable,
                        ChildTable,
                        IndexStoreTable,
                        IndexStoreSequence,
                    ]
                    :
                    [
                        ParentTable,
                        ChildTable,
                        IndexStoreTable,
                        EmulatedSequenceTable,
                    ],
                Array.Empty<string>());

            // Act: The generated assembly recreates the same schema that supplied its model.
            var scaffoldedModel = scopedServices
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(
                        connectionString,
                        contextName: "RuntimeSchemaContext",
                        suppressOnConfiguring: true));

            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);

            await GeneratedContextRuntimeVerifier
                .VerifyAsync(
                    scaffoldedModel,
                    connectionString,
                    serverVersionText)
                .ConfigureAwait(false);

            // Assert
            await AssertRuntimeSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
        finally
        {
            await DropSchemaAsync(connectionString)
                .ConfigureAwait(false);
            await DropIndexAndStoreSchemaAsync(connectionString, target)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertRuntimeSchemaAsync(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        var sequenceExistsSql = IsMariaDb(target)
            ?
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{IndexStoreSequence}'
              AND TABLE_TYPE = 'SEQUENCE';
            """
            :
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{EmulatedSequenceTable}'
              AND TABLE_TYPE = 'BASE TABLE';
            """;

        // Act
        var tableCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('{ParentTable}', '{ChildTable}', '{IndexStoreTable}')
              AND TABLE_TYPE = 'BASE TABLE';
            """)
            .ConfigureAwait(false);

        var foreignKeyCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(*)
            FROM information_schema.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_NAME = '{ForeignKey}';
            """)
            .ConfigureAwait(false);

        var checkCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{ParentTable}'
              AND CONSTRAINT_TYPE = 'CHECK'
              AND CONSTRAINT_NAME = '{CheckConstraint}';
            """)
            .ConfigureAwait(false);

        var foreignKeyIndexCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(DISTINCT INDEX_NAME)
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{ChildTable}'
              AND COLUMN_NAME = 'ParentId'
              AND SEQ_IN_INDEX = 1;
            """)
            .ConfigureAwait(false);

        var jsonConstraintCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(*)
            FROM information_schema.TABLE_CONSTRAINTS AS constraints
            INNER JOIN information_schema.CHECK_CONSTRAINTS AS checks
                ON checks.CONSTRAINT_SCHEMA = constraints.CONSTRAINT_SCHEMA
                AND checks.CONSTRAINT_NAME = constraints.CONSTRAINT_NAME
            WHERE constraints.TABLE_SCHEMA = DATABASE()
              AND constraints.TABLE_NAME = '{IndexStoreTable}'
              AND constraints.CONSTRAINT_TYPE = 'CHECK'
              AND LOWER(checks.CHECK_CLAUSE) LIKE '%json_valid%';
            """)
            .ConfigureAwait(false);

        var backfilledRowCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(*)
            FROM `{ParentTable}`
            WHERE `OptionalCount` = 9
              AND `ComputedCount` = 10;
            """)
            .ConfigureAwait(false);

        var providerIndexCount = await ExecuteScalarInt64Async(
            connection,
            $"""
            SELECT COUNT(DISTINCT INDEX_NAME)
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{IndexStoreTable}'
              AND INDEX_NAME IN ('{PrefixIndex}', '{FullTextIndex}', '{SpatialIndex}', '{UniqueIndex}');
            """)
            .ConfigureAwait(false);

        var sequenceCount = await ExecuteScalarInt64Async(connection, sequenceExistsSql)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(3L, tableCount);
        Assert.Equal(1L, foreignKeyCount);
        Assert.Equal(1L, checkCount);
        Assert.Equal(1L, foreignKeyIndexCount);
        Assert.Equal(IsMariaDb(target) ? 1L : 0L, jsonConstraintCount);
        Assert.Equal(1L, backfilledRowCount);
        Assert.Equal(4L, providerIndexCount);
        Assert.Equal(1L, sequenceCount);
    }
}
