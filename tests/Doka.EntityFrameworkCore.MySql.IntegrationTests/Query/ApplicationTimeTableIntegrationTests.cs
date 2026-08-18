namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies MariaDB application-time and bitemporal contracts against supported live engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class ApplicationTimeTableIntegrationTests
{
    private const string TableName = "IntBitemporalItems";
    private const string ApplicationPeriodName = "BusinessValidity";
    private const string ApplicationPeriodStartColumnName = "BusinessValidFrom";
    private const string ApplicationPeriodEndColumnName = "BusinessValidTo";
    private const string SystemPeriodStartColumnName = "SystemValidFrom";
    private const string SystemPeriodEndColumnName = "SystemValidTo";

    private static readonly DateTime s_initialFrom = new(2026, 1, 1);
    private static readonly DateTime s_updateFrom = new(2026, 4, 1);
    private static readonly DateTime s_deleteFrom = new(2026, 5, 1);
    private static readonly DateTime s_deleteTo = new(2026, 6, 1);
    private static readonly DateTime s_updateTo = new(2026, 7, 1);
    private static readonly DateTime s_initialTo = new(2027, 1, 1);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task Bitemporal_contract_executes_on_mariadb1011() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Bitemporal_contract_executes_on_mariadb114() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Bitemporal_contract_executes_on_mariadb118() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Bitemporal_contract_executes_on_mariadb123() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task RunBitemporalContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await using var emptyContext = CreateEmptyContext(target);
            await using var bitemporalContext = CreateBitemporalContext(target);

            await ExecuteOperationsAsync(
                bitemporalContext,
                connection,
                GetDifferences(emptyContext, bitemporalContext));
            await AssertPhysicalContractAsync(connection, target);
            AssertScaffoldingContract(connectionString);
            await AssertPortionMutationContractAsync(target);

            await ExecuteOperationsAsync(emptyContext, connection, GetDifferences(bitemporalContext, emptyContext));

            Assert.Equal(0, await CountTablesAsync(connection));
        }
        finally
        {
            // The model-driven drop above is part of the contract. Raw SQL is
            // reserved for failure recovery so a failed assertion cannot leak
            // state into another live integration test.
            await CleanupAsync(connection);
        }
    }

    private static async Task AssertPhysicalContractAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection));
        Assert.Equal(
            "SYSTEM VERSIONED",
            await ExecuteScalarAsync(
                connection,
                """
                SELECT TABLE_TYPE
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName;
                """,
                ("@tableName", TableName)));
        if (!IntegrationTestEnvironment
                .GetServerVersion(target)
                .Profile.Engine.Has(EngineCapability.TemporalPeriodCatalog))
        {
            var createTableSql = await ReadCreateTableAsync(connection);

            Assert.Contains(
                $"PERIOD FOR `{ApplicationPeriodName}` "
                + $"(`{ApplicationPeriodStartColumnName}`, `{ApplicationPeriodEndColumnName}`)",
                createTableSql,
                StringComparison.Ordinal);
            Assert.Contains(
                $"`{ApplicationPeriodName}` WITHOUT OVERLAPS",
                createTableSql,
                StringComparison.Ordinal);
            return;
        }

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.PERIODS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                  AND PERIOD = @periodName
                  AND START_COLUMN_NAME = @periodStart
                  AND END_COLUMN_NAME = @periodEnd;
                """,
                ("@tableName", TableName),
                ("@periodName", ApplicationPeriodName),
                ("@periodStart", ApplicationPeriodStartColumnName),
                ("@periodEnd", ApplicationPeriodEndColumnName)));
        Assert.Equal(
            1,
            await CountAsync(
                connection,
                """
                SELECT COUNT(*)
                FROM information_schema.KEY_PERIOD_USAGE
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                  AND CONSTRAINT_NAME = 'PRIMARY'
                  AND PERIOD_NAME = @periodName;
                """,
                ("@tableName", TableName),
                ("@periodName", ApplicationPeriodName)));
    }

    private static void AssertScaffoldingContract(
        string connectionString
    )
    {
        using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions([TableName], Array.Empty<string>());
        var databaseModel = scopedServices
            .GetRequiredService<IDatabaseModelFactory>()
            .Create(connectionString, databaseOptions);

        var sourceTable = Assert.Single(databaseModel.Tables);

        Assert.True(
            sourceTable.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value as bool?);
        Assert.True(
            sourceTable.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)
                ?.Value as bool?);
        Assert.Equal(
            ApplicationPeriodName,
            sourceTable.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodName)
                ?.Value);
        Assert.Equal(
            ApplicationPeriodStartColumnName,
            sourceTable.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodStartColumn)
                ?.Value);
        Assert.Equal(
            ApplicationPeriodEndColumnName,
            sourceTable.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodEndColumn)
                ?.Value);
        Assert.True(
            sourceTable.PrimaryKey?.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                ?.Value as bool?);

        var scaffoldedModel = scopedServices
            .GetRequiredService<IReverseEngineerScaffolder>()
            .ScaffoldModel(
                connectionString,
                databaseOptions,
                new ModelReverseEngineerOptions(),
                ScaffoldingTestServices.CreateCodeGenerationOptions(
                    connectionString,
                    contextName: "BitemporalSchemaContext"));

        var contextCode = scaffoldedModel.ContextFile.Code;

        Assert.Contains("tableBuilder.IsTemporal(temporalTableBuilder =>", contextCode);
        Assert.Contains("tableBuilder.HasApplicationTimePeriod(applicationTimeTableBuilder =>", contextCode);
        Assert.Contains($"applicationTimeTableBuilder.HasPeriodName(\"{ApplicationPeriodName}\")", contextCode);
        Assert.Contains(
            $".HasPeriodStart(\"{ApplicationPeriodStartColumnName}\")",
            contextCode);
        Assert.Contains($".HasPeriodEnd(\"{ApplicationPeriodEndColumnName}\")", contextCode);
        Assert.Contains($".HasColumnName(\"{ApplicationPeriodStartColumnName}\")", contextCode);
        Assert.Contains($".HasColumnName(\"{ApplicationPeriodEndColumnName}\")", contextCode);
        Assert.Contains(".UseWithoutOverlaps()", contextCode);
    }

    private static async Task AssertPortionMutationContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using (var seedContext = CreateBitemporalContext(target))
        {
            seedContext.Items.Add(
                new BitemporalItem
                {
                    Id = 1,
                    Name = "original",
                    BusinessValidFrom = s_initialFrom,
                    BusinessValidTo = s_initialTo,
                });
            await seedContext.SaveChangesAsync();
        }

        await using (var updateContext = CreateBitemporalContext(target))
        {
            _ = await updateContext
                .Items.ForPortionOf(s_updateFrom, s_updateTo)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Name, "updated"));
        }

        await AssertRowsAsync(
            target,
            new ExpectedRow("original", s_initialFrom, s_updateFrom),
            new ExpectedRow("updated", s_updateFrom, s_updateTo),
            new ExpectedRow("original", s_updateTo, s_initialTo));

        await using (var deleteContext = CreateBitemporalContext(target))
        {
            _ = await deleteContext
                .Items.ForPortionOf(s_deleteFrom, s_deleteTo)
                .ExecuteDeleteAsync();
        }

        // MariaDB splits the affected row at both delete boundaries. Checking
        // all four surviving portions proves that the provider emitted native
        // FOR PORTION OF semantics instead of approximating them with a filter.
        await AssertRowsAsync(
            target,
            new ExpectedRow("original", s_initialFrom, s_updateFrom),
            new ExpectedRow("updated", s_updateFrom, s_deleteFrom),
            new ExpectedRow("updated", s_deleteTo, s_updateTo),
            new ExpectedRow("original", s_updateTo, s_initialTo));
    }

    private static async Task AssertRowsAsync(
        IntegrationDatabaseTarget target,
        params ExpectedRow[] expectedRows
    )
    {
        await using var context = CreateBitemporalContext(target);
        var actualRows = await context
            .Items.AsNoTracking()
            .OrderBy(item => item.BusinessValidFrom)
            .Select(item => new ExpectedRow(item.Name, item.BusinessValidFrom, item.BusinessValidTo))
            .ToListAsync();

        Assert.Equal(expectedRows, actualRows);
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

    private static BitemporalContext CreateBitemporalContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions.Create<BitemporalContext>().UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new BitemporalContext(options);
    }

    private static EmptyApplicationTimeContext CreateEmptyContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions.Create<EmptyApplicationTimeContext>().UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new EmptyApplicationTimeContext(options);
    }

    private static Task<int> CountTablesAsync(
        MySqlConnection connection
    ) => CountAsync(
        connection,
        """
        SELECT COUNT(*)
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @tableName;
        """,
        ("@tableName", TableName));

    private static async Task<string> ReadCreateTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SET STATEMENT sql_mode = '', sql_quote_show_create = 1 "
            + $"FOR SHOW CREATE TABLE `{TableName}`;";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return reader.GetString(1);
    }

    private static async Task<int> CountAsync(
        MySqlConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters
    ) => Convert.ToInt32(await ExecuteScalarAsync(connection, commandText, parameters), CultureInfo.InvariantCulture);

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
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        _ = await command.ExecuteNonQueryAsync();
    }

    private sealed record ExpectedRow(
        string Name,
        DateTime From,
        DateTime To
    );

    private sealed class BitemporalItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public DateTime BusinessValidFrom { get; set; }

        public DateTime BusinessValidTo { get; set; }
    }

    private sealed class BitemporalContext : DbContext
    {
        public BitemporalContext(
            DbContextOptions<BitemporalContext> options
        ) : base(options) { }

        public DbSet<BitemporalItem> Items => Set<BitemporalItem>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<BitemporalItem>(entity =>
        {
            entity.ToTable(
                TableName,
                table => table.IsBitemporal(
                    systemTime =>
                    {
                        systemTime.HasPeriodStart(SystemPeriodStartColumnName);
                        systemTime.HasPeriodEnd(SystemPeriodEndColumnName);
                    },
                    applicationTime =>
                    {
                        applicationTime.HasPeriodName(ApplicationPeriodName);
                        applicationTime
                            .HasPeriodStart(item => item.BusinessValidFrom)
                            .HasColumnName(ApplicationPeriodStartColumnName);
                        applicationTime
                            .HasPeriodEnd(item => item.BusinessValidTo)
                            .HasColumnName(ApplicationPeriodEndColumnName);
                    }));
            entity
                .HasKey(item => item.Id)
                .UseWithoutOverlaps();
            entity
                .Property(item => item.Name)
                .HasMaxLength(64);
        });
    }

    private sealed class EmptyApplicationTimeContext : DbContext
    {
        public EmptyApplicationTimeContext(
            DbContextOptions<EmptyApplicationTimeContext> options
        ) : base(options) { }
    }
}
