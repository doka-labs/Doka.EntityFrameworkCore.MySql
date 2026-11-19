namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>A recursive execution obtains a new owner even when the cached base owner began as ordinary.</summary>
    [Fact]
    public void Ordinary_to_recursive_reuse_transfers_exactly_one_open_reference()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(null);
        var parameters = scope.Parameters(null, null);
        RelationalDataReader original;
        using (original = command.ExecuteReader(parameters)) { }

        command.PopulateFrom(scope.Command(Policy([])));

        // Act
        using var recursive = command.ExecuteReader(parameters);

        // Assert
        Assert.IsType<MySqlRecursiveRelationalDataReader>(recursive);
        Assert.NotSame(original, recursive);
        Assert.Equal(2, scope.Database.OpenCalls);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>An ordinary execution restores EF's cached base owner after recursive execution.</summary>
    [Fact]
    public void Recursive_to_ordinary_reuse_keeps_the_original_base_path()
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, null);
        using (command.ExecuteReader(parameters)) { }

        command.PopulateFrom(scope.Command(null));

        // Act
        using var ordinary = command.ExecuteReader(parameters);

        // Assert
        Assert.IsType<RelationalDataReader>(ordinary);
        Assert.Same(scope.Database.LastReader, ordinary.DbDataReader);
        Assert.NotNull(scope.Database.LastReader);
        Assert.Equal(0, scope.Database.LastReader.ReadCalls);
        Assert.Equal(2, scope.Database.OpenCalls);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>Repeated disposal cannot release a caller's earlier logical open reference.</summary>
    /// <param name="asynchronous">Whether to dispose asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_disposal_preserves_the_callers_open_reference(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Connection.Open();
        var command = scope.Command(Policy([]));
        var reader = command.ExecuteReader(scope.Parameters(null, null));
        if (asynchronous)
        {
            await reader.DisposeAsync();
        }
        else
        {
            reader.Dispose();
        }

        // Act
        if (asynchronous)
        {
            await reader.DisposeAsync();
        }
        else
        {
            reader.Dispose();
        }

        // Assert
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCalls);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Throws<ObjectDisposedException>(() => reader.DbDataReader);
    }

    /// <summary>Native close failure remains visible and is never retried by another owner disposal.</summary>
    /// <param name="asynchronous">Whether to dispose asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_close_failure_is_reported_without_a_second_reference_release(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        var closeFailure = new IOException("native-close-canary");
        scope.Database.CloseFailure = closeFailure;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader
                .DisposeAsync()
                .AsTask())
            : Record.Exception(reader.Dispose);

        var secondException = asynchronous
            ? await Record.ExceptionAsync(() => reader
                .DisposeAsync()
                .AsTask())
            : Record.Exception(reader.Dispose);

        // Assert
        Assert.Same(closeFailure, exception);
        Assert.Null(secondException);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Throws<ObjectDisposedException>(() => reader.DbCommand);
        scope.Database.CloseFailure = null;
    }

    /// <summary>Failed buffering retains its read failure together with deferred raw input disposal failure.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Buffer_read_and_input_disposal_failures_are_preserved_together(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var readFailure = new IOException("buffer-read-canary");
        var disposalFailure = new IOException("buffer-input-disposal-canary");
        scope.Database.ReaderFactory = () =>
        {
            var raw = Rows(typeof(int), 7);
            raw.ReadFailure = readFailure;
            raw.DisposeFailure = disposalFailure;
            raw.FailDisposeOnce = true;

            return raw;
        };

        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, IntColumns());
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(readFailure, failures);
        Assert.Contains(disposalFailure, failures);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>Successful buffering cannot silently discard an input-disposal failure.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Buffer_success_still_propagates_input_disposal_failure(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var disposalFailure = new IOException("buffer-success-disposal-canary");
        scope.Database.ReaderFactory = () =>
        {
            var raw = Rows(typeof(int), 7);
            raw.DisposeFailure = disposalFailure;
            raw.FailDisposeOnce = true;

            return raw;
        };

        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, IntColumns());
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        Assert.Same(disposalFailure, exception);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>A command-dispose failure does not skip the logical reference release.</summary>
    /// <param name="asynchronous">Whether to dispose asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_disposal_failure_still_attempts_connection_close(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        var commandFailure = new IOException("command-disposal-canary");
        scope.Database.CommandDisposeFailure = commandFailure;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader
                .DisposeAsync()
                .AsTask())
            : Record.Exception(reader.Dispose);

        // Assert
        Assert.Same(commandFailure, exception);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }
}
