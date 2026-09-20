namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the temporal-table contract against every supported live engine.
/// </summary>
[IntegrationDatabaseTest]
public sealed class TemporalTableIntegrationTests
{
    private const string TableName = "IntTemporalItems";
    private const string HistoryTableName = "IntTemporalItemsHistory";
    private const string PeriodStartColumnName = "ValidFrom";

    private const string PeriodEndColumnName = "ValidTo";

    // WHY: The history schema is part of this test's fixed model and source
    // assertions. No other parallel class uses the container's base database.
    private const string TestDatabaseName = "doka_provider";

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Temporal_contract_executes_on_mysql84() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MySql84, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Temporal_contract_executes_on_mysql97() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MySql97, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Temporal_contract_executes_on_mariadb1011() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb1011, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Temporal_contract_executes_on_mariadb114() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb114, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Temporal_contract_executes_on_mariadb118() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Temporal_contract_executes_on_mariadb123() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    /// <summary>Runs the temporal contract integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunTemporalContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var connectionString = IntegrationTestEnvironment.GetBaseConnectionString(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken: cancellationToken);
        await CleanupAsync(connection, cancellationToken);
        var initialSessionState = await ReadSessionStateAsync(connection, target, cancellationToken);

        try
        {
            await RunInitialCreateAndDropAsync(connection, target, cancellationToken);
            await RunTransitionLifecycleAsync(connection, target, cancellationToken);
            await RunRollbackLifecycleAsync(connection, target, cancellationToken);
            await RunConcurrencyLifecycleAsync(connection, target, cancellationToken);

            // Native MariaDB transitions require a temporary session variable.
            // The provider scopes it to each DDL statement so caller-owned
            // session state remains unchanged after the migration lifecycle.
            Assert.Equal(initialSessionState, await ReadSessionStateAsync(connection, target, cancellationToken));
        }
        finally
        {
            // This cleanup is only failure recovery. All contract DDL above is
            // generated from the EF model by the provider under test.
            await CleanupAsync(connection, CancellationToken.None);
        }
    }

    /// <summary>Runs the initial create and drop integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunInitialCreateAndDropAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(
            temporalContext,
            connection,
            GetDifferences(emptyContext, temporalContext),
            cancellationToken);

        await AssertPhysicalContractAsync(connection, target, cancellationToken);
        AssertScaffoldingContract(connection.ConnectionString, target);
        await WriteVersionsAsync(temporalContext, cancellationToken);
        await AssertTemporalQueriesAsync(target, cancellationToken);
        await AssertCancellationContractAsync(target, cancellationToken);

        await ExecuteOperationsAsync(
            emptyContext,
            connection,
            GetDifferences(temporalContext, emptyContext),
            cancellationToken);

        await AssertRemovedAsync(connection, cancellationToken);
    }

    /// <summary>Runs the rollback lifecycle integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunRollbackLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(
            temporalContext,
            connection,
            GetDifferences(emptyContext, temporalContext),
            cancellationToken);

        await using (var transaction = await temporalContext.Database.BeginTransactionAsync(
            cancellationToken: cancellationToken))
        {
            var item = new TemporalItem { Name = "rolled-back-version-1" };

            temporalContext.Items.Add(item);
            await temporalContext.SaveChangesAsync(cancellationToken: cancellationToken);

            item.Name = "rolled-back-version-2";
            await temporalContext.SaveChangesAsync(cancellationToken: cancellationToken);
            await transaction.RollbackAsync(cancellationToken: cancellationToken);
        }

        await using (var verificationContext = CreateTemporalContext(target))
        {
            Assert.Empty(await verificationContext.Items.ToListAsync(cancellationToken: cancellationToken));
            Assert.Empty(
                await verificationContext
                    .Items
                    .TemporalAll()
                    .ToListAsync(cancellationToken: cancellationToken));
        }

        await ExecuteOperationsAsync(
            emptyContext,
            connection,
            GetDifferences(temporalContext, emptyContext),
            cancellationToken);

        await AssertRemovedAsync(connection, cancellationToken);
    }

    /// <summary>Runs the concurrency lifecycle integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunConcurrencyLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(
            temporalContext,
            connection,
            GetDifferences(emptyContext, temporalContext),
            cancellationToken);

        temporalContext.Items.Add(new TemporalItem { Name = "original" });
        await temporalContext.SaveChangesAsync(cancellationToken: cancellationToken);

        await using (var winnerContext = CreateTemporalContext(target))
        await using (var staleContext = CreateTemporalContext(target))
        {
            var winner = await winnerContext.Items.SingleAsync(cancellationToken: cancellationToken);
            var stale = await staleContext.Items.SingleAsync(cancellationToken: cancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken: cancellationToken);

            winner.Name = "winner";
            await winnerContext.SaveChangesAsync(cancellationToken: cancellationToken);

            stale.Name = "stale";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                staleContext.SaveChangesAsync(cancellationToken: cancellationToken));
        }

        await using (var verificationContext = CreateTemporalContext(target))
        {
            Assert.Equal(
                ["winner"],
                await verificationContext
                    .Items
                    .Select(item => item.Name)
                    .ToListAsync(cancellationToken: cancellationToken));

            Assert.Equal(
                ["original", "winner"],
                await ReadNamesAsync(verificationContext.Items.TemporalAll(), cancellationToken));
        }

        await ExecuteOperationsAsync(
            emptyContext,
            connection,
            GetDifferences(temporalContext, emptyContext),
            cancellationToken);

        await AssertRemovedAsync(connection, cancellationToken);
    }

    /// <summary>Runs the transition lifecycle integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunTransitionLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var nonTemporalContext = CreateNonTemporalContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(
            nonTemporalContext,
            connection,
            GetDifferences(emptyContext, nonTemporalContext),
            cancellationToken);

        await AssertNonTemporalContractAsync(connection, cancellationToken);

        await ExecuteOperationsAsync(
            temporalContext,
            connection,
            GetDifferences(nonTemporalContext, temporalContext),
            cancellationToken);

        await AssertPhysicalContractAsync(connection, target, cancellationToken);
        await WriteVersionsAsync(temporalContext, cancellationToken);
        await AssertTemporalQueriesAsync(target, cancellationToken);
        await AssertCancellationContractAsync(target, cancellationToken);

        await ExecuteOperationsAsync(
            nonTemporalContext,
            connection,
            GetDifferences(temporalContext, nonTemporalContext),
            cancellationToken);

        await AssertNonTemporalContractAsync(connection, cancellationToken);

        await ExecuteOperationsAsync(
            emptyContext,
            connection,
            GetDifferences(nonTemporalContext, emptyContext),
            cancellationToken);

        await AssertRemovedAsync(connection, cancellationToken);
    }

    /// <summary>Executes the write versions database scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task WriteVersionsAsync(
        TemporalContext context,
        CancellationToken cancellationToken
    )
    {
        var item = new TemporalItem { Name = "version-1" };

        context.Items.Add(item);
        await context.SaveChangesAsync(cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken: cancellationToken);

        item.Name = "version-2";
        await context.SaveChangesAsync(cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken: cancellationToken);

        context.Items.Remove(item);
        await context.SaveChangesAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Verifies the temporal queries contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertTemporalQueriesAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var context = CreateTemporalContext(target);
        var expectedVersions = new[] { "version-1", "version-2" };

        // Act
        var versions = await context
            .Items
            .TemporalAll()
            .Select(item => new
            {
                item.Name,
                ValidFrom = EF.Property<DateTime>(item, PeriodStartColumnName),
                ValidTo = EF.Property<DateTime>(item, PeriodEndColumnName),
            })
            .OrderBy(version => version.ValidFrom)
            .ToListAsync(cancellationToken: cancellationToken);

        var firstFrom = AsUtc(versions[0].ValidFrom);
        var firstTo = AsUtc(versions[0].ValidTo);
        var secondFrom = AsUtc(versions[1].ValidFrom);
        var secondTo = AsUtc(versions[1].ValidTo);
        var firstBoundary = await ReadNamesAsync(context.Items.TemporalAsOf(firstFrom), cancellationToken);
        var firstMidpoint = await ReadNamesAsync(
            context.Items.TemporalAsOf(GetMidpoint(firstFrom, firstTo)),
            cancellationToken);

        var secondBoundary = await ReadNamesAsync(context.Items.TemporalAsOf(firstTo), cancellationToken);
        var secondMidpoint = await ReadNamesAsync(
            context.Items.TemporalAsOf(GetMidpoint(secondFrom, secondTo)),
            cancellationToken);

        var afterDeletion = await ReadNamesAsync(context.Items.TemporalAsOf(secondTo), cancellationToken);
        var emptyRange = await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, firstFrom), cancellationToken);
        var firstRange = await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, firstTo), cancellationToken);
        var secondRange = await ReadNamesAsync(context.Items.TemporalFromTo(firstTo, secondTo), cancellationToken);
        var entireRange = await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, secondTo), cancellationToken);
        var inclusiveFirstBoundary = await ReadNamesAsync(
            context.Items.TemporalBetween(firstFrom, firstFrom),
            cancellationToken);

        var inclusiveSecondRange = await ReadNamesAsync(
            context.Items.TemporalBetween(firstTo, secondTo),
            cancellationToken);

        var inclusiveEntireRange = await ReadNamesAsync(
            context.Items.TemporalBetween(firstFrom, secondTo),
            cancellationToken);

        var containedFirstRange = await ReadNamesAsync(
            context.Items.TemporalContainedIn(firstFrom, firstTo),
            cancellationToken);

        var containedSecondRange = await ReadNamesAsync(
            context.Items.TemporalContainedIn(secondFrom, secondTo),
            cancellationToken);

        var containedEntireRange = await ReadNamesAsync(
            context.Items.TemporalContainedIn(firstFrom, secondTo),
            cancellationToken);

        var allVersions = await ReadNamesAsync(context.Items.TemporalAll(), cancellationToken);
        var versionCount = await context
            .Items
            .TemporalAll()
            .CountAsync(cancellationToken: cancellationToken);

        var secondVersionExists = await context
            .Items
            .TemporalAll()
            .AnyAsync(
                item => item.Name == "version-2",
                cancellationToken: cancellationToken);

        var currentItems = await context.Items.ToListAsync(cancellationToken: cancellationToken);

        // Assert
        Assert.Collection(
            versions,
            version => Assert.Equal("version-1", version.Name),
            version => Assert.Equal("version-2", version.Name));

        Assert.True(firstFrom < firstTo);
        Assert.Equal(firstTo, secondFrom);
        Assert.True(secondFrom < secondTo);

        Assert.Equal(["version-1"], firstBoundary);
        Assert.Equal(["version-1"], firstMidpoint);
        Assert.Equal(["version-2"], secondBoundary);
        Assert.Equal(["version-2"], secondMidpoint);
        Assert.Empty(afterDeletion);
        Assert.Empty(emptyRange);
        Assert.Equal(["version-1"], firstRange);
        Assert.Equal(["version-2"], secondRange);
        Assert.Equal(expectedVersions, entireRange);
        Assert.Equal(["version-1"], inclusiveFirstBoundary);
        Assert.Equal(["version-2"], inclusiveSecondRange);
        Assert.Equal(expectedVersions, inclusiveEntireRange);
        Assert.Equal(["version-1"], containedFirstRange);
        Assert.Equal(["version-2"], containedSecondRange);
        Assert.Equal(expectedVersions, containedEntireRange);
        Assert.Equal(expectedVersions, allVersions);
        Assert.Equal(2, versionCount);
        Assert.True(secondVersionExists);
        Assert.Empty(currentItems);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Verifies the cancellation contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertCancellationContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var interceptor = new CommandCancellationProbeInterceptor();

        await using var context = CreateTemporalContext(target, interceptor);

        // WHY: Keep the deliberate probe token distinct while honoring cancellation from its caller.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var probeCancellationToken = cancellationSource.Token;

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context
            .Items
            .TemporalAll()
            .CountAsync(probeCancellationToken));

        // Assert
        Assert.Equal(probeCancellationToken, exception.CancellationToken);
        Assert.Equal(probeCancellationToken, interceptor.ReceivedCancellationToken);
        Assert.Equal(1, interceptor.InvocationCount);
    }

    private static void AssertScaffoldingContract(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions(
            [
                TableName,
                HistoryTableName,
            ],
            Array.Empty<string>());

        var databaseModel = scopedServices
            .GetRequiredService<IDatabaseModelFactory>()
            .Create(connectionString, databaseOptions);

        // Act
        var sourceTable = Assert.Single(databaseModel.Tables);

        // Assert
        Assert.Equal(TableName, sourceTable.Name);
        Assert.True(
            sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value as bool?);

        Assert.Equal(
            PeriodStartColumnName,
            sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)
                ?.Value);

        Assert.Equal(
            PeriodEndColumnName,
            sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)
                ?.Value);

        if (IsMySql(target))
        {
            Assert.Equal(
                HistoryTableName,
                sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)
                    ?.Value);

            Assert.Equal(
                TestDatabaseName,
                sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistorySchema)
                    ?.Value);
        }
        else
        {
            Assert.Null(sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable));
            Assert.Null(sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistorySchema));
        }

        var scaffoldedModel = scopedServices
            .GetRequiredService<IReverseEngineerScaffolder>()
            .ScaffoldModel(
                connectionString,
                databaseOptions,
                new ModelReverseEngineerOptions(),
                ScaffoldingTestServices.CreateCodeGenerationOptions(
                    connectionString,
                    contextName: "TemporalSchemaContext"));

        var contextCode = scaffoldedModel.ContextFile.Code;

        Assert.Contains("tableBuilder.IsTemporal(temporalTableBuilder =>", contextCode);
        Assert.Contains($".HasPeriodStart(\"{PeriodStartColumnName}\")", contextCode);
        Assert.Contains($".HasPeriodEnd(\"{PeriodEndColumnName}\")", contextCode);
        Assert.Contains($".HasColumnName(\"{PeriodStartColumnName}\")", contextCode);
        Assert.Contains($".HasColumnName(\"{PeriodEndColumnName}\")", contextCode);

        if (IsMySql(target))
        {
            Assert.Contains(
                $"temporalTableBuilder.UseHistoryTable(\"{HistoryTableName}\", " + $"\"{TestDatabaseName}\")",
                contextCode);
        }
        else
        {
            Assert.DoesNotContain("temporalTableBuilder.UseHistoryTable(", contextCode);
        }
    }

    /// <summary>Reads the names from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static Task<List<string>> ReadNamesAsync(
        IQueryable<TemporalItem> query,
        CancellationToken cancellationToken
    )
    {
        return query
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Verifies the physical contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertPhysicalContractAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection, TableName, cancellationToken));
        Assert.Equal(
            2,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                  AND COLUMN_NAME IN (@periodStart, @periodEnd)
                  AND DATA_TYPE = @dataType
                  AND DATETIME_PRECISION = 6
                  AND IS_NULLABLE = 'NO';
                """,
                cancellationToken,
                ("@tableName", TableName),
                ("@periodStart", PeriodStartColumnName),
                ("@periodEnd", PeriodEndColumnName),
                ("@dataType", IsMySql(target)
                    ? "datetime"
                    : "timestamp")));

        if (IsMySql(target))
        {
            Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, TableName, cancellationToken));
            Assert.Equal("InnoDB", await ReadTableEngineAsync(connection, TableName, cancellationToken));
            Assert.Equal(1, await CountTablesAsync(connection, HistoryTableName, cancellationToken));
            Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, HistoryTableName, cancellationToken));
            Assert.Equal("InnoDB", await ReadTableEngineAsync(connection, HistoryTableName, cancellationToken));
            Assert.Equal(
                await CountColumnsAsync(connection, TableName, cancellationToken),
                await CountColumnsAsync(connection, HistoryTableName, cancellationToken));

            Assert.Equal(
                3,
                await CountAsync(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM information_schema.TRIGGERS
                    WHERE TRIGGER_SCHEMA = DATABASE()
                      AND EVENT_OBJECT_TABLE = @tableName
                      AND ACTION_TIMING = 'BEFORE'
                      AND EVENT_MANIPULATION IN ('INSERT', 'UPDATE', 'DELETE');
                    """,
                    cancellationToken,
                    ("@tableName", TableName)));

            return;
        }

        Assert.Equal("SYSTEM VERSIONED", await ReadTableTypeAsync(connection, TableName, cancellationToken));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName, cancellationToken));
        Assert.Equal(
            2,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                  AND (
                      (COLUMN_NAME = @periodStart AND UPPER(TRIM(GENERATION_EXPRESSION)) = 'ROW START')
                      OR (COLUMN_NAME = @periodEnd AND UPPER(TRIM(GENERATION_EXPRESSION)) = 'ROW END')
                  );
                """,
                cancellationToken,
                ("@tableName", TableName),
                ("@periodStart", PeriodStartColumnName),
                ("@periodEnd", PeriodEndColumnName)));
    }

    /// <summary>Verifies the removed contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertRemovedAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal(0, await CountTablesAsync(connection, TableName, cancellationToken));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName, cancellationToken));
        Assert.Equal(
            0,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.TRIGGERS
                WHERE TRIGGER_SCHEMA = DATABASE()
                  AND EVENT_OBJECT_TABLE = @tableName;
                """,
                cancellationToken,
                ("@tableName", TableName)));
    }

    /// <summary>Verifies the non temporal contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertNonTemporalContractAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection, TableName, cancellationToken));
        Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, TableName, cancellationToken));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName, cancellationToken));
        Assert.Equal(
            0,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                  AND COLUMN_NAME IN (@periodStart, @periodEnd);
                """,
                cancellationToken,
                ("@tableName", TableName),
                ("@periodStart", PeriodStartColumnName),
                ("@periodEnd", PeriodEndColumnName)));

        Assert.Equal(
            0,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.TRIGGERS
                WHERE TRIGGER_SCHEMA = DATABASE()
                  AND EVENT_OBJECT_TABLE = @tableName;
                """,
                cancellationToken,
                ("@tableName", TableName)));
    }

    /// <summary>Executes the operations with the caller's cancellation token.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task ExecuteOperationsAsync(
        DbContext context,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        foreach (var migrationCommand in generator.Generate(operations, model))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command.ExecuteNonQueryAsync(cancellationToken: cancellationToken);
        }
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    )
    {
        var sourceModel = source
            .GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        var targetModel = target
            .GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        return target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(sourceModel, targetModel);
    }

    private static TemporalContext CreateTemporalContext(
        IntegrationDatabaseTarget target,
        params IInterceptor[] interceptors
    )
    {
        Assert.Equal(
            TestDatabaseName,
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(target)).Database);

        var optionsBuilder = IntegrationTestDbContextOptions
            .Create<TemporalContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetBaseConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target));

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new TemporalContext(optionsBuilder.Options);
    }

    private static NonTemporalContext CreateNonTemporalContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions
            .Create<NonTemporalContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetBaseConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new NonTemporalContext(options);
    }

    private static EmptyTemporalContext CreateEmptyContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions
            .Create<EmptyTemporalContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetBaseConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new EmptyTemporalContext(options);
    }

    /// <summary>Counts the tables in the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static Task<int> CountTablesAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        return CountAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName;
            """,
            cancellationToken,
            ("@tableName", tableName));
    }

    /// <summary>Reads the session state from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<TemporalSessionState> ReadSessionStateAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var sqlMode = Convert.ToString(
                await ExecuteScalarAsync(connection, "SELECT @@SESSION.sql_mode;", cancellationToken),
                CultureInfo.InvariantCulture)
            ?? "";

        var alterHistoryMode = IsMySql(target)
            ? null
            : Convert.ToString(
                await ExecuteScalarAsync(
                    connection,
                    "SELECT @@SESSION.system_versioning_alter_history;",
                    cancellationToken),
                CultureInfo.InvariantCulture);

        return new TemporalSessionState(sqlMode, alterHistoryMode);
    }

    private static bool IsMySql(
        IntegrationDatabaseTarget target
    ) => !IntegrationTestEnvironment.GetServerVersion(target)
        .IsMariaDb;

    /// <summary>Counts the columns in the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static Task<int> CountColumnsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        return CountAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName;
            """,
            cancellationToken,
            ("@tableName", tableName));
    }

    /// <summary>Reads a count using a parameterized SQL command.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<int> CountAsync(
        MySqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        return Convert.ToInt32(
            await ExecuteScalarAsync(connection, commandText, cancellationToken, parameters),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the table type from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<string> ReadTableTypeAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        return Assert.IsType<string>(
            await ExecuteScalarAsync(
                connection,
                """
                SELECT TABLE_TYPE
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName;
                """,
                cancellationToken,
                ("@tableName", tableName)));
    }

    /// <summary>Reads the table engine from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<string> ReadTableEngineAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        return Assert.IsType<string>(
            await ExecuteScalarAsync(
                connection,
                """
                SELECT ENGINE
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName;
                """,
                cancellationToken,
                ("@tableName", tableName)));
    }

    /// <summary>Executes the scalar with the caller's cancellation token.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<object?> ExecuteScalarAsync(
        MySqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Removes integration tables using the caller's cancellation policy.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task CleanupAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var sourceCommand = connection.CreateCommand();
        sourceCommand.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        _ = await sourceCommand.ExecuteNonQueryAsync(cancellationToken: cancellationToken);

        await using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = $"DROP TABLE IF EXISTS `{HistoryTableName}`;";
        _ = await historyCommand.ExecuteNonQueryAsync(cancellationToken: cancellationToken);
    }

    private static DateTime AsUtc(
        DateTime value
    ) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime GetMidpoint(
        DateTime from,
        DateTime to
    ) => new(from.Ticks + (to.Ticks - from.Ticks) / 2, DateTimeKind.Utc);

    private static void ConfigureTemporalItem(
        ModelBuilder modelBuilder,
        bool temporal
    )
    {
        modelBuilder.Entity<TemporalItem>(entity =>
        {
            if (temporal)
            {
                entity.ToTable(
                    TableName,
                    table => table.IsTemporal(temporalTable =>
                    {
                        temporalTable.UseHistoryTable(HistoryTableName, TestDatabaseName);
                        temporalTable.HasPeriodStart(PeriodStartColumnName);
                        temporalTable.HasPeriodEnd(PeriodEndColumnName);
                    }));
            }
            else
            {
                entity.ToTable(TableName);
            }

            entity.HasKey(item => item.Id);
            entity
                .Property(item => item.Name)
                .HasMaxLength(64)
                .IsConcurrencyToken();
        });
    }

    private sealed class TemporalItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed record TemporalSessionState(
        string SqlMode,
        string? SystemVersioningAlterHistory
    );

    private sealed class TemporalContext : DbContext
    {
        public TemporalContext(
            DbContextOptions<TemporalContext> options
        ) : base(options) { }

        public DbSet<TemporalItem> Items => Set<TemporalItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalItem(modelBuilder, temporal: true);
    }

    private sealed class NonTemporalContext : DbContext
    {
        public NonTemporalContext(
            DbContextOptions<NonTemporalContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureTemporalItem(modelBuilder, temporal: false);
    }

    private sealed class EmptyTemporalContext : DbContext
    {
        public EmptyTemporalContext(
            DbContextOptions<EmptyTemporalContext> options
        ) : base(options) { }
    }
}
