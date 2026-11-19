namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Repeated native missing-marker errors retain both operation identities and never permit the locking reader.</summary>
    /// <param name="asynchronous">Whether the locking command uses asynchronous execution.</param>
    /// <param name="emulatedAmbient">Whether the caller owns an emulated ambient rather than local transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Distinct_missing_marker_failures_remain_visible_before_locking_data(
        bool asynchronous,
        bool emulatedAmbient
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = emulatedAmbient
            ? new CommittableTransaction()
            : null;

        using var local = emulatedAmbient
            ? null
            : scope.Connection.BeginTransaction();

        if (ambient is not null)
        {
            EnlistEmulatedAmbient(scope, ambient);
        }

        var receiptFailure = CreateNativeFailure(1305);
        var releaseFailure = CreateNativeFailure(1305);
        scope.Database.LocalControlFailure = sql => sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal)
            ? releaseFailure
            : sql.StartsWith(emulatedAmbient
                    ? "SAVEPOINT"
                    : "ROLLBACK TO",
                StringComparison.Ordinal)
                ? receiptFailure
                : null;

        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Collection(failures,
            failure => Assert.Same(receiptFailure, failure),
            failure => Assert.Same(releaseFailure, failure));

        Assert.All(failures,
            failure => Assert.Equal(1305,
                Assert.IsType<MySqlException>(failure)
                    .Number));

        Assert.Equal(emulatedAmbient
                ? 2
                : 3,
            scope.Database.LocalControlSql.Count);

        Assert.Equal(scope.Database.LocalControlSql.Count, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Same(local, scope.Connection.CurrentTransaction);

        if (ambient is not null)
        {
            Assert.Same(ambient, scope.Database.Enlisted);
            Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
            Assert.True(scope.Connection.RequiresEmulatedAmbientReceipt);
        }
    }

    /// <summary>A pre-execution callback failure and the subsequent native missing-marker cleanup error both survive.</summary>
    /// <param name="asynchronous">Whether the locking command uses asynchronous execution.</param>
    /// <param name="emulatedAmbient">Whether the caller owns an emulated ambient rather than local transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Pre_execution_failure_is_not_hidden_by_missing_marker_cleanup(
        bool asynchronous,
        bool emulatedAmbient
    )
    {
        // Arrange
        var callbackFailure = new IOException("receipt-before-execution-canary");
        using var scope = new CommandScope(new SavepointFailureInterceptor(creating: true, callbackFailure));
        using var ambient = emulatedAmbient
            ? new CommittableTransaction()
            : null;

        using var local = emulatedAmbient
            ? null
            : scope.Connection.BeginTransaction();

        if (ambient is not null)
        {
            EnlistEmulatedAmbient(scope, ambient);

            // WHY: The ambient receipt uses raw ADO commands rather than EF savepoint callbacks.
            // Fail its existing pre-execution hook before any simulated native marker is created.
            scope.Database.BeforeLocalControl = sql =>
            {
                if (sql.StartsWith("SAVEPOINT", StringComparison.Ordinal))
                {
                    throw callbackFailure;
                }
            };
        }

        var releaseFailure = CreateNativeFailure(1305);
        scope.Database.LocalControlFailure = sql => sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal)
            ? releaseFailure
            : null;

        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Collection(failures,
            failure => Assert.Same(callbackFailure, failure),
            failure => Assert.Same(releaseFailure, failure));

        Assert.Equal(emulatedAmbient
                ? 2
                : 1,
            scope.Database.LocalControlSql.Count);

        Assert.StartsWith("RELEASE SAVEPOINT", scope.Database.LocalControlSql[^1], StringComparison.Ordinal);
        Assert.Equal(scope.Database.LocalControlSql.Count, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Same(local, scope.Connection.CurrentTransaction);

        if (ambient is not null)
        {
            Assert.Same(ambient, scope.Database.Enlisted);
            Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
        }
    }

    /// <summary>A nonnative release callback failure cannot be discarded merely because the receipt reported a missing marker.</summary>
    /// <param name="asynchronous">Whether the locking command uses asynchronous execution.</param>
    /// <param name="emulatedAmbient">Whether the caller owns an emulated ambient rather than local transaction.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Release_callback_failure_is_retained_alongside_native_receipt_failure(
        bool asynchronous,
        bool emulatedAmbient
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = emulatedAmbient
            ? new CommittableTransaction()
            : null;

        using var local = emulatedAmbient
            ? null
            : scope.Connection.BeginTransaction();

        if (ambient is not null)
        {
            EnlistEmulatedAmbient(scope, ambient);
        }

        var receiptFailure = CreateNativeFailure(1305);
        var callbackFailure = new IOException("receipt-release-callback-canary");
        scope.Database.LocalControlFailure = sql => sql.StartsWith(emulatedAmbient
                ? "SAVEPOINT"
                : "ROLLBACK TO",
            StringComparison.Ordinal)
            ? receiptFailure
            : null;

        scope.Database.BeforeLocalControl = sql =>
        {
            if (sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal))
            {
                throw callbackFailure;
            }
        };

        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Collection(failures,
            failure => Assert.Same(receiptFailure, failure),
            failure => Assert.Same(callbackFailure, failure));

        Assert.Equal(emulatedAmbient
                ? 2
                : 3,
            scope.Database.LocalControlSql.Count);

        Assert.Equal(scope.Database.LocalControlSql.Count, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Same(local, scope.Connection.CurrentTransaction);

        if (ambient is not null)
        {
            Assert.Same(ambient, scope.Database.Enlisted);
            Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
        }
    }
}
