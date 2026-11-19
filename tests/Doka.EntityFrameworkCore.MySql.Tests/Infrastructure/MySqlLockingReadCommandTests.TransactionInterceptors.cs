namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Authorized transaction callback failures propagate while owned-marker cleanup remains attempted.</summary>
    /// <param name="asynchronous">Whether validation uses the asynchronous callback path.</param>
    /// <param name="creating">Whether creating rather than rollback-to throws.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Transaction_interceptor_failure_is_retained_without_disposing_the_caller_transaction(
        bool asynchronous,
        bool creating
    )
    {
        // Arrange
        var failure = new IOException("transaction-interceptor-canary");
        var interceptor = new SavepointFailureInterceptor(creating, failure);
        using var scope = new CommandScope(interceptor);
        using var transaction = scope.Connection.BeginTransaction();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlLocalTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.Same(failure, exception);
        Assert.Equal(creating
                ? 1
                : 2,
            scope.Database.LocalControlSql.Count);

        Assert.StartsWith("RELEASE SAVEPOINT", scope.Database.LocalControlSql[^1], StringComparison.Ordinal);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(0, scope.Database.ReaderCount);
    }

    private sealed class SavepointFailureInterceptor : DbTransactionInterceptor
    {
        private readonly bool _creating;
        private readonly Exception _failure;

        /// <summary>Initializes the interceptor with the behavior observed by this test.</summary>
        public SavepointFailureInterceptor(
            bool creating,
            Exception failure
        )
        {
            _creating = creating;
            _failure = failure;
        }

        public override InterceptionResult CreatingSavepoint(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result
        )
        {
            if (_creating)
            {
                throw _failure;
            }

            return result;
        }

        public override ValueTask<InterceptionResult> CreatingSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            // WHY: The fake represents an immediate callback failure and has no asynchronous I/O.
            // ReSharper disable once MethodHasAsyncOverload
            return ValueTask.FromResult(CreatingSavepoint(transaction, eventData, result));
        }

        public override InterceptionResult RollingBackToSavepoint(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result
        )
        {
            if (!_creating)
            {
                throw _failure;
            }

            return result;
        }

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            // WHY: Both callback paths expose the same deterministic failure without scheduling work.
            // ReSharper disable once MethodHasAsyncOverload
            return ValueTask.FromResult(RollingBackToSavepoint(transaction, eventData, result));
        }
    }
}
