using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Qualifies handler-authored scoped migration commands against every
/// supported MySQL-family server without depending on an external package.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlScopedMigrationOperationIntegrationTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_scoped_handler_contract() =>
        AssertScopedHandlerContractAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertScopedHandlerContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        await AssertAsyncSuccessAndFailureAsync(target)
            .ConfigureAwait(false);
        AssertSynchronousSuccessAndFailure(target);
        await AssertSetupFailuresStillCleanAsync(target)
            .ConfigureAwait(false);
        await AssertPreparedBodyFailureStillCleansAsync(target)
            .ConfigureAwait(false);
        await AssertCancellationStillCleansAsync(target)
            .ConfigureAwait(false);
        await AssertCleanupFailureClosesUnsafeConnectionAsync(target)
            .ConfigureAwait(false);
        await AssertBodyAndCleanupFailureRetainsBothAsync(target)
            .ConfigureAwait(false);
        await AssertPoolReuseIsCleanAsync(target, connectionReset: false)
            .ConfigureAwait(false);
        await AssertPoolReuseIsCleanAsync(target, connectionReset: true)
            .ConfigureAwait(false);
    }

    private static async Task AssertAsyncSuccessAndFailureAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = CreateConnection(target, pooling: false);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (var context = CreateContext(connection, target))
        {
            var success = GenerateCommand(context, ScopedScenario.Success, transactionSuppressed: false);
            var result = await success
                .Command
                .ExecuteNonQueryAsync(
                    context.GetService<IRelationalConnection>(),
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(1, result);
            Assert.False(success.Command.TransactionSuppressed);
            Assert.Equal(ConnectionState.Open, connection.State);
            await AssertSessionCleanAsync(connection, success.Operation)
                .ConfigureAwait(false);
        }

        await using (var context = CreateContext(connection, target))
        {
            var failure = GenerateCommand(context, ScopedScenario.BodyFailure, transactionSuppressed: true);
            var exception = await Assert.ThrowsAsync<MySqlException>(() => failure.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));

            Assert.Equal(1062, exception.Number);
            Assert.True(failure.Command.TransactionSuppressed);
            Assert.Equal(ConnectionState.Open, connection.State);
            await AssertSessionCleanAsync(connection, failure.Operation)
                .ConfigureAwait(false);
        }
    }

    private static void AssertSynchronousSuccessAndFailure(
        IntegrationDatabaseTarget target
    )
    {
        using var connection = CreateConnection(target, pooling: false);
        connection.Open();

        using (var context = CreateContext(connection, target))
        {
            var success = GenerateCommand(context, ScopedScenario.Success, transactionSuppressed: false);

            Assert.Equal(1, success.Command.ExecuteNonQuery(context.GetService<IRelationalConnection>()));
            Assert.Equal(ConnectionState.Open, connection.State);
            AssertSessionClean(connection, success.Operation);
        }

        using (var context = CreateContext(connection, target))
        {
            var failure = GenerateCommand(context, ScopedScenario.BodyFailure, transactionSuppressed: false);
            var exception = Assert.Throws<MySqlException>(() =>
                failure.Command.ExecuteNonQuery(context.GetService<IRelationalConnection>()));

            Assert.Equal(1062, exception.Number);
            Assert.Equal(ConnectionState.Open, connection.State);
            AssertSessionClean(connection, failure.Operation);
        }
    }

    private static async Task AssertSetupFailuresStillCleanAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = CreateConnection(target, pooling: false);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var scenario in new[]
                 {
                     ScopedScenario.FirstSetupFailure,
                     ScopedScenario.LaterSetupFailure
                 })
        {
            await using var context = CreateContext(connection, target);
            var generated = GenerateCommand(context, scenario, transactionSuppressed: false);
            var exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));

            Assert.Equal(1644, exception.Number);
            Assert.Equal(ConnectionState.Open, connection.State);
            await AssertSessionCleanAsync(connection, generated.Operation)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertPreparedBodyFailureStillCleansAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = CreateConnection(target, pooling: false);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = CreateContext(connection, target);
        var generated = GenerateCommand(context, ScopedScenario.PreparedBodyFailure, transactionSuppressed: true);

        var exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
            context.GetService<IRelationalConnection>(),
            cancellationToken: CancellationToken.None));

        Assert.Equal(1062, exception.Number);
        await AssertSessionCleanAsync(connection, generated.Operation)
            .ConfigureAwait(false);

        await using var deallocate = connection.CreateCommand();
        deallocate.CommandText = $"DEALLOCATE PREPARE {generated.Operation.PreparedStatementName};";
        var deallocateException = await Assert.ThrowsAsync<MySqlException>(() =>
            deallocate.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal(1243, deallocateException.Number);
    }

    private static async Task AssertCancellationStillCleansAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = CreateConnection(target, pooling: false);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = CreateContext(connection, target);
        var generated = GenerateCommand(context, ScopedScenario.Cancellation, transactionSuppressed: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() => generated.Command.ExecuteNonQueryAsync(
            context.GetService<IRelationalConnection>(),
            cancellationToken: cancellation.Token));

        Assert.NotNull(exception);
        if (exception is MySqlException mySqlException)
        {
            Assert.Equal(1317, mySqlException.Number);
        }
        else
        {
            Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
        }

        Assert.Equal(ConnectionState.Open, connection.State);
        await AssertSessionCleanAsync(connection, generated.Operation)
            .ConfigureAwait(false);
    }

    private static async Task AssertCleanupFailureClosesUnsafeConnectionAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = CreateConnectionString(target, pooling: true, connectionReset: false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var unsafeConnectionId = await ReadConnectionIdAsync(connection)
            .ConfigureAwait(false);

        await using var context = CreateContext(connection, target);
        var (migrationCommand, operation) = GenerateCommand(
            context,
            ScopedScenario.CleanupFailure,
            transactionSuppressed: true);

        await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE `{operation.CleanupAuditTableName}` (`Id` int NOT NULL);" + Environment.NewLine)
            .ConfigureAwait(false);

        var exception = await Assert.ThrowsAsync<MySqlMigrationSessionCleanupException>(() =>
            migrationCommand.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));

        Assert.Equal(
            1644,
            Assert.IsType<MySqlException>(exception.InnerException)
                .Number);
        Assert.Equal(ConnectionState.Closed, connection.State);

        await using var replacementConnection = new MySqlConnection(connectionString);
        await replacementConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            Assert.NotEqual(
                unsafeConnectionId,
                await ReadConnectionIdAsync(replacementConnection)
                    .ConfigureAwait(false));

            await using var audit = replacementConnection.CreateCommand();
            audit.CommandText = $"SELECT COUNT(*) FROM `{operation.CleanupAuditTableName}`;";

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await audit
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await ExecuteNonQueryAsync(
                    replacementConnection,
                    $"DROP TABLE IF EXISTS `{operation.CleanupAuditTableName}`;" + Environment.NewLine)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertBodyAndCleanupFailureRetainsBothAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var connection = CreateConnection(target, pooling: true);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = CreateContext(connection, target);
        var generated = GenerateCommand(context, ScopedScenario.BodyAndCleanupFailure, transactionSuppressed: true);

        var exception = await Assert.ThrowsAsync<MySqlMigrationSessionCleanupException>(() =>
            generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));

        var failures = Assert.IsType<AggregateException>(exception.InnerException)
            .InnerExceptions;

        Assert.Collection(
            failures,
            primary => Assert.Equal(
                1062,
                Assert.IsType<MySqlException>(primary)
                    .Number),
            cleanup => Assert.Equal(
                1644,
                Assert.IsType<MySqlException>(cleanup)
                    .Number));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static async Task AssertPoolReuseIsCleanAsync(
        IntegrationDatabaseTarget target,
        bool connectionReset
    )
    {
        var connectionString = CreateConnectionString(target, pooling: true, connectionReset: connectionReset);
        long initialConnectionId;
        ScopedHandlerOperation operation;

        await using (var seedConnection = new MySqlConnection(connectionString))
        {
            await seedConnection
                .OpenAsync(CancellationToken.None)
                .ConfigureAwait(false);
            initialConnectionId = await ReadConnectionIdAsync(seedConnection)
                .ConfigureAwait(false);
        }

        await using (var context = CreateContext(connectionString, target))
        {
            var generated = GenerateCommand(context, ScopedScenario.BodyFailure, transactionSuppressed: false);
            operation = generated.Operation;
            var exception = await Assert.ThrowsAsync<MySqlException>(() => generated.Command.ExecuteNonQueryAsync(
                context.GetService<IRelationalConnection>(),
                cancellationToken: CancellationToken.None));

            Assert.Equal(1062, exception.Number);
        }

        await using var reusedConnection = new MySqlConnection(connectionString);
        await reusedConnection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            initialConnectionId,
            await ReadConnectionIdAsync(reusedConnection)
                .ConfigureAwait(false));
        await AssertSessionCleanAsync(reusedConnection, operation)
            .ConfigureAwait(false);
    }

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
        var command = Assert.Single(
            context
                .GetService<IMigrationsSqlGenerator>()
                .Generate([operation], context.Model));

        Assert.IsType<MySqlScopedMigrationCommand>(command);

        return new GeneratedCommand(command, operation);
    }

    private static async Task AssertSessionCleanAsync(
        MySqlConnection connection,
        ScopedHandlerOperation operation
    )
    {
        await using var variable = connection.CreateCommand();
        variable.CommandText = $"SELECT {operation.VariableName} IS NULL;";

        Assert.Equal(
            1L,
            Convert.ToInt64(
                await variable
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture));

        await using var table = connection.CreateCommand();
        table.CommandText = $"SELECT COUNT(*) FROM `{operation.TableName}`;";
        var exception = await Assert.ThrowsAsync<MySqlException>(() =>
            table.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(1146, exception.Number);
    }

    private static void AssertSessionClean(
        MySqlConnection connection,
        ScopedHandlerOperation operation
    )
    {
        using var variable = connection.CreateCommand();
        variable.CommandText = $"SELECT {operation.VariableName} IS NULL;";

        Assert.Equal(1L, Convert.ToInt64(variable.ExecuteScalar(), CultureInfo.InvariantCulture));

        using var table = connection.CreateCommand();
        table.CommandText = $"SELECT COUNT(*) FROM `{operation.TableName}`;";
        var exception = Assert.Throws<MySqlException>(table.ExecuteScalar);

        Assert.Equal(1146, exception.Number);
    }

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

    private static MySqlConnection CreateConnection(
        IntegrationDatabaseTarget target,
        bool pooling
    ) => new(CreateConnectionString(target, pooling));

    private static string CreateConnectionString(
        IntegrationDatabaseTarget target,
        bool pooling,
        bool connectionReset = false
    ) => new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
    {
        AllowUserVariables = true,
        ConnectionReset = connectionReset,
        MinimumPoolSize = pooling ? 1u : 0u,
        MaximumPoolSize = pooling ? 1u : 100u,
        Pooling = pooling,
    }.ConnectionString;

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

    private enum ScopedScenario
    {
        Success,
        BodyFailure,
        FirstSetupFailure,
        LaterSetupFailure,
        PreparedBodyFailure,
        Cancellation,
        CleanupFailure,
        BodyAndCleanupFailure,
    }

    private sealed class ScopedHandlerOperation : MigrationOperation
    {
        public ScopedHandlerOperation(
            ScopedScenario scenario,
            string suffix,
            bool transactionSuppressed
        )
        {
            Scenario = scenario;
            Suffix = suffix;
            TransactionSuppressed = transactionSuppressed;
        }

        public string PreparedStatementName => $"doka_scope_statement_{Suffix}";

        public string CleanupAuditTableName => $"DokaScopeAudit_{Suffix}";

        public ScopedScenario Scenario { get; }

        public string Suffix { get; }

        public string TableName => $"DokaScope_{Suffix}";

        public bool TransactionSuppressed { get; }

        public string VariableName => $"@doka_scope_{Suffix}";
    }

    private sealed class ScopedHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.scoped.runtime";

        public Type OperationType => typeof(ScopedHandlerOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (ScopedHandlerOperation)context.Operation;
            var command = operation.Scenario switch
            {
                ScopedScenario.Success =>
                    StandardCommand(operation, $"INSERT INTO `{operation.TableName}` (`Id`) VALUES (1);"),
                ScopedScenario.BodyFailure =>
                    StandardCommand(operation, $"INSERT INTO `{operation.TableName}` (`Id`) VALUES (1), (1);"),
                ScopedScenario.FirstSetupFailure =>
                    MySqlMigrationCommandSpec.CreateScoped(
                        ["SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO = 1644, MESSAGE_TEXT = 'setup failure';"],
                        $"SET {operation.VariableName} = 2;",
                        [$"SET {operation.VariableName} = NULL;"],
                        operation.TransactionSuppressed),
                ScopedScenario.LaterSetupFailure =>
                    MySqlMigrationCommandSpec.CreateScoped(
                        [
                            CreateTemporaryTable(operation),
                            "SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO = 1644, MESSAGE_TEXT = 'setup failure';",
                        ],
                        $"SET {operation.VariableName} = 2;",
                        StandardCleanup(operation),
                        operation.TransactionSuppressed),
                ScopedScenario.PreparedBodyFailure => MySqlMigrationCommandSpec.CreateScoped(
                    [
                        CreateTemporaryTable(operation),
                        $"SET {operation.VariableName} = "
                        + $"'INSERT INTO `{operation.TableName}` (`Id`) VALUES (1), (1)';",
                        $"PREPARE {operation.PreparedStatementName} FROM {operation.VariableName};",
                    ],
                    $"EXECUTE {operation.PreparedStatementName};",
                    [
                        $"DROP TEMPORARY TABLE IF EXISTS `{operation.TableName}`;",
                        $"SET {operation.VariableName} = NULL;",
                        $"DEALLOCATE PREPARE {operation.PreparedStatementName};",
                    ],
                    operation.TransactionSuppressed),
                ScopedScenario.Cancellation => CancellationCommand(operation),
                ScopedScenario.CleanupFailure => FailingCleanupCommand(
                    operation,
                    $"INSERT INTO `{operation.TableName}` (`Id`) VALUES (1);",
                    recordCleanupContinuation: true),
                ScopedScenario.BodyAndCleanupFailure => FailingCleanupCommand(
                    operation,
                    $"INSERT INTO `{operation.TableName}` (`Id`) VALUES (1), (1);",
                    recordCleanupContinuation: false),
                _ => throw new InvalidOperationException(
                    $"Unknown scoped migration test scenario '{operation.Scenario}'."),
            };

            return MySqlMigrationOperationResult.Generated([command], "scoped_runtime");
        }

        private static MySqlMigrationCommandSpec StandardCommand(
            ScopedHandlerOperation operation,
            string body
        ) => MySqlMigrationCommandSpec.CreateScoped(
            [
                CreateTemporaryTable(operation),
                $"SET {operation.VariableName} = 1;",
            ],
            body,
            StandardCleanup(operation),
            operation.TransactionSuppressed);

        private static MySqlMigrationCommandSpec FailingCleanupCommand(
            ScopedHandlerOperation operation,
            string body,
            bool recordCleanupContinuation
        )
        {
            var cleanup = new List<string>();

            if (recordCleanupContinuation)
            {
                cleanup.Add($"INSERT INTO `{operation.CleanupAuditTableName}` (`Id`) VALUES (1);");
            }

            cleanup.Add("SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO = 1644, MESSAGE_TEXT = 'cleanup failure';");
            cleanup.Add($"DROP TEMPORARY TABLE IF EXISTS `{operation.TableName}`;");
            cleanup.Add($"SET {operation.VariableName} = NULL;");

            return MySqlMigrationCommandSpec.CreateScoped(
                [
                    CreateTemporaryTable(operation),
                    $"SET {operation.VariableName} = 1;",
                ],
                body,
                cleanup,
                operation.TransactionSuppressed);
        }

        private static MySqlMigrationCommandSpec CancellationCommand(
            ScopedHandlerOperation operation
        ) => MySqlMigrationCommandSpec.CreateScoped(
            [
                CreateTemporaryTable(operation),
                $"SET {operation.VariableName} = 1;",
                $"INSERT INTO `{operation.TableName}` (`Id`) VALUES (1);",
            ],
            $"SELECT 1 FROM `{operation.TableName}` WHERE SLEEP(30) = 0;",
            StandardCleanup(operation),
            operation.TransactionSuppressed);

        private static string CreateTemporaryTable(
            ScopedHandlerOperation operation
        ) => $"CREATE TEMPORARY TABLE `{operation.TableName}` (`Id` int NOT NULL PRIMARY KEY);";

        private static string[] StandardCleanup(
            ScopedHandlerOperation operation
        ) =>
        [
            $"DROP TEMPORARY TABLE IF EXISTS `{operation.TableName}`;",
            $"SET {operation.VariableName} = NULL;",
        ];
    }

    private sealed class ScopedHandlerOptionsExtension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(
            IServiceCollection services
        ) => services.TryAddEnumerable(ServiceDescriptor.Scoped<IMySqlMigrationOperationHandler, ScopedHandler>());

        public void Validate(
            IDbContextOptions options
        )
        {
            if (options.FindExtension<MySqlOptionsExtension>() is null)
            {
                throw new InvalidOperationException("The scoped handler tests require the Doka MySQL provider.");
            }
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(
                IDbContextOptionsExtension extension
            ) : base(extension) { }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "scoped-handler-tests ";

            public override int GetServiceProviderHashCode() => 0;

            public override void PopulateDebugInfo(
                IDictionary<string, string> debugInfo
            ) => debugInfo["Doka:ScopedHandlerTests"] = "1";

            public override bool ShouldUseSameServiceProvider(
                DbContextOptionsExtensionInfo other
            ) => other is ExtensionInfo;
        }
    }

    private sealed class ScopedHandlerContext : DbContext
    {
        public ScopedHandlerContext(
            DbContextOptions<ScopedHandlerContext> options
        ) : base(options) { }
    }
}
