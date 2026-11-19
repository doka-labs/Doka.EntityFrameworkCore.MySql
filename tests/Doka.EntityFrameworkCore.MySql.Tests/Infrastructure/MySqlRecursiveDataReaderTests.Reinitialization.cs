namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>Reinitialization owns a fresh execution reference without releasing a separate caller reference.</summary>
    /// <param name="asynchronous">Whether to dispose the reinitialized execution asynchronously.</param>
    /// <param name="callerOpen">Whether a caller-owned logical open must survive disposal.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reinitialization_after_disposal_owns_only_the_fresh_execution_reference(
        bool asynchronous,
        bool callerOpen
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        await using var reader = (MySqlRecursiveRelationalDataReader)await scope
            .Command(Policy([]))
            .ExecuteReaderAsync(scope.Parameters(null, null), cancellationToken);

        await reader.DisposeAsync();

        if (callerOpen)
        {
            await scope.Connection.OpenAsync(cancellationToken);
        }

        await scope.Connection.OpenAsync(cancellationToken);
        var command = scope.Database.CreateCommand();
        var raw = Rows(typeof(int), 17);
        var guarded = new MySqlRecursiveDataReader(raw, Policy([]));
        var commandId = Guid.NewGuid();

        // Act
        reader.Initialize(scope.Connection, command, guarded, commandId, logger: null);

        if (asynchronous)
        {
            await reader.DisposeAsync();
        }
        else
        {
            // WHY: The theory must exercise synchronous disposal independently of DisposeAsync.
            // ReSharper disable once MethodHasAsyncOverload
            reader.Dispose();
        }

        // Assert
        Assert.Equal(2, scope.Database.OpenCalls);
        Assert.Equal(callerOpen
                ? 1
                : 2,
            scope.Database.CloseCalls);

        Assert.Equal(2, scope.Database.CommandsDisposed);
        Assert.Equal(1, raw.DisposeCalls);
        Assert.Equal(callerOpen
                ? ConnectionState.Open
                : ConnectionState.Closed,
            scope.Database.State);

        Assert.Equal(commandId, reader.CommandId);
        Assert.Throws<ObjectDisposedException>(() => reader.RelationalConnection);
        Assert.Throws<ObjectDisposedException>(() => reader.DbCommand);
        Assert.Throws<ObjectDisposedException>(() => reader.DbDataReader);
    }
}
