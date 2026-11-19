namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>A cancelled invocation never starts a receipt or changes the caller's ambient transaction.</summary>
    [Fact]
    public async Task Already_cancelled_emulated_ambient_receipt_does_not_create_a_command()
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(()
            => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(scope.Database.ControlCommands);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    /// <summary>An uncertain cancelled receipt releases only its unique marker with no cancelled cleanup token.</summary>
    [Fact]
    public async Task Uncertain_emulated_ambient_cancellation_releases_the_owned_marker_uncancellably()
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        using var cancellation = new CancellationTokenSource();
        var nativeCancellation = new OperationCanceledException(cancellation.Token);
        scope.Database.LocalControlFailure = sql => sql.StartsWith("SAVEPOINT", StringComparison.Ordinal)
            ? nativeCancellation
            : null;

        scope.Database.BeforeLocalControl = sql =>
        {
            if (sql.StartsWith("SAVEPOINT", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        };

        // Act
        var exception = await Record.ExceptionAsync(()
            => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellation.Token));

        // Assert
        Assert.Same(nativeCancellation, exception);
        Assert.Collection(scope.Database.ControlCommands,
            control => AssertAmbientBatch(control.Sql),
            control => Assert.StartsWith("RELEASE SAVEPOINT __doka_ambient_", control.Sql, StringComparison.Ordinal));

        Assert.Equal(CancellationToken.None, scope.Database.LastNonQueryCancellationToken);
        Assert.Equal(2, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }

    /// <summary>Cancellation observed after the complete batch cannot release its already-removed marker again.</summary>
    [Fact]
    public async Task Completed_emulated_ambient_batch_is_not_released_again_after_cancellation()
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        using var cancellation = new CancellationTokenSource();
        scope.Database.BeforeLocalControl = _ => cancellation.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(()
            => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        AssertAmbientBatch(Assert.Single(scope.Database.ControlCommands)
            .Sql);

        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.ReaderCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }
}
