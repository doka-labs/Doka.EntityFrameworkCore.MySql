namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the temporal-table contract against every supported live engine.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class TemporalTableIntegrationTests
{
    private const string TableName = "IntTemporalItems";
    private const string HistoryTableName = "IntTemporalItemsHistory";
    private const string PeriodStartColumnName = "ValidFrom";
    private const string PeriodEndColumnName = "ValidTo";
    private const string TestDatabaseName = "doka_provider";

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Temporal_contract_executes_on_mysql84() => RunTemporalContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task Temporal_contract_executes_on_mysql97() => RunTemporalContractAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Temporal_contract_executes_on_mariadb1011() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Temporal_contract_executes_on_mariadb114() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Temporal_contract_executes_on_mariadb118() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Temporal_contract_executes_on_mariadb123() =>
        RunTemporalContractAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunTemporalContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await CleanupAsync(connection);
        var initialSessionState = await ReadSessionStateAsync(connection, target);

        try
        {
            await RunInitialCreateAndDropAsync(connection, target);
            await RunTransitionLifecycleAsync(connection, target);
            await RunRollbackLifecycleAsync(connection, target);
            await RunConcurrencyLifecycleAsync(connection, target);

            // Native MariaDB transitions require a temporary session variable.
            // The provider scopes it to each DDL statement so caller-owned
            // session state remains unchanged after the migration lifecycle.
            Assert.Equal(initialSessionState, await ReadSessionStateAsync(connection, target));
        }
        finally
        {
            // This cleanup is only failure recovery. All contract DDL above is
            // generated from the EF model by the provider under test.
            await CleanupAsync(connection);
        }
    }

    private static async Task RunInitialCreateAndDropAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(temporalContext, connection, GetDifferences(emptyContext, temporalContext));
        await AssertPhysicalContractAsync(connection, target);
        AssertScaffoldingContract(connection.ConnectionString, target);
        await WriteVersionsAsync(temporalContext);
        await AssertTemporalQueriesAsync(target);

        await ExecuteOperationsAsync(emptyContext, connection, GetDifferences(temporalContext, emptyContext));
        await AssertRemovedAsync(connection);
    }

    private static async Task RunRollbackLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(temporalContext, connection, GetDifferences(emptyContext, temporalContext));

        await using (var transaction = await temporalContext.Database.BeginTransactionAsync())
        {
            var item = new TemporalItem { Name = "rolled-back-version-1" };

            temporalContext.Items.Add(item);
            await temporalContext.SaveChangesAsync();

            item.Name = "rolled-back-version-2";
            await temporalContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var verificationContext = CreateTemporalContext(target))
        {
            Assert.Empty(await verificationContext.Items.ToListAsync());
            Assert.Empty(
                await verificationContext
                    .Items.TemporalAll()
                    .ToListAsync());
        }

        await ExecuteOperationsAsync(emptyContext, connection, GetDifferences(temporalContext, emptyContext));
        await AssertRemovedAsync(connection);
    }

    private static async Task RunConcurrencyLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(temporalContext, connection, GetDifferences(emptyContext, temporalContext));

        temporalContext.Items.Add(new TemporalItem { Name = "original" });
        await temporalContext.SaveChangesAsync();

        await using (var winnerContext = CreateTemporalContext(target))
        await using (var staleContext = CreateTemporalContext(target))
        {
            var winner = await winnerContext.Items.SingleAsync();
            var stale = await staleContext.Items.SingleAsync();

            await Task.Delay(TimeSpan.FromMilliseconds(25));

            winner.Name = "winner";
            await winnerContext.SaveChangesAsync();

            stale.Name = "stale";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
        }

        await using (var verificationContext = CreateTemporalContext(target))
        {
            Assert.Equal(
                ["winner"],
                await verificationContext
                    .Items.Select(item => item.Name)
                    .ToListAsync());
            Assert.Equal(["original", "winner"], await ReadNamesAsync(verificationContext.Items.TemporalAll()));
        }

        await ExecuteOperationsAsync(emptyContext, connection, GetDifferences(temporalContext, emptyContext));
        await AssertRemovedAsync(connection);
    }

    private static async Task RunTransitionLifecycleAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        await using var emptyContext = CreateEmptyContext(target);
        await using var nonTemporalContext = CreateNonTemporalContext(target);
        await using var temporalContext = CreateTemporalContext(target);

        await ExecuteOperationsAsync(nonTemporalContext, connection, GetDifferences(emptyContext, nonTemporalContext));
        await AssertNonTemporalContractAsync(connection);

        await ExecuteOperationsAsync(temporalContext, connection, GetDifferences(nonTemporalContext, temporalContext));
        await AssertPhysicalContractAsync(connection, target);
        await WriteVersionsAsync(temporalContext);
        await AssertTemporalQueriesAsync(target);

        await ExecuteOperationsAsync(
            nonTemporalContext,
            connection,
            GetDifferences(temporalContext, nonTemporalContext));
        await AssertNonTemporalContractAsync(connection);

        await ExecuteOperationsAsync(emptyContext, connection, GetDifferences(nonTemporalContext, emptyContext));
        await AssertRemovedAsync(connection);
    }

    private static async Task WriteVersionsAsync(
        TemporalContext context
    )
    {
        var item = new TemporalItem { Name = "version-1" };

        context.Items.Add(item);
        await context.SaveChangesAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(25));

        item.Name = "version-2";
        await context.SaveChangesAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(25));

        context.Items.Remove(item);
        await context.SaveChangesAsync();
    }

    private static async Task AssertTemporalQueriesAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = CreateTemporalContext(target);
        var versions = await context
            .Items.TemporalAll()
            .Select(item => new
            {
                item.Name,
                ValidFrom = EF.Property<DateTime>(item, PeriodStartColumnName),
                ValidTo = EF.Property<DateTime>(item, PeriodEndColumnName),
            })
            .OrderBy(version => version.ValidFrom)
            .ToListAsync();

        Assert.Collection(
            versions,
            version => Assert.Equal("version-1", version.Name),
            version => Assert.Equal("version-2", version.Name));

        var firstFrom = AsUtc(versions[0].ValidFrom);
        var firstTo = AsUtc(versions[0].ValidTo);
        var secondFrom = AsUtc(versions[1].ValidFrom);
        var secondTo = AsUtc(versions[1].ValidTo);

        Assert.True(firstFrom < firstTo);
        Assert.Equal(firstTo, secondFrom);
        Assert.True(secondFrom < secondTo);

        Assert.Equal(["version-1"], await ReadNamesAsync(context.Items.TemporalAsOf(firstFrom)));
        Assert.Equal(["version-1"], await ReadNamesAsync(context.Items.TemporalAsOf(GetMidpoint(firstFrom, firstTo))));
        Assert.Equal(["version-2"], await ReadNamesAsync(context.Items.TemporalAsOf(firstTo)));
        Assert.Equal(
            ["version-2"],
            await ReadNamesAsync(context.Items.TemporalAsOf(GetMidpoint(secondFrom, secondTo))));
        Assert.Empty(await ReadNamesAsync(context.Items.TemporalAsOf(secondTo)));

        var expectedVersions = new[]
        {
            "version-1",
            "version-2",
        };

        Assert.Empty(await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, firstFrom)));
        Assert.Equal(["version-1"], await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, firstTo)));
        Assert.Equal(["version-2"], await ReadNamesAsync(context.Items.TemporalFromTo(firstTo, secondTo)));
        Assert.Equal(expectedVersions, await ReadNamesAsync(context.Items.TemporalFromTo(firstFrom, secondTo)));
        Assert.Equal(["version-1"], await ReadNamesAsync(context.Items.TemporalBetween(firstFrom, firstFrom)));
        Assert.Equal(["version-2"], await ReadNamesAsync(context.Items.TemporalBetween(firstTo, secondTo)));
        Assert.Equal(expectedVersions, await ReadNamesAsync(context.Items.TemporalBetween(firstFrom, secondTo)));
        Assert.Equal(["version-1"], await ReadNamesAsync(context.Items.TemporalContainedIn(firstFrom, firstTo)));
        Assert.Equal(["version-2"], await ReadNamesAsync(context.Items.TemporalContainedIn(secondFrom, secondTo)));
        Assert.Equal(expectedVersions, await ReadNamesAsync(context.Items.TemporalContainedIn(firstFrom, secondTo)));
        Assert.Equal(expectedVersions, await ReadNamesAsync(context.Items.TemporalAll()));
        Assert.Equal(
            2,
            await context
                .Items.TemporalAll()
                .CountAsync());
        Assert.True(
            await context
                .Items.TemporalAll()
                .AnyAsync(item => item.Name == "version-2"));
        Assert.Empty(await context.Items.ToListAsync());
        Assert.Empty(context.ChangeTracker.Entries());

        await AssertCancellationContractAsync(target);
    }

    private static async Task AssertCancellationContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var interceptor = new CommandCancellationProbeInterceptor();

        await using var context = CreateTemporalContext(target, interceptor);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context
            .Items.TemporalAll()
            .CountAsync(cancellationToken));

        Assert.Equal(cancellationToken, exception.CancellationToken);
        Assert.Equal(cancellationToken, interceptor.ReceivedCancellationToken);
        Assert.Equal(1, interceptor.InvocationCount);
    }

    private static void AssertScaffoldingContract(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions(
            [
                TableName,
                HistoryTableName
            ],
            Array.Empty<string>());

        var databaseModel = scopedServices
            .GetRequiredService<IDatabaseModelFactory>()
            .Create(connectionString, databaseOptions);

        var sourceTable = Assert.Single(databaseModel.Tables);

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

    private static Task<List<string>> ReadNamesAsync(
        IQueryable<TemporalItem> query
    )
    {
        return query
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync();
    }

    private static async Task AssertPhysicalContractAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection, TableName));
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
                ("@tableName", TableName),
                ("@periodStart", PeriodStartColumnName),
                ("@periodEnd", PeriodEndColumnName),
                ("@dataType", IsMySql(target) ? "datetime" : "timestamp")));

        if (IsMySql(target))
        {
            Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, TableName));
            Assert.Equal("InnoDB", await ReadTableEngineAsync(connection, TableName));
            Assert.Equal(1, await CountTablesAsync(connection, HistoryTableName));
            Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, HistoryTableName));
            Assert.Equal("InnoDB", await ReadTableEngineAsync(connection, HistoryTableName));
            Assert.Equal(
                await CountColumnsAsync(connection, TableName),
                await CountColumnsAsync(connection, HistoryTableName));
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
                    ("@tableName", TableName)));
            return;
        }

        Assert.Equal("SYSTEM VERSIONED", await ReadTableTypeAsync(connection, TableName));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName));
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
                ("@tableName", TableName),
                ("@periodStart", PeriodStartColumnName),
                ("@periodEnd", PeriodEndColumnName)));
    }

    private static async Task AssertRemovedAsync(
        MySqlConnection connection
    )
    {
        Assert.Equal(0, await CountTablesAsync(connection, TableName));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName));
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
                ("@tableName", TableName)));
    }

    private static async Task AssertNonTemporalContractAsync(
        MySqlConnection connection
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection, TableName));
        Assert.Equal("BASE TABLE", await ReadTableTypeAsync(connection, TableName));
        Assert.Equal(0, await CountTablesAsync(connection, HistoryTableName));
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
                ("@tableName", TableName)));
    }

    private static async Task ExecuteOperationsAsync(
        DbContext context,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        foreach (var migrationCommand in generator.Generate(operations, model))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command.ExecuteNonQueryAsync();
        }
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    )
    {
        var sourceModel = source
            .GetService<IDesignTimeModel>()
            .Model.GetRelationalModel();

        var targetModel = target
            .GetService<IDesignTimeModel>()
            .Model.GetRelationalModel();

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
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target)).Database);

        var optionsBuilder = IntegrationTestDbContextOptions.Create<TemporalContext>().UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
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
        var options = IntegrationTestDbContextOptions.Create<NonTemporalContext>().UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new NonTemporalContext(options);
    }

    private static EmptyTemporalContext CreateEmptyContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions.Create<EmptyTemporalContext>().UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new EmptyTemporalContext(options);
    }

    private static Task<int> CountTablesAsync(
        MySqlConnection connection,
        string tableName
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
            ("@tableName", tableName));
    }

    private static async Task<TemporalSessionState> ReadSessionStateAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        var sqlMode = Convert.ToString(
                await ExecuteScalarAsync(connection, "SELECT @@SESSION.sql_mode;"),
                CultureInfo.InvariantCulture)
            ?? "";

        var alterHistoryMode = IsMySql(target)
            ? null
            : Convert.ToString(
                await ExecuteScalarAsync(connection, "SELECT @@SESSION.system_versioning_alter_history;"),
                CultureInfo.InvariantCulture);

        return new TemporalSessionState(sqlMode, alterHistoryMode);
    }

    private static bool IsMySql(
        IntegrationDatabaseTarget target
    ) => !IntegrationTestEnvironment.GetServerVersion(target).IsMariaDb;

    private static Task<int> CountColumnsAsync(
        MySqlConnection connection,
        string tableName
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
            ("@tableName", tableName));
    }

    private static async Task<int> CountAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters
    )
    {
        return Convert.ToInt32(
            await ExecuteScalarAsync(connection, commandText, parameters),
            CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadTableTypeAsync(
        MySqlConnection connection,
        string tableName
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
                [("@tableName", tableName)]));
    }

    private static async Task<string> ReadTableEngineAsync(
        MySqlConnection connection,
        string tableName
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
                [("@tableName", tableName)]));
    }

    private static async Task<object?> ExecuteScalarAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return await command.ExecuteScalarAsync();
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var sourceCommand = connection.CreateCommand();
        sourceCommand.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        _ = await sourceCommand.ExecuteNonQueryAsync();

        await using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = $"DROP TABLE IF EXISTS `{HistoryTableName}`;";
        _ = await historyCommand.ExecuteNonQueryAsync();
    }

    private static DateTime AsUtc(
        DateTime value
    ) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime GetMidpoint(
        DateTime from,
        DateTime to
    ) => new(from.Ticks + ((to.Ticks - from.Ticks) / 2), DateTimeKind.Utc);

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
}
