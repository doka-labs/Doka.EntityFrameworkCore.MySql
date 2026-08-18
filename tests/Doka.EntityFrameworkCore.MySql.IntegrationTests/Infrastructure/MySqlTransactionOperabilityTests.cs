namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the transaction-operability live baseline against representative live targets.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "DriverContract")]
public sealed class MySqlTransactionOperabilityTests
{
    private const string TransactionTableName = "Phase3TransactionEntities";

    /// <summary>
    /// Verifies every reusable transaction-operability contract against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_satisfies_the_transaction_operability_contract() =>
        AssertTransactionOperabilityContractAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies every reusable transaction-operability contract against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_satisfies_the_transaction_operability_contract() =>
        AssertTransactionOperabilityContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies every reusable transaction-operability contract against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_satisfies_the_transaction_operability_contract() =>
        AssertTransactionOperabilityContractAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// Verifies that rolling back to a savepoint preserves the pre-savepoint state.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Savepoints_preserve_the_pre_savepoint_state()
    {
        await AssertSavepointsPreserveStateAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 savepoints preserve the pre-savepoint state.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_savepoints_preserve_the_pre_savepoint_state()
    {
        await AssertSavepointsPreserveStateAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 savepoints preserve the pre-savepoint state.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_savepoints_preserve_the_pre_savepoint_state()
    {
        await AssertSavepointsPreserveStateAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that retry-enabled execution strategies reject user-managed transactions outside the explicit retry boundary.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Retrying_execution_strategy_rejects_user_managed_transactions_outside_the_retry_boundary()
    {
        await AssertUserManagedTransactionsAreRejectedAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 rejects user-managed transactions outside the
    /// explicit retry boundary.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task
        MariaDb114_retrying_execution_strategy_rejects_user_managed_transactions_outside_the_retry_boundary()
    {
        await AssertUserManagedTransactionsAreRejectedAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 rejects user-managed transactions outside the explicit retry boundary.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task
        MariaDb118_retrying_execution_strategy_rejects_user_managed_transactions_outside_the_retry_boundary()
    {
        await AssertUserManagedTransactionsAreRejectedAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the documented verify-succeeded execution-strategy pattern runs successfully.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Execute_in_transaction_async_supports_the_verify_succeeded_pattern()
    {
        await AssertVerifySucceededPatternAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 supports the verify-succeeded execution-strategy pattern.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_execute_in_transaction_async_supports_the_verify_succeeded_pattern()
    {
        await AssertVerifySucceededPatternAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 supports the verify-succeeded execution-strategy pattern.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_execute_in_transaction_async_supports_the_verify_succeeded_pattern()
    {
        await AssertVerifySucceededPatternAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the supported async ambient transaction path commits successfully when completed.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Transaction_scope_async_flow_commits_when_completed()
    {
        await AssertTransactionScopeAsyncFlowCommitsAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.4 supports the async ambient transaction path.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_transaction_scope_async_flow_commits_when_completed()
    {
        await AssertTransactionScopeAsyncFlowCommitsAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 supports the async ambient transaction path when completed.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_transaction_scope_async_flow_commits_when_completed()
    {
        await AssertTransactionScopeAsyncFlowCommitsAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    private static async Task AssertTransactionOperabilityContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await AssertSavepointsPreserveStateAsync(target, serverVersion)
            .ConfigureAwait(false);
        await AssertUserManagedTransactionsAreRejectedAsync(target, serverVersion)
            .ConfigureAwait(false);
        await AssertVerifySucceededPatternAsync(target, serverVersion)
            .ConfigureAwait(false);
        await AssertTransactionScopeAsyncFlowCommitsAsync(target, serverVersion)
            .ConfigureAwait(false);
    }

    private static async Task AssertSavepointsPreserveStateAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new TransactionOperabilityContext(CreateOptions(connectionString, serverVersion));
            var strategy = context.Database.CreateExecutionStrategy();

            await strategy
                .ExecuteAsync(async () =>
                {
                    await using var transaction = await context
                        .Database.BeginTransactionAsync()
                        .ConfigureAwait(false);

                    Assert.True(transaction.SupportsSavepoints);

                    context.Entities.Add(
                        new TransactionEntity
                        {
                            Name = "before-savepoint",
                        });
                    await context
                        .SaveChangesAsync()
                        .ConfigureAwait(false);

                    await transaction
                        .CreateSavepointAsync("before-second-insert")
                        .ConfigureAwait(false);

                    context.Entities.Add(
                        new TransactionEntity
                        {
                            Name = "after-savepoint",
                        });
                    await context
                        .SaveChangesAsync()
                        .ConfigureAwait(false);

                    await transaction
                        .RollbackToSavepointAsync("before-second-insert")
                        .ConfigureAwait(false);
                    await transaction
                        .CommitAsync()
                        .ConfigureAwait(false);
                })
                .ConfigureAwait(false);

            await using var verificationContext = new TransactionOperabilityContext(
                CreateOptions(connectionString, serverVersion));

            var names = await verificationContext
                .Entities.OrderBy(entity => entity.Id)
                .Select(entity => entity.Name)
                .ToListAsync()
                .ConfigureAwait(false);

            var persistedName = Assert.Single(names);

            Assert.Equal("before-savepoint", persistedName);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertUserManagedTransactionsAreRejectedAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var context = new TransactionOperabilityContext(CreateOptions(connectionString, serverVersion));
        await using var transaction = await context
            .Database.BeginTransactionAsync()
            .ConfigureAwait(false);

        var strategy = context.Database.CreateExecutionStrategy();

        var exception = await Assert
            .ThrowsAsync<InvalidOperationException>(() => strategy.ExecuteAsync(async () =>
            {
                await context
                    .Database.ExecuteSqlRawAsync("SELECT 1")
                    .ConfigureAwait(false);

                return 1;
            }))
            .ConfigureAwait(false);

        Assert.Contains("does not support user-initiated transactions", exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertVerifySucceededPatternAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new TransactionOperabilityContext(CreateOptions(connectionString, serverVersion));
            var strategy = context.Database.CreateExecutionStrategy();

            await strategy
                .ExecuteInTransactionAsync(
                    async cancellationToken =>
                    {
                        context.Entities.Add(
                            new TransactionEntity
                            {
                                Name = "verify-succeeded",
                            });
                        await context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    },
                    async cancellationToken => await context
                        .Entities.AnyAsync(entity => entity.Name == "verify-succeeded", cancellationToken)
                        .ConfigureAwait(false),
                    IsolationLevel.ReadCommitted)
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();

            Assert.True(
                await context
                    .Entities.AnyAsync(entity => entity.Name == "verify-succeeded")
                    .ConfigureAwait(false));
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertTransactionScopeAsyncFlowCommitsAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var connectionString = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            AutoEnlist = true,
        }.ConnectionString;

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using (var context =
                         new TransactionOperabilityContext(CreateOptions(connectionString, serverVersion)))
            {
                var strategy = context.Database.CreateExecutionStrategy();

                await strategy
                    .ExecuteAsync(async () =>
                    {
                        using var scope = new System.Transactions.TransactionScope(
                            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

                        context.Entities.Add(
                            new TransactionEntity
                            {
                                Name = "ambient-transaction",
                            });
                        await context
                            .SaveChangesAsync()
                            .ConfigureAwait(false);

                        scope.Complete();
                    })
                    .ConfigureAwait(false);
            }

            await using var verificationContext = new TransactionOperabilityContext(
                CreateOptions(connectionString, serverVersion));

            Assert.True(
                await verificationContext
                    .Entities.AnyAsync(entity => entity.Name == "ambient-transaction")
                    .ConfigureAwait(false));
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<TransactionOperabilityContext> CreateOptions(
        string connectionString,
        MySqlServerVersion serverVersion
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<TransactionOperabilityContext>();

        builder.UseMySql(
            connectionString,
            serverVersion,
            options => options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1)));

        return builder.Options;
    }

    private static async Task ResetDatabaseObjectsAsync(
        string connectionString
    )
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(connectionString)
            .ConfigureAwait(false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{TransactionTableName}`;
                               CREATE TABLE `{TransactionTableName}` (
                                   `Id` int NOT NULL AUTO_INCREMENT,
                                   `Name` longtext NOT NULL,
                                   CONSTRAINT `PK_{TransactionTableName}` PRIMARY KEY (`Id`)
                               ) CHARACTER SET utf8mb4;
                               """;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class TransactionOperabilityContext : DbContext
    {
        public TransactionOperabilityContext(
            DbContextOptions<TransactionOperabilityContext> options
        ) : base(options) { }

        public DbSet<TransactionEntity> Entities => Set<TransactionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TransactionEntity>(entity =>
            {
                entity.ToTable(TransactionTableName);
                entity.HasKey(candidate => candidate.Id);
                entity.Property(candidate => candidate.Name);
            });
        }
    }

    private sealed class TransactionEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
