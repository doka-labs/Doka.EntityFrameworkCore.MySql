namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlScopedMigrationOperationIntegrationTests
{
    private static IEnumerable<IntegrationDatabaseTarget> SelectedTargets() =>
        IntegrationTestEnvironment.GetSupportedTargets().Where(IntegrationTestEnvironment.IsTargetSelected);

    /// <summary>Builds the single scoped command required by the runtime fixture.</summary>
    private static GeneratedCommand GenerateCommand(
        ScopedHandlerContext context,
        ScopedScenario scenario,
        bool transactionSuppressed
    )
    {
        var operation = new ScopedHandlerOperation(
            scenario,
            Guid
                .NewGuid()
                .ToString("N", CultureInfo.InvariantCulture),
            transactionSuppressed);

        // WHY: Runtime cleanup probes are valid only when the extension produced
        // exactly one scoped command; fail the fixture before executing another shape.
        var command = Assert.Single(
            context
                .GetService<IMigrationsSqlGenerator>()
                .Generate([operation], context.Model));

        Assert.IsType<MySqlScopedMigrationCommand>(command);

        return new GeneratedCommand(command, operation);
    }

    /// <summary>Captures async cleanup evidence while the physical session is still open.</summary>
    private static async Task<SessionObservation> ObserveSessionAsync(
        MySqlConnection connection,
        ScopedHandlerOperation operation
    )
    {
        // WHY: Preserve the explicit connection-state assertion instead of
        // replacing it with an incidental "connection is closed" query failure.
        if (connection.State != ConnectionState.Open)
        {
            return new SessionObservation(0, null);
        }

        await using var variable = connection.CreateCommand();
        variable.CommandText = $"SELECT {operation.VariableName} IS NULL;";
        var clearedVariable = Convert.ToInt64(
            await variable.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await using var table = connection.CreateCommand();
        table.CommandText = $"SELECT COUNT(*) FROM `{operation.TableName}`;";
        MySqlException? missingTableException = null;

        try
        {
            _ = await table.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (MySqlException exception)
        {
            missingTableException = exception;
        }

        return new SessionObservation(clearedVariable, missingTableException);
    }

    /// <summary>Captures cleanup evidence through the deliberately synchronous API path.</summary>
    private static SessionObservation ObserveSession(
        MySqlConnection connection,
        ScopedHandlerOperation operation
    )
    {
        if (connection.State != ConnectionState.Open)
        {
            return new SessionObservation(0, null);
        }

        using var variable = connection.CreateCommand();
        variable.CommandText = $"SELECT {operation.VariableName} IS NULL;";

        // WHY: This observation belongs to the synchronous executor contract.
        // ReSharper disable once MethodHasAsyncOverload
        var clearedVariable = Convert.ToInt64(variable.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var table = connection.CreateCommand();
        table.CommandText = $"SELECT COUNT(*) FROM `{operation.TableName}`;";
        MySqlException? missingTableException = null;

        try
        {
            // WHY: Keep the synchronous cleanup probe independent of async execution.
            // ReSharper disable once MethodHasAsyncOverload
            _ = table.ExecuteScalar();
        }
        catch (MySqlException exception)
        {
            missingTableException = exception;
        }

        return new SessionObservation(clearedVariable, missingTableException);
    }

    /// <summary>Asserts captured cleanup state without issuing any database operation.</summary>
    private static void AssertCleanSession(
        SessionObservation session
    )
    {
        Assert.Equal(1L, session.ClearedVariable);
        var exception = session.MissingTableException;

        Assert.NotNull(exception);
        Assert.Equal(1146, exception.Number);
    }

    private readonly record struct SessionObservation(
        long ClearedVariable,
        MySqlException? MissingTableException);

    /// <summary>Identifies the physical server session for quarantine and reuse assertions.</summary>
    private static async Task<long> ReadConnectionIdAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONNECTION_ID();";

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Executes fixture-owned setup or teardown SQL on an already-open session.</summary>
    private static async Task ExecuteNonQueryAsync(
        MySqlConnection connection,
        string commandText
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        _ = await command
            .ExecuteNonQueryAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>Creates an unopened fixture connection with the requested pooling policy.</summary>
    private static MySqlConnection CreateConnection(
        IntegrationDatabaseTarget target,
        bool pooling
    ) => new(CreateConnectionString(target, pooling));

    /// <summary>Retains one schema owner and an explicit one-slot pool for physical-session assertions.</summary>
    // WHY: All topic partials resolve connections here so CallerFilePath cannot
    // assign different schemas to setup, execution, observation, or teardown.
    private static string CreateConnectionString(
        IntegrationDatabaseTarget target,
        bool pooling,
        bool connectionReset = false
    ) => new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
    {
        AllowUserVariables = true,
        ConnectionReset = connectionReset,
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        MinimumPoolSize = pooling ? 1u : 0u,
        MaximumPoolSize = pooling ? 1u : 100u,
        Pooling = pooling,
    }.ConnectionString;

    /// <summary>Registers the scoped handler on a context using an externally owned connection.</summary>
    private static ScopedHandlerContext CreateContext(
        MySqlConnection connection,
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions
            .Create<ScopedHandlerContext>()
            .UseMySql(connection, IntegrationTestEnvironment.GetServerVersion(target));

        AddHandlerExtension(builder);

        return new ScopedHandlerContext(builder.Options);
    }

    /// <summary>Registers the scoped handler on a context that owns and returns its pooled connection.</summary>
    private static ScopedHandlerContext CreateContext(
        string connectionString,
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions
            .Create<ScopedHandlerContext>()
            .UseMySql(connectionString, IntegrationTestEnvironment.GetServerVersion(target));

        AddHandlerExtension(builder);

        return new ScopedHandlerContext(builder.Options);
    }

    private static void AddHandlerExtension(
        DbContextOptionsBuilder<ScopedHandlerContext> builder
    ) => ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ScopedHandlerOptionsExtension());

    private readonly record struct GeneratedCommand(
        MigrationCommand Command,
        ScopedHandlerOperation Operation
    );
}
