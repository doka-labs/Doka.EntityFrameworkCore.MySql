namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>A new successful enlistment on the same physical connection cannot satisfy an earlier captured receipt.</summary>
    /// <param name="asynchronous">Whether the locking reader opens asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emulated_ambient_receipt_rejects_a_changed_managed_identity_after_native_success(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var original = new CommittableTransaction();
        using var replacement = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, original);
        var callback = typeof(MySqlRelationalConnection).GetMethod("ConnectionEnlistTransaction",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(callback);
        scope.Database.BeforeLocalControl = _ => callback.Invoke(scope.Connection, [replacement]);
        var command = scope.Command(MySqlLockingReadMode.Update);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(scope.Parameters));

        // Assert
        Assert.Contains("transaction changed",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.Ordinal);

        AssertAmbientBatch(Assert.Single(scope.Database.ControlCommands)
            .Sql);

        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Same(replacement, scope.Database.Enlisted);
        Assert.Equal(TransactionStatus.Active, original.TransactionInformation.Status);
        Assert.Equal(TransactionStatus.Active, replacement.TransactionInformation.Status);
        Assert.Equal(0, scope.Database.CloseCount);
    }

    /// <summary>Command disposal is part of the receipt scope and cannot hide a changed managed binding.</summary>
    /// <param name="asynchronous">Whether execution and command disposal are asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emulated_ambient_receipt_rechecks_identity_after_its_command_is_disposed(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var original = new CommittableTransaction();
        using var replacement = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, original);
        var callback = typeof(MySqlRelationalConnection).GetMethod("ConnectionEnlistTransaction",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(callback);
        scope.Database.AfterCommandDispose = _ => callback.Invoke(scope.Connection, [replacement]);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlAmbientTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.Contains("transaction changed",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.Ordinal);

        AssertAmbientBatch(Assert.Single(scope.Database.ControlCommands)
            .Sql);

        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Same(replacement, scope.Database.Enlisted);
        Assert.Equal(TransactionStatus.Active, original.TransactionInformation.Status);
        Assert.Equal(TransactionStatus.Active, replacement.TransactionInformation.Status);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Existing connection lifecycle invalidation clears the cached non-XA configuration with its binding.</summary>
    /// <param name="reset">Whether to reset the EF connection rather than externally close the physical connection.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emulated_ambient_configuration_is_cleared_with_its_binding(
        bool reset
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);

        // Act
        if (reset)
        {
            ((IResettableService)scope.Connection).ResetState();
        }
        else
        {
            scope.Database.Close();
            scope.Database.Open();
        }

        // Assert
        Assert.False(scope.Connection.RequiresEmulatedAmbientReceipt);
        Assert.Throws<InvalidOperationException>(scope.Connection.CaptureEmulatedAmbientReceipt);
        Assert.Empty(scope.Database.ControlCommands);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    /// <summary>A successful native receipt never becomes a cached liveness decision for the next invocation.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_emulated_ambient_receipt_uses_a_fresh_native_batch(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        if (asynchronous)
        {
            await MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken);
            await MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken);
        }
        else
        {
            MySqlAmbientTransactionGuard.Validate(scope.Connection);
            MySqlAmbientTransactionGuard.Validate(scope.Connection);
        }

        // Assert
        Assert.Equal(2, scope.Database.ControlCommands.Count);
        Assert.All(scope.Database.ControlCommands, control => AssertAmbientBatch(control.Sql));
        Assert.NotEqual(scope.Database.ControlCommands[0].Sql, scope.Database.ControlCommands[1].Sql);
        Assert.Equal(2, scope.Database.CommandDisposeCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    /// <summary>Ordinary nonquery commands retain their existing native path on an emulated ambient connection.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_nonquery_does_not_probe_emulated_ambient_liveness(
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
        var affected = asynchronous
            ? await command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken)
            : command.ExecuteNonQuery(scope.Parameters);

        // Assert
        Assert.Equal(1, affected);
        Assert.Empty(scope.Database.ControlCommands);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.True(scope.Connection.RequiresEmulatedAmbientReceipt);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }
}
