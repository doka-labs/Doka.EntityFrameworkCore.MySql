namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Each typed read proves a non-XA ambient binding with one native control batch and the configured timeout.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emulated_ambient_locking_reader_uses_one_three_control_receipt(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        scope.Connection.CommandTimeout = 17;
        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        await using var reader = asynchronous
            ? await command.ExecuteReaderAsync(scope.Parameters, cancellationToken)
            : command.ExecuteReader(scope.Parameters);

        // Assert
        var control = Assert.Single(scope.Database.ControlCommands);
        AssertAmbientBatch(control.Sql);
        Assert.Equal(17, control.Timeout);
        Assert.False(control.HasTransaction);
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.True(scope.Connection.RequiresEmulatedAmbientReceipt);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(0, scope.Database.CloseCount);
    }

    /// <summary>The direct-write wrapper observes enlistment established during its own Open and probes before data.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_validates_auto_enlisted_emulated_ambient_after_open(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Database.ConnectionString += "UseXaTransactions=False;";
        using var ambient =
            new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled);

        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var affected = asynchronous
            ? await command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken)
            : command.ExecuteNonQuery(scope.Parameters);

        // Assert
        Assert.Equal(1, affected);
        AssertAmbientBatch(Assert.Single(scope.Database.ControlCommands)
            .Sql);

        Assert.Equal(2, scope.Database.NonQueryCount);
        Assert.NotNull(scope.Database.Enlisted);
        Assert.Equal(TransactionStatus.Active, scope.Database.Enlisted.TransactionInformation.Status);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Ordinary reads do not gain a receipt even on a confirmed emulated ambient connection.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_reader_does_not_probe_emulated_ambient_liveness(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        var command = scope.Command(MySqlLockingReadMode.None);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        await using var reader = asynchronous
            ? await command.ExecuteReaderAsync(scope.Parameters, cancellationToken)
            : command.ExecuteReader(scope.Parameters);

        // Assert
        Assert.Empty(scope.Database.ControlCommands);
        Assert.Equal(1, scope.Database.ReaderCount);
        Assert.True(scope.Connection.RequiresEmulatedAmbientReceipt);
    }

    /// <summary>The unchanged default XA binding needs no emulation receipt for either typed command form.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    /// <param name="write">Whether to execute an explicit direct write rather than a locking reader.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Default_XA_binding_does_not_gain_a_control_probe(
        bool asynchronous,
        bool write
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(ambient);
        var command = scope.Command(write
            ? MySqlLockingReadMode.None
            : MySqlLockingReadMode.Update);

        command.IsDirectWrite = write;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        if (write)
        {
            if (asynchronous)
            {
                await command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken);
            }
            else
            {
                command.ExecuteNonQuery(scope.Parameters);
            }
        }
        else
        {
            await using var reader = asynchronous
                ? await command.ExecuteReaderAsync(scope.Parameters, cancellationToken)
                : command.ExecuteReader(scope.Parameters);
        }

        // Assert
        Assert.Empty(scope.Database.ControlCommands);
        Assert.False(scope.Connection.RequiresEmulatedAmbientReceipt);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    /// <summary>A stale native transaction is rejected before either typed command can execute data or release a caller open reference.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    /// <param name="write">Whether the guarded command writes rather than reads.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Stale_emulated_ambient_receipt_blocks_data_and_preserves_borrowed_open_scope(
        bool asynchronous,
        bool write
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        var nativeFailure = CreateNativeFailure(1305);
        scope.Database.LocalControlFailure = sql => sql.StartsWith("SAVEPOINT", StringComparison.Ordinal)
            ? nativeFailure
            : null;

        var command = scope.Command(write
            ? MySqlLockingReadMode.None
            : MySqlLockingReadMode.Update);

        command.IsDirectWrite = write;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        Exception? exception;
        if (write)
        {
            exception = asynchronous
                ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken))
                : Record.Exception(() => command.ExecuteNonQuery(scope.Parameters));
        }
        else
        {
            exception = asynchronous
                ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
                : Record.Exception(() => command.ExecuteReader(scope.Parameters));
        }

        // Assert
        Assert.Same(nativeFailure, exception);
        Assert.Collection(scope.Database.ControlCommands,
            control => AssertAmbientBatch(control.Sql),
            control => Assert.StartsWith("RELEASE SAVEPOINT __doka_ambient_", control.Sql, StringComparison.Ordinal));

        Assert.Equal(2, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    private static void EnlistEmulatedAmbient(
        CommandScope scope,
        CommittableTransaction transaction
    )
    {
        scope.Database.ConnectionString += "UseXaTransactions=False;";
        scope.Connection.Open();
        scope.Connection.EnlistTransaction(transaction);
    }

    private static void AssertAmbientBatch(
        string sql
    )
    {
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, statements.Length);
        Assert.StartsWith("SAVEPOINT ", statements[0], StringComparison.Ordinal);
        var marker = statements[0]["SAVEPOINT ".Length..];
        Assert.StartsWith("__doka_ambient_", marker, StringComparison.Ordinal);
        Assert.Equal("ROLLBACK TO SAVEPOINT " + marker, statements[1]);
        Assert.Equal("RELEASE SAVEPOINT " + marker, statements[2]);
    }
}
