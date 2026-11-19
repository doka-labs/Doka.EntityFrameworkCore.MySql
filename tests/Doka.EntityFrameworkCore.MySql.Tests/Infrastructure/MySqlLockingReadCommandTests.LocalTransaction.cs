namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Local validation uses only an owned savepoint, its immediate rollback and its release.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_transaction_receipt_proves_liveness_without_changing_caller_ownership(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        if (asynchronous)
        {
            await MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellationToken);
        }
        else
        {
            MySqlLocalTransactionGuard.Validate(scope.Connection);
        }

        // Assert
        Assert.Collection(scope.Database.LocalControlSql,
            sql => Assert.StartsWith("SAVEPOINT", sql, StringComparison.Ordinal),
            sql => Assert.StartsWith("ROLLBACK TO", sql, StringComparison.Ordinal),
            sql => Assert.StartsWith("RELEASE SAVEPOINT", sql, StringComparison.Ordinal));

        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(0, scope.Database.CloseCount);
    }

    /// <summary>A retained marker is returned only after its local liveness proof succeeds.</summary>
    [Fact]
    public async Task Retained_marker_is_validated_without_releasing_the_callers_invocation_boundary()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var marker =
            await MySqlLocalTransactionGuard.CreateValidatedSavepointAsync(scope.Connection, cancellationToken);

        // Assert
        Assert.StartsWith("__doka_tx_", marker, StringComparison.Ordinal);
        Assert.Collection(scope.Database.LocalControlSql,
            sql => Assert.Contains(marker, sql, StringComparison.Ordinal),
            sql => Assert.Contains(marker, sql, StringComparison.Ordinal));

        Assert.StartsWith("ROLLBACK TO", scope.Database.LocalControlSql[1], StringComparison.Ordinal);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        await transaction.ReleaseSavepointAsync(marker, CancellationToken.None);
    }

    /// <summary>Repeated validations never reuse an invocation marker that could belong to an earlier command.</summary>
    [Fact]
    public void Repeated_local_receipts_use_distinct_owned_markers()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();

        // Act
        MySqlLocalTransactionGuard.Validate(scope.Connection);
        MySqlLocalTransactionGuard.Validate(scope.Connection);

        // Assert
        Assert.Equal(6, scope.Database.LocalControlSql.Count);
        Assert.NotEqual(scope.Database.LocalControlSql[0], scope.Database.LocalControlSql[3]);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>An uncertain create result still attempts release of only the newly generated marker.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_savepoint_creation_attempts_owned_marker_cleanup(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        var failure = new IOException("savepoint-create-canary");
        scope.Database.LocalControlFailure = sql => sql.StartsWith("SAVEPOINT", StringComparison.Ordinal)
            ? failure
            : null;

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlLocalTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.Same(failure, exception);
        Assert.Collection(scope.Database.LocalControlSql,
            sql => Assert.StartsWith("SAVEPOINT", sql, StringComparison.Ordinal),
            sql => Assert.StartsWith("RELEASE SAVEPOINT", sql, StringComparison.Ordinal));

        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>No transaction is invented when validation is requested without an open local association.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_local_transaction_fails_before_any_control_command(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlLocalTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(scope.Database.LocalControlSql);
        Assert.Equal(0, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>Server rollback-to rejection blocks data even when the managed transaction still has its connection.</summary>
    /// <param name="asynchronous">Whether the locking execution is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_local_transaction_receipt_blocks_the_locking_reader_and_preserves_caller_scope(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        var nativeFailure = new InvalidOperationException("server-savepoint-missing-canary");
        scope.Database.LocalControlFailure = sql => sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal)
            ? nativeFailure
            : null;

        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.Same(nativeFailure, exception);
        Assert.Equal(3, scope.Database.LocalControlSql.Count);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(0, scope.Database.CloseCount);
    }

    /// <summary>Failure to release an owned marker cannot replace the original validation failure.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_receipt_retains_primary_and_marker_cleanup_failures(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        var primary = new IOException("rollback-to-canary");
        var cleanup = new IOException("marker-release-canary");
        scope.Database.LocalControlFailure = sql => sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal)
            ? primary
            : sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal)
                ? cleanup
                : null;

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlLocalTransactionGuard.Validate(scope.Connection));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(primary, failures);
        Assert.Contains(cleanup, failures);
        Assert.Equal(3, scope.Database.LocalControlSql.Count);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Cancellation before the receipt cannot send control statements or change the local transaction.</summary>
    [Fact]
    public async Task Cancelled_local_receipt_does_not_send_control_sql()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(()
            => MySqlLocalTransactionGuard.ValidateAsync(scope.Connection, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(scope.Database.LocalControlSql);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Cancellation after savepoint creation still releases only that owned marker without a request token.</summary>
    [Fact]
    public async Task Cancelled_created_receipt_releases_its_marker_without_executing_data()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Connection.BeginTransaction();
        using var cancellation = new CancellationTokenSource();
        scope.Database.BeforeLocalControl = sql =>
        {
            if (sql.StartsWith("SAVEPOINT", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        };

        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception =
            await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Collection(scope.Database.LocalControlSql,
            sql => Assert.StartsWith("SAVEPOINT", sql, StringComparison.Ordinal),
            sql => Assert.StartsWith("RELEASE SAVEPOINT", sql, StringComparison.Ordinal));

        Assert.Equal(CancellationToken.None, scope.Database.LastNonQueryCancellationToken);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
    }
}
