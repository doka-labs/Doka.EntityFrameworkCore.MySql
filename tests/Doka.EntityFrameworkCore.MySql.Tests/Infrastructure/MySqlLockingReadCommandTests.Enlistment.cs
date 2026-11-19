namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>A successful explicit enlistment provides proof without depending on Transaction.Current.</summary>
    [Fact]
    public void Confirmed_enlistment_is_accepted_without_an_ambient_scope()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.Null(exception);
        Assert.Same(transaction, scope.Database.Enlisted);
        Assert.Null(Transaction.Current);
    }

    /// <summary>Ambient scope presence alone does not establish enlistment on an already-open connection.</summary>
    [Fact]
    public void Ambient_transaction_presence_without_enlistment_is_rejected()
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Connection.Open();
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.NotNull(Transaction.Current);
        Assert.Null(scope.Database.Enlisted);
    }

    /// <summary>A context reset clears proof even when a caller-owned physical connection stays open.</summary>
    [Fact]
    public void Reset_discards_a_borrowed_connections_enlistment_receipt()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);
        ((IResettableService)scope.Connection).ResetState();

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(TransactionStatus.Active, transaction.TransactionInformation.Status);
    }

    /// <summary>Replacing the configured connection never transfers proof from the previous physical object.</summary>
    [Fact]
    public void Replacing_connection_discards_the_previous_enlistment_receipt()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);
        using var replacement = new RecordingConnection();
        replacement.Open();
        scope.Connection.SetDbConnection(replacement, contextOwnsConnection: false);

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Same(replacement, scope.Connection.DbConnection);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(TransactionStatus.Active, transaction.TransactionInformation.Status);
    }

    /// <summary>Completed enlistment receipts are rejected even if the physical connection remains open.</summary>
    [Fact]
    public void Completed_enlisted_transaction_is_rejected()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);
        transaction.Commit();

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>An external close or broken state clears proof before a caller reopens the same object.</summary>
    /// <param name="endedState">The externally observed state that invalidates the receipt.</param>
    [Theory]
    [InlineData(ConnectionState.Closed)]
    [InlineData(ConnectionState.Broken)]
    public void External_connection_state_change_invalidates_enlistment(
        ConnectionState endedState
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);
        scope.Database.SetState(endedState);
        scope.Database.Open();

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>A failed driver enlistment never produces proof from ambient scope presence alone.</summary>
    [Fact]
    public void Failed_enlistment_does_not_establish_a_transaction_receipt()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = new CommittableTransaction();
        scope.Connection.Open();
        scope.Database.EnlistmentFailure = new InvalidOperationException("enlistment-canary");
        Assert.Throws<InvalidOperationException>(() => scope.Connection.EnlistTransaction(transaction));

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Null(scope.Database.Enlisted);
    }

    /// <summary>A failed re-enlistment invalidates existing proof even if the driver retains the previous transaction.</summary>
    [Fact]
    public void Failed_reenlistment_discards_the_previous_receipt()
    {
        // Arrange
        using var scope = new CommandScope();
        using var original = new CommittableTransaction();
        using var replacement = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(original);
        var driverFailure = new InvalidOperationException("reenlistment-canary");
        scope.Database.EnlistmentFailure = driverFailure;

        // WHY: This sealed connection's protected callback is EF's actual re-enlistment seam.
        // Invoke it directly to isolate failed driver transition from EF's ambient-stack bookkeeping.
        var callback = typeof(MySqlRelationalConnection).GetMethod("ConnectionEnlistTransaction",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(callback);
        var setupFailure = Record.Exception(() => callback.Invoke(scope.Connection, [replacement]));

        // Act
        var exception = Record.Exception(scope.Connection.ValidateLockingReadTransaction);

        // Assert
        Assert.Same(driverFailure,
            Assert.IsType<TargetInvocationException>(setupFailure)
                .InnerException);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Same(original, scope.Database.Enlisted);
        Assert.Equal(TransactionStatus.Active, original.TransactionInformation.Status);
    }

    /// <summary>Failed ambient enlistment retains EF's own lifecycle behavior without an additional guard reference.</summary>
    /// <param name="asynchronous">Whether to exercise asynchronous connection acquisition.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_ambient_enlistment_keeps_the_same_lifecycle_as_ordinary_reads(
        bool asynchronous
    )
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var baseline =
            await RunFailedAmbientEnlistmentAsync(MySqlLockingReadMode.None, asynchronous, cancellationToken);

        // Act
        var actual =
            await RunFailedAmbientEnlistmentAsync(MySqlLockingReadMode.Update, asynchronous, cancellationToken);

        // Assert
        Assert.IsType<InvalidOperationException>(actual.Failure);
        Assert.Equal(baseline.Failure!.Message, actual.Failure.Message);
        Assert.Equal(baseline.State, actual.State);
        Assert.Equal(baseline.CloseCount, actual.CloseCount);
        Assert.Equal(0, actual.ReaderCount);
    }

    /// <summary>A ConnectionOpened failure cannot consume an earlier logical scope after external physical close.</summary>
    /// <param name="asynchronous">Whether to exercise asynchronous connection acquisition.</param>
    /// <param name="callerScope">Whether a caller already holds a logical open reference.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Opened_interception_failure_does_not_consume_the_callers_open_scope(
        bool asynchronous,
        bool callerScope
    )
    {
        // Arrange
        var interceptor = new ThrowingOpenedInterceptor();
        using var scope = new CommandScope(interceptor);
        if (callerScope)
        {
            scope.Connection.Open();
            scope.Database.Close();
        }

        interceptor.Enabled = true;
        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.Same(interceptor.Failure, exception);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(callerScope
                ? 1
                : 0,
            scope.Database.CloseCount);

        Assert.Equal(0, scope.Database.ReaderCount);
    }

    private static async Task<(Exception? Failure, ConnectionState State, int CloseCount, int ReaderCount)>
        RunFailedAmbientEnlistmentAsync(
            MySqlLockingReadMode mode,
            bool asynchronous,
            CancellationToken cancellationToken
        )
    {
        using var scope = new CommandScope();
        scope.Connection.Open();
        scope.Database.EnlistmentFailure = new InvalidOperationException("ambient-enlistment-canary");
        using var ambient =
            new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled);

        var command = scope.Command(mode);
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // WHY: EF's failed-open behavior is the control, not an inferred private count.
        // A locking command must not introduce another acquisition or consume the caller's reference.
        scope.Connection.Close();

        return (exception, scope.Database.State, scope.Database.CloseCount, scope.Database.ReaderCount);
    }
}
