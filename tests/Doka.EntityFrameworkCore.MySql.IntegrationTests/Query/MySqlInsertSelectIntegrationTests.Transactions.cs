using IsolationLevel = System.Transactions.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Local commit and rollback are distinct native outcomes, never implicit terminal behavior.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Local_transaction_remains_caller_owned_until_explicit_commit_or_rollback(
        string target,
        bool commit
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var connection = context.Database.GetDbConnection();

        // Act
        var count = await context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        var before = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);
        var retainedOwnership = ReferenceEquals(transaction, context.Database.CurrentTransaction);

        if (commit)
        {
            await transaction.CommitAsync(safety.Token);
        }
        else
        {
            await transaction.RollbackAsync(safety.Token);
        }

        var after = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(["0"], before);
        Assert.Equal([
                commit
                    ? "3"
                    : "0",
            ],
            after);

        Assert.True(retainedOwnership);
        Assert.Same(connection, Assert.Single(commands.Connections));
        Assert.Single(commands.Statements);
    }

    /// <summary>A borrowed native transaction survives context disposal and is committed only by its owner.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Borrowed_connection_and_transaction_survive_context_disposal(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var connection = await store.OpenNativeAsync(safety.Token);
        await using var transaction = await connection.BeginTransactionAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands, connection);
        await context.Database.UseTransactionAsync(transaction, safety.Token);

        // Act
        var count = await context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        await context.DisposeAsync();
        var remainsOwned = ReferenceEquals(transaction.Connection, connection)
            && connection.State == ConnectionState.Open;

        var before = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);
        await transaction.CommitAsync(safety.Token);
        var after = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.True(remainsOwned);
        Assert.Equal(["0"], before);
        Assert.Equal(["3"], after);
        Assert.Same(connection, Assert.Single(commands.Connections));
    }

    /// <summary>Actual native visibility demonstrates ambient enlistment and the scope owner's final decision.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Ambient_scope_controls_commit_and_rollback_on_the_actual_connection(
        string target,
        bool complete
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        var count = 0;
        string[] before;
        DbConnection? usedConnection;

        // Act
        using (var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var context = store.CreateContext(commands);
            count = await context
                .Set<InsertSource>()
                .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                    setters => setters
                        .SetProperty(row => row.Value, row => row.Value)
                        .SetProperty(row => row.Code, row => row.Label),
                    safety.Token);

            usedConnection = context.Database.GetDbConnection();
            before = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

            if (complete)
            {
                scope.Complete();
            }
        }

        var after = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(["0"], before);
        Assert.Equal([
                complete
                    ? "3"
                    : "0",
            ],
            after);

        Assert.Same(usedConnection, Assert.Single(commands.Connections));
    }

    /// <summary>A canceled invocation performs no native command and leaves the pooled context reusable.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Early_cancellation_is_no_write_and_does_not_poison_pooled_reuse(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var services = store.CreatePooledServices(commands);
        var factory = services.GetRequiredService<IDbContextFactory<InsertContext>>();
        await using var context = await factory.CreateDbContextAsync(safety.Token);

        // Act
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                canceled.Token));

        await context.DisposeAsync();
        await using var reused = await factory.CreateDbContextAsync(safety.Token);
        var healthy = await reused
            .Set<InsertSource>()
            .CountAsync(safety.Token);

        var persisted = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);

        // Assert
        Assert.Equal(canceled.Token, error.CancellationToken);
        Assert.Same(context, reused);
        Assert.Equal(3, healthy);
        Assert.Equal(["0"], persisted);
        Assert.Empty(commands.Statements);
    }

    /// <summary>Changed tenant/source parameters are rebound after a genuine one-entry context-pool reuse.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Pool_reuse_and_parameter_changes_do_not_reuse_stale_target_or_tenant_state(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var services = store.CreatePooledServices(commands);
        var factory = services.GetRequiredService<IDbContextFactory<InsertContext>>();
        await using var first = await factory.CreateDbContextAsync(safety.Token);
        first.TenantId = 1;
        var threshold = 15;

        // Act
        var firstCount = await first
            .Set<InsertSource>()
            .Where(row => row.Value > threshold)
            .ExecuteInsertFromQueryAsync(first.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        await first.DisposeAsync();
        await using var second = await factory.CreateDbContextAsync(safety.Token);
        second.TenantId = 2;
        threshold = 35;
        var secondCount = await second
            .Set<InsertSource>()
            .Where(row => row.Value > threshold)
            .ExecuteInsertFromQueryAsync(second.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(2, firstCount);
        Assert.Equal(1, secondCount);
        Assert.Equal(["20|beta", "30|gamma", "50|other"], actual);
        Assert.Equal(2, commands.Statements.Count);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(second.ChangeTracker.Entries());
    }

    /// <summary>Post-write interceptor failure preserves its identity without retrying or falsely declaring rollback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Failure_after_native_write_does_not_imply_rollback_or_a_second_attempt(
        string target,
        bool cancel
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        using var cancellation = new CancellationTokenSource();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        Exception primary = cancel
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("post-write observer failed");

        var interceptor = new AfterWriteFailure(primary, cancellation, cancel);
        await using var context = new InsertContext(new DbContextOptionsBuilder<InsertContext>(store.CreateOptions())
            .AddInterceptors(interceptor)
            .Options);

        // Act
        var error = await Record.ExceptionAsync(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                cancellation.Token));

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        var healthy = await context
            .Set<InsertSource>()
            .CountAsync(safety.Token);

        // Assert
        Assert.Same(primary, error);
        Assert.Equal(["10", "20", "30"], actual);
        Assert.Equal(1, interceptor.Attempts);
        Assert.Equal(3, healthy);
    }

    /// <summary>Injects only after a real successful INSERT, making the completed write independently observable.</summary>
    private sealed class AfterWriteFailure : DbCommandInterceptor
    {
        private readonly bool _cancel;
        private readonly CancellationTokenSource _cancellation;
        private readonly Exception _primary;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public AfterWriteFailure(
            Exception primary,
            CancellationTokenSource cancellation,
            bool cancel
        )
        {
            _primary = primary;
            _cancellation = cancellation;
            _cancel = cancel;
        }

        public int Attempts { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken
        )
        {
            Attempts++;

            return new ValueTask<InterceptionResult<int>>(result);
        }

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken
        )
        {
            if (_cancel)
            {
                _cancellation.Cancel();
            }

            throw _primary;
        }
    }
}
