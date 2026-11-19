namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies command metadata, transaction proof and resource ownership without a database server.</summary>
public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>The provider builder preserves executable SQL separately from redacted log SQL.</summary>
    [Fact]
    public void Builder_preserves_sensitive_text_redaction()
    {
        // Arrange
        using var scope = new CommandScope();
        var builder = new MySqlRelationalCommandBuilder(scope.Dependencies);
        builder.Append("SELECT '");
        builder.Append("secret-canary", sensitive: true);
        builder.Append("'");

        // Act
        var command = Assert.IsType<MySqlRelationalCommand>(builder.Build());

        // Assert
        Assert.Contains("secret-canary", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-canary", command.LogCommandText, StringComparison.Ordinal);
        Assert.NotEqual(command.CommandText, command.LogCommandText);
        Assert.Equal(MySqlLockingReadMode.None, command.LockingReadMode);
    }

    /// <summary>A command rented from the connection pool receives the new provider template's mode.</summary>
    /// <param name="modeValue">The internal locking policy represented by its numeric value.</param>
    [Theory]
    [InlineData((int)MySqlLockingReadMode.Update)]
    [InlineData((int)MySqlLockingReadMode.Share)]
    [InlineData((int)MySqlLockingReadMode.NoWait)]
    [InlineData((int)MySqlLockingReadMode.SkipLocked)]
    public void Rented_command_copies_locking_metadata(
        int modeValue
    )
    {
        // Arrange
        var mode = (MySqlLockingReadMode)modeValue;
        using var scope = new CommandScope();
        var original = scope.Command(MySqlLockingReadMode.None);
        scope.Connection.ReturnCommand(original);
        var template = scope.Command(mode);

        // Act
        var rented = Assert.IsType<MySqlRelationalCommand>(scope.Connection.RentCommand());
        rented.PopulateFrom(template);

        // Assert
        Assert.Same(original, rented);
        Assert.Equal(mode, rented.LockingReadMode);
        Assert.Equal(template.CommandText, rented.CommandText);
        Assert.Equal(template.LogCommandText, rented.LogCommandText);
    }

    /// <summary>Ordinary templates explicitly clear a reused command's locking policy.</summary>
    /// <param name="providerTemplate">Whether the ordinary template is another provider command.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_template_clears_a_previously_rented_locking_mode(
        bool providerTemplate
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.NoWait);
        var ordinary = providerTemplate
            ? scope.Command(MySqlLockingReadMode.None)
            : new RelationalCommand(scope.Dependencies, "SELECT 2", "SELECT <redacted>", []);

        // Act
        command.PopulateFrom(ordinary);

        // Assert
        Assert.Equal(MySqlLockingReadMode.None, command.LockingReadMode);
        Assert.Equal(ordinary.CommandText, command.CommandText);
        Assert.Equal(ordinary.LogCommandText, command.LogCommandText);
    }

    /// <summary>Creating a DbCommand for inspection does not demand or create a transaction.</summary>
    [Fact]
    public void Command_creation_remains_available_without_a_transaction()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        using var adoCommand = command.CreateDbCommand(scope.Parameters, Guid.Empty, DbCommandMethod.ExecuteReader);

        // Assert
        Assert.Equal(command.CommandText, adoCommand.CommandText);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>Ordinary reads remain executable without transactions and keep the reader's connection alive.</summary>
    [Fact]
    public void Ordinary_reader_keeps_existing_execution_semantics()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.None);

        // Act
        using var reader = command.ExecuteReader(scope.Parameters);

        // Assert
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>A missing transaction fails before command execution and releases the extra guard scope.</summary>
    [Fact]
    public void Locking_reader_without_a_transaction_balances_open_and_close()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception = Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>The asynchronous guard failure also closes its scope without executing SQL.</summary>
    [Fact]
    public async Task Async_locking_reader_without_a_transaction_balances_open_and_close()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception =
            await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }

    /// <summary>Already-cancelled requests do not open a connection or create an implicit transaction.</summary>
    [Fact]
    public async Task Cancellation_before_open_does_not_acquire_resources()
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception =
            await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>A failed locking execution restores the ordinary-read policy for the same context.</summary>
    /// <param name="asynchronous">Whether the failed locking execution uses the asynchronous path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_locking_execution_restores_ordinary_reader_behavior(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var locking = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var guardFailure = asynchronous
            ? await Record.ExceptionAsync(() => locking.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => locking.ExecuteReader(scope.Parameters));

        var ordinary = scope.Command(MySqlLockingReadMode.None);

        // Act
        using var reader = ordinary.ExecuteReader(scope.Parameters);

        // Assert
        Assert.IsType<InvalidOperationException>(guardFailure);
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>Cancellation restores the command guard before a later ordinary asynchronous read.</summary>
    [Fact]
    public async Task Cancelled_locking_execution_restores_ordinary_async_reader_behavior()
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var locking = scope.Command(MySqlLockingReadMode.Update);
        var cancelled =
            await Record.ExceptionAsync(() => locking.ExecuteReaderAsync(scope.Parameters, cancellation.Token));

        var ordinary = scope.Command(MySqlLockingReadMode.None);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        await using var reader = await ordinary.ExecuteReaderAsync(scope.Parameters, cancellationToken);

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(cancelled);
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Null(scope.Connection.CurrentTransaction);
    }

    /// <summary>A local transaction remains owned and usable by the caller after the reader is disposed.</summary>
    [Fact]
    public void Local_transaction_survives_locking_reader_disposal()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Context.Database.BeginTransaction();
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        using (command.ExecuteReader(scope.Parameters)) { }

        // Assert
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>UseTransaction accepts the caller's live transaction on this exact connection.</summary>
    [Fact]
    public void Borrowed_transaction_survives_locking_reader_disposal()
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Database.Open();
        using var borrowed = scope.Database.BeginTransaction();
        using var transaction = scope.Context.Database.UseTransaction(borrowed);
        var command = scope.Command(MySqlLockingReadMode.Share);

        // Act
        using (command.ExecuteReader(scope.Parameters)) { }

        // Assert
        Assert.Same(borrowed, transaction!.GetDbTransaction());
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>A borrowed transaction ended directly by its owner cannot satisfy the live-transaction guard.</summary>
    [Fact]
    public void Ended_borrowed_transaction_is_rejected_before_reader_execution()
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Database.Open();
        using var borrowed = scope.Database.BeginTransaction();
        using var transaction = scope.Context.Database.UseTransaction(borrowed);
        borrowed.Commit();
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception = Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(1, scope.Database.CommitCount);
    }

    /// <summary>Cancellation after opening does not consume the caller's transaction scope.</summary>
    [Fact]
    public async Task Cancellation_during_execution_retains_caller_transaction()
    {
        // Arrange
        using var scope = new CommandScope();
        using var transaction = scope.Context.Database.BeginTransaction();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var cancellation = new OperationCanceledException(cancellationToken);
        scope.Database.ReaderFailure = cancellation;
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception =
            await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken));

        // Assert
        Assert.Same(cancellation, exception);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
    }

    /// <summary>A guard failure and a failing physical close both remain available to the caller.</summary>
    [Fact]
    public void Guard_and_close_failures_are_preserved_together()
    {
        // Arrange
        using var scope = new CommandScope();
        var closeFailure = new IOException("close-canary");
        scope.Database.CloseFailure = closeFailure;
        var command = scope.Command(MySqlLockingReadMode.Update);

        // Act
        var exception = Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        var aggregate = Assert.IsType<AggregateException>(exception);
        Assert.IsType<InvalidOperationException>(aggregate.InnerExceptions[0]);
        Assert.Same(closeFailure, aggregate.InnerExceptions[1]);
        Assert.Equal(0, scope.Database.ReaderCount);
        scope.Database.CloseFailure = null;
    }
}
