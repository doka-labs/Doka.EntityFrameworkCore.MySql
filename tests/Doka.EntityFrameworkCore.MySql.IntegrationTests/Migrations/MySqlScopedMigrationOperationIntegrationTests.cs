namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Qualifies handler-authored scoped migration commands against every
/// supported MySQL-family server without depending on an external package.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlScopedMigrationOperationIntegrationTests
{
    private static readonly ScopedScenario[] s_executionScenarios =
        [ScopedScenario.Success, ScopedScenario.BodyFailure];
    private static readonly ScopedScenario[] s_setupScenarios =
        [ScopedScenario.FirstSetupFailure, ScopedScenario.LaterSetupFailure];

    private static readonly bool[] s_connectionResetModes = [false, true];

    /// <summary>Identifies each selected supported engine.</summary>
    public static IEnumerable<object[]> TargetCases =>
        SelectedTargets().Select(target => new object[] { target.ToString() });

    /// <summary>Identifies independent successful and failed command executions.</summary>
    public static IEnumerable<object[]> ExecutionCases =>
        from target in SelectedTargets()
        from scenario in s_executionScenarios
        select new object[] { target.ToString(), scenario.ToString() };

    /// <summary>Identifies the first and later setup failure boundaries independently.</summary>
    public static IEnumerable<object[]> SetupFailureCases =>
        from target in SelectedTargets()
        from scenario in s_setupScenarios
        select new object[] { target.ToString(), scenario.ToString() };

    /// <summary>Identifies pooled-session reuse with and without connector reset.</summary>
    public static IEnumerable<object[]> PoolReuseCases =>
        from target in SelectedTargets()
        from reset in s_connectionResetModes
        select new object[] { target.ToString(), reset };

    /// <summary>Verifies async command result/error and complete session cleanup.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ExecutionCases))]
    public async Task Asynchronous_scoped_command_restores_session(
        string target,
        string scenario
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var execution = Enum.Parse<ScopedScenario>(scenario);
        var bodyFailure = execution == ScopedScenario.BodyFailure;
        await using var connection = CreateConnection(engine, pooling: false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, execution, transactionSuppressed: bodyFailure);

        // Act
        int? result = null;
        MySqlException? exception = null;

        if (bodyFailure)
        {
            exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));
        }
        else
        {
            result = await generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        var state = connection.State;
        var session = await ObserveSessionAsync(connection, generated.Operation).ConfigureAwait(false);

        // Assert
        Assert.Equal(bodyFailure, generated.Command.TransactionSuppressed);
        Assert.Equal(ConnectionState.Open, state);
        AssertCleanSession(session);

        if (bodyFailure)
        {
            Assert.Equal(1062, exception!.Number);
        }
        else
        {
            Assert.Equal(1, result);
        }
    }

    /// <summary>Verifies the synchronous command executor separately from its async counterpart.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ExecutionCases))]
    public void Synchronous_scoped_command_restores_session(
        string target,
        string scenario
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var execution = Enum.Parse<ScopedScenario>(scenario);
        var bodyFailure = execution == ScopedScenario.BodyFailure;
        using var connection = CreateConnection(engine, pooling: false);

        // WHY: This case qualifies the synchronous connection and command path.
        // ReSharper disable once MethodHasAsyncOverload
        connection.Open();
        using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, execution, transactionSuppressed: false);

        // Act
        int? result = null;
        MySqlException? exception = null;

        if (bodyFailure)
        {
            // WHY: This row qualifies the synchronous exception/cleanup path.
            // ReSharper disable once MethodHasAsyncOverload
            exception = Assert.Throws<MySqlException>(() =>
                generated.Command.ExecuteNonQuery(context.GetService<IRelationalConnection>()));
        }
        else
        {
            // WHY: This case qualifies the synchronous executor explicitly.
            // ReSharper disable once MethodHasAsyncOverload
            result = generated.Command.ExecuteNonQuery(context.GetService<IRelationalConnection>());
        }

        var state = connection.State;
        var session = ObserveSession(connection, generated.Operation);

        // Assert
        Assert.Equal(ConnectionState.Open, state);
        AssertCleanSession(session);

        if (bodyFailure)
        {
            Assert.Equal(1062, exception!.Number);
        }
        else
        {
            Assert.Equal(1, result);
        }
    }

    /// <summary>Verifies session cleanup after one setup failure boundary.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SetupFailureCases))]
    public async Task Failed_setup_still_cleans_session(
        string target,
        string scenario
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var connection = CreateConnection(engine, pooling: false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, Enum.Parse<ScopedScenario>(scenario), transactionSuppressed: false);

        // Act
        var exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
            context.GetService<IRelationalConnection>(),
            cancellationToken: CancellationToken.None));
        var state = connection.State;
        var session = await ObserveSessionAsync(connection, generated.Operation).ConfigureAwait(false);

        // Assert
        Assert.Equal(1644, exception.Number);
        Assert.Equal(ConnectionState.Open, state);
        AssertCleanSession(session);
    }

    /// <summary>Verifies a failed prepared body releases its prepared statement and session state.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Prepared_body_failure_deallocates_statement(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var connection = CreateConnection(engine, pooling: false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, ScopedScenario.PreparedBodyFailure, transactionSuppressed: true);
        await using var deallocate = connection.CreateCommand();
        deallocate.CommandText = $"DEALLOCATE PREPARE {generated.Operation.PreparedStatementName};";

        // Act
        var exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
            context.GetService<IRelationalConnection>(),
            cancellationToken: CancellationToken.None));
        var session = await ObserveSessionAsync(connection, generated.Operation).ConfigureAwait(false);
        var deallocateException = await Assert.ThrowsAsync<MySqlException>(() =>
            deallocate.ExecuteNonQueryAsync(CancellationToken.None));

        // Assert
        Assert.Equal(1062, exception.Number);
        AssertCleanSession(session);
        Assert.Equal(1243, deallocateException.Number);
    }

    /// <summary>Verifies cancellation still completes the scoped cleanup contract.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Cancellation_still_cleans_session(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var connection = CreateConnection(engine, pooling: false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, ScopedScenario.Cancellation, transactionSuppressed: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        // Act
        var exception = await Record.ExceptionAsync(() => generated.Command.ExecuteNonQueryAsync(
            context.GetService<IRelationalConnection>(),
            cancellationToken: cancellation.Token));
        var state = connection.State;
        var session = await ObserveSessionAsync(connection, generated.Operation).ConfigureAwait(false);

        // Assert
        Assert.NotNull(exception);

        if (exception is MySqlException mySqlException)
        {
            Assert.Equal(1317, mySqlException.Number);
        }
        else
        {
            Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
        }

        Assert.Equal(ConnectionState.Open, state);
        AssertCleanSession(session);
    }

    /// <summary>Verifies unsafe cleanup closes its connection and excludes that session from the pool.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Cleanup_failure_quarantines_session_and_continues_cleanup(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var connectionString = CreateConnectionString(engine, pooling: true, connectionReset: false);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var unsafeConnectionId = await ReadConnectionIdAsync(connection).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, ScopedScenario.CleanupFailure, transactionSuppressed: true);
        var operation = generated.Operation;
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE TABLE `{operation.CleanupAuditTableName}` (`Id` int NOT NULL);" + Environment.NewLine)
            .ConfigureAwait(false);

        await using var replacementConnection = new MySqlConnection(connectionString);

        try
        {
            // Act
            var exception = await Assert.ThrowsAsync<MySqlMigrationSessionCleanupException>(() =>
                generated.Command.ExecuteNonQueryAsync(
                    context.GetService<IRelationalConnection>(),
                    cancellationToken: CancellationToken.None));
            var state = connection.State;
            long? replacementId = null;
            long? cleanupCount = null;

            // WHY: If quarantine regresses, opening a second connection against
            // the one-slot pool would hide the state failure behind pool timeout.
            if (state == ConnectionState.Closed)
            {
                await replacementConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                replacementId = await ReadConnectionIdAsync(replacementConnection).ConfigureAwait(false);
                await using var audit = replacementConnection.CreateCommand();
                audit.CommandText = $"SELECT COUNT(*) FROM `{operation.CleanupAuditTableName}`;";
                cleanupCount = Convert.ToInt64(
                    await audit.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }

            // Assert
            Assert.Equal(1644, Assert.IsType<MySqlException>(exception.InnerException).Number);
            Assert.Equal(ConnectionState.Closed, state);
            Assert.NotEqual(unsafeConnectionId, replacementId);
            Assert.Equal(1L, cleanupCount);
        }
        finally
        {
            var dropSql = $"DROP TABLE IF EXISTS `{operation.CleanupAuditTableName}`;" + Environment.NewLine;

            if (replacementConnection.State == ConnectionState.Open)
            {
                await ExecuteNonQueryAsync(replacementConnection, dropSql).ConfigureAwait(false);
            }
            else
            {
                // WHY: Teardown must not need a slot retained by an unsafe session
                // when the quarantine assertion itself fails.
                await using var cleanupConnection = CreateConnection(engine, pooling: false);
                await cleanupConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await ExecuteNonQueryAsync(cleanupConnection, dropSql).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Preserves both the body and cleanup failures in their original order.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TargetCases))]
    public async Task Body_and_cleanup_failure_retains_both_errors(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        await using var connection = CreateConnection(engine, pooling: true);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = CreateContext(connection, engine);
        var generated = GenerateCommand(context, ScopedScenario.BodyAndCleanupFailure, transactionSuppressed: true);

        // Act
        var exception = await Assert.ThrowsAsync<MySqlMigrationSessionCleanupException>(() =>
            generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));
        var state = connection.State;

        // Assert
        var failures = Assert.IsType<AggregateException>(exception.InnerException).InnerExceptions;

        Assert.Collection(
            failures,
            primary => Assert.Equal(1062, Assert.IsType<MySqlException>(primary).Number),
            cleanup => Assert.Equal(1644, Assert.IsType<MySqlException>(cleanup).Number));
        Assert.Equal(ConnectionState.Closed, state);
    }

    /// <summary>Verifies command failure leaves the same physical pooled session clean for reuse.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PoolReuseCases))]
    public async Task Reused_pooled_session_is_clean(
        string target,
        bool connectionReset
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var connectionString = CreateConnectionString(engine, pooling: true, connectionReset: connectionReset);
        long initialConnectionId;

        await using (var seedConnection = new MySqlConnection(connectionString))
        {
            await seedConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            initialConnectionId = await ReadConnectionIdAsync(seedConnection).ConfigureAwait(false);
        }

        ScopedHandlerOperation operation;
        MySqlException exception;

        // WHY: Disposal is part of this lifecycle: the command-owning context
        // must return its connection before the same physical session is reopened.
        // Act
        await using (var context = CreateContext(connectionString, engine))
        {
            var generated = GenerateCommand(context, ScopedScenario.BodyFailure, transactionSuppressed: false);
            operation = generated.Operation;
            exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));
        }

        await using var reusedConnection = new MySqlConnection(connectionString);
        await reusedConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var reusedConnectionId = await ReadConnectionIdAsync(reusedConnection).ConfigureAwait(false);
        var session = await ObserveSessionAsync(reusedConnection, operation).ConfigureAwait(false);

        // Assert
        Assert.Equal(1062, exception.Number);
        Assert.Equal(initialConnectionId, reusedConnectionId);
        AssertCleanSession(session);
    }
}
