namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Presence of a managed ambient scope cannot manufacture a confirmed driver enlistment.</summary>
    /// <param name="asynchronous">Whether validation is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_emulated_enlistment_fails_before_any_control_command(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Database.ConnectionString += "UseXaTransactions=False;";
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlAmbientTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(scope.Database.ControlCommands);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
        Assert.False(scope.Connection.RequiresEmulatedAmbientReceipt);
    }

    /// <summary>A disposal failure after full native success remains visible without an unnecessary marker release.</summary>
    /// <param name="asynchronous">Whether to validate asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_emulated_receipt_preserves_disposal_failure_without_releasing_again(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        var disposalFailure = new IOException("ambient-command-dispose-canary");
        scope.Database.CommandDisposeFailure = disposalFailure;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlAmbientTransactionGuard.Validate(scope.Connection));

        // Assert
        Assert.Same(disposalFailure, exception);
        AssertAmbientBatch(Assert.Single(scope.Database.ControlCommands)
            .Sql);

        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Native execution, both owned command disposals and uncertain-marker release failures all survive.</summary>
    /// <param name="asynchronous">Whether execution and cleanup are asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Emulated_receipt_retains_native_disposal_and_marker_cleanup_failures(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var ambient = new CommittableTransaction();
        EnlistEmulatedAmbient(scope, ambient);
        var nativeFailure = CreateNativeFailure(1305);
        var disposalFailure = new IOException("ambient-dispose-canary");
        var cleanupFailure = new IOException("ambient-release-canary");
        scope.Database.CommandDisposeFailure = disposalFailure;
        scope.Database.LocalControlFailure = sql => sql.StartsWith("SAVEPOINT", StringComparison.Ordinal)
            ? nativeFailure
            : cleanupFailure;

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => MySqlAmbientTransactionGuard.ValidateAsync(scope.Connection, cancellationToken))
            : Record.Exception(() => MySqlAmbientTransactionGuard.Validate(scope.Connection));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Equal(4, failures.Count);
        Assert.Contains(nativeFailure, failures);
        Assert.Equal(2, failures.Count(failure => ReferenceEquals(failure, disposalFailure)));
        Assert.Contains(cleanupFailure, failures);
        Assert.Equal(2, scope.Database.ControlCommands.Count);
        Assert.Equal(2, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
        Assert.Equal(TransactionStatus.Active, ambient.TransactionInformation.Status);
    }
}
