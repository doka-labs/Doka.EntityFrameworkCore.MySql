namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies MariaDB application-time and bitemporal contracts against supported live engines.
/// </summary>
[IntegrationDatabaseTest]
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
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb1011, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Bitemporal_contract_executes_on_mariadb114() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb114, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Bitemporal_contract_executes_on_mariadb118() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb118, CancellationToken.None);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task Bitemporal_contract_executes_on_mariadb123() =>
        RunBitemporalContractAsync(IntegrationDatabaseTarget.MariaDb123, CancellationToken.None);

    /// <summary>Runs the bitemporal contract integration scenario.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task RunBitemporalContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken: cancellationToken);
        await CleanupAsync(connection, cancellationToken);

        try
        {
            await using var emptyContext = CreateEmptyContext(target);
            await using var bitemporalContext = CreateBitemporalContext(target);

            await ExecuteOperationsAsync(
                bitemporalContext,
                connection,
                GetDifferences(emptyContext, bitemporalContext),
                cancellationToken);

            await AssertPhysicalContractAsync(connection, target, cancellationToken);
            AssertScaffoldingContract(connectionString);
            await AssertPortionMutationContractAsync(target, cancellationToken);

            await ExecuteOperationsAsync(
                emptyContext,
                connection,
                GetDifferences(bitemporalContext, emptyContext),
                cancellationToken);

            Assert.Equal(0, await CountTablesAsync(connection, cancellationToken));
        }
        finally
        {
            // The model-driven drop above is part of the contract. Raw SQL is
            // reserved for failure recovery so a failed assertion cannot leak
            // state into another live integration test.
            await CleanupAsync(connection, CancellationToken.None);
        }
    }

    /// <summary>Verifies the physical contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertPhysicalContractAsync(
        MySqlConnection connection,
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal(1, await CountTablesAsync(connection, cancellationToken));
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
                cancellationToken,
                ("@tableName", TableName)));

        if (!IntegrationTestEnvironment
            .GetServerVersion(target)
            .Profile
            .Engine
            .Has(EngineCapability.TemporalPeriodCatalog))
        {
            var createTableSql = await ReadCreateTableAsync(connection, cancellationToken);

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
                cancellationToken,
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
                cancellationToken,
                ("@tableName", TableName),
                ("@periodName", ApplicationPeriodName)));
    }

    private static void AssertScaffoldingContract(
        string connectionString
    )
    {
        // Arrange
        using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var databaseOptions = new DatabaseModelFactoryOptions([TableName], Array.Empty<string>());
        var databaseModel = scopedServices
            .GetRequiredService<IDatabaseModelFactory>()
            .Create(connectionString, databaseOptions);

        // Act
        var sourceTable = Assert.Single(databaseModel.Tables);

        // Assert
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

    /// <summary>Verifies the portion mutation contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertPortionMutationContractAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
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

            await seedContext.SaveChangesAsync(cancellationToken: cancellationToken);
        }

        await using (var updateContext = CreateBitemporalContext(target))
        {
            _ = await updateContext
                .Items
                .ForPortionOf(s_updateFrom, s_updateTo)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.Name, "updated"),
                    cancellationToken: cancellationToken);
        }

        await AssertRowsAsync(
            target,
            cancellationToken,
            new ExpectedRow("original", s_initialFrom, s_updateFrom),
            new ExpectedRow("updated", s_updateFrom, s_updateTo),
            new ExpectedRow("original", s_updateTo, s_initialTo));

        await using (var deleteContext = CreateBitemporalContext(target))
        {
            _ = await deleteContext
                .Items
                .ForPortionOf(s_deleteFrom, s_deleteTo)
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);
        }

        // MariaDB splits the affected row at both delete boundaries. Checking
        // all four surviving portions proves that the provider emitted native
        // FOR PORTION OF semantics instead of approximating them with a filter.
        await AssertRowsAsync(
            target,
            cancellationToken,
            new ExpectedRow("original", s_initialFrom, s_updateFrom),
            new ExpectedRow("updated", s_updateFrom, s_deleteFrom),
            new ExpectedRow("updated", s_deleteTo, s_updateTo),
            new ExpectedRow("original", s_updateTo, s_initialTo));
    }

    /// <summary>Verifies the rows contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertRowsAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken,
        params ExpectedRow[] expectedRows
    )
    {
        // Arrange
        await using var context = CreateBitemporalContext(target);

        // Act
        var actualRows = await context
            .Items
            .AsNoTracking()
            .OrderBy(item => item.BusinessValidFrom)
            .Select(item => new ExpectedRow(item.Name, item.BusinessValidFrom, item.BusinessValidTo))
            .ToListAsync(cancellationToken: cancellationToken);

        // Assert
        Assert.Equal(expectedRows, actualRows);
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

    private static BitemporalContext CreateBitemporalContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions
            .Create<BitemporalContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new BitemporalContext(options);
    }

    private static EmptyApplicationTimeContext CreateEmptyContext(
        IntegrationDatabaseTarget target
    )
    {
        var options = IntegrationTestDbContextOptions
            .Create<EmptyApplicationTimeContext>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .Options;

        return new EmptyApplicationTimeContext(options);
    }

    /// <summary>Counts the tables in the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static Task<int> CountTablesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    ) => CountAsync(
        connection,
        """
        SELECT COUNT(*)
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = @tableName;
        """,
        cancellationToken,
        ("@tableName", TableName));

    /// <summary>Reads the create table from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<string> ReadCreateTableAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SET STATEMENT sql_mode = '', sql_quote_show_create = 1 "
            + $"FOR SHOW CREATE TABLE `{TableName}`;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken: cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        return reader.GetString(1);
    }

    /// <summary>Reads a count using a parameterized SQL command.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<int> CountAsync(
        MySqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    ) =>
        Convert.ToInt32(
            await ExecuteScalarAsync(connection, commandText, cancellationToken, parameters),
            CultureInfo.InvariantCulture);

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

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
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
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{TableName}`;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken: cancellationToken);
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
