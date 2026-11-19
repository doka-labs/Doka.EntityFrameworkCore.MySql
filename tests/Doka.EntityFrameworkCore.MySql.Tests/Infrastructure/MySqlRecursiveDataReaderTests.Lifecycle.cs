namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>Reader diagnostics retain command correlation and visible read count after the recursive ownership transfer.</summary>
    /// <param name="asynchronous">Whether to dispose asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reader_diagnostics_keep_command_identity_and_visible_read_count(
        bool asynchronous
    )
    {
        // Arrange
        var observer = new DisposalObserver();
        using var scope = new CommandScope(observer);
        var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        var commandId = reader.CommandId;
        var command = reader.DbCommand;
        var visibleReader = reader.DbDataReader;
        reader.Read();

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
        Assert.NotNull(observer.Closing);
        Assert.NotNull(observer.Disposing);
        Assert.Equal(commandId, observer.Closing.CommandId);
        Assert.Equal(commandId, observer.Disposing.CommandId);
        Assert.Same(command, observer.Closing.Command);
        Assert.Same(visibleReader, observer.Disposing.DataReader);
        Assert.Equal(1, observer.Closing.ReadCount);
        Assert.Equal(1, observer.Disposing.ReadCount);
        Assert.Equal(asynchronous, observer.Closing.IsAsync);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>A diagnostic failure cannot skip independent command or connection cleanup.</summary>
    /// <param name="asynchronous">Whether to dispose asynchronously.</param>
    /// <param name="closing">Whether the throwing callback observes closing rather than disposing.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Diagnostic_failures_remain_visible_and_do_not_skip_owned_cleanup(
        bool asynchronous,
        bool closing
    )
    {
        // Arrange
        var failure = new IOException("reader-diagnostic-canary");
        var observer = new DisposalObserver
        {
            ClosingFailure = closing
                ? failure
                : null,
            DisposingFailure = closing
                ? null
                : failure,
        };

        using var scope = new CommandScope(observer);
        var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => reader
                .DisposeAsync()
                .AsTask())
            : Record.Exception(reader.Dispose);

        // Assert
        Assert.Same(failure, exception);
        Assert.NotNull(scope.Database.LastReader);
        Assert.True(scope.Database.LastReader.IsClosed);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>Closing suppression retains EF's callback contract while disposal still releases owned resources.</summary>
    /// <param name="asynchronous">Whether to close asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_suppression_does_not_close_the_native_reader_early(
        bool asynchronous
    )
    {
        // Arrange
        var observer = new DisposalObserver { SuppressClosing = true };
        using var scope = new CommandScope(observer);
        await using var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        // Act
        if (asynchronous)
        {
            await reader.CloseAsync();
        }
        else
        {
            reader.Close();
        }

        // Assert
        Assert.NotNull(observer.Closing);
        Assert.NotNull(scope.Database.LastReader);
        Assert.False(scope.Database.LastReader.IsClosed);
        Assert.Equal(0, scope.Database.CommandsDisposed);
        Assert.Equal(0, scope.Database.CloseCalls);
    }

    /// <summary>Explicit disposal suppression leaves cleanup to the interceptor, matching EF's ownership contract.</summary>
    [Fact]
    public void Disposal_suppression_retains_interceptor_owned_resources()
    {
        // Arrange
        var observer = new DisposalObserver { SuppressDisposing = true };
        using var scope = new CommandScope(observer);
        var reader = scope
            .Command(Policy([]))
            .ExecuteReader(scope.Parameters(null, null));

        var command = reader.DbCommand;

        // Act
        reader.Dispose();

        // Assert
        Assert.NotNull(observer.Disposing);
        Assert.Equal(0, scope.Database.CommandsDisposed);
        Assert.Equal(0, scope.Database.CloseCalls);
        Assert.Throws<ObjectDisposedException>(() => reader.DbCommand);
        command.Dispose();
        scope.Connection.Close();
    }

    /// <summary>A cancellation during priming or existing buffering releases every owned resource.</summary>
    /// <param name="buffered">Whether cancellation occurs after the first guarded row inside EF's existing buffer.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_read_cancellation_releases_the_recursive_owner(
        bool buffered
    )
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        scope.Database.ReaderFactory = () =>
        {
            var raw = Rows(typeof(int), 7, 8);
            raw.ReadFailure = failure;
            raw.FailReadAt = buffered
                ? 2
                : 1;

            return raw;
        };

        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null,
            buffered
                ? IntColumns()
                : null);

        // Act
        var exception = await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellation.Token));

        // Assert
        Assert.Same(failure, exception);
        Assert.NotNull(scope.Database.LastReader);
        Assert.True(scope.Database.LastReader.IsClosed);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
    }

    /// <summary>An interceptor replacement without the generated guard fails closed and is cleaned up.</summary>
    /// <param name="asynchronous">Whether execution uses the asynchronous native interceptor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_interceptor_replacement_is_rejected_before_payload_read(
        bool asynchronous
    )
    {
        // Arrange
        using var table = new DataTable();
        table.Columns.Add("replacement", typeof(int));
        table.Rows.Add(9);
        using var replacement = new ReaderProbe(table);
        var observer = new ReaderObserver { Replacement = replacement };
        using var scope = new CommandScope(observer);
        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, null);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, replacement.ReadCalls);
        Assert.True(replacement.IsClosed);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.NotNull(scope.Database.LastReader);
        scope.Database.LastReader.Dispose();
    }

    private sealed class DisposalObserver : DbCommandInterceptor
    {
        internal DataReaderClosingEventData? Closing { get; private set; }
        internal DataReaderDisposingEventData? Disposing { get; private set; }
        internal Exception? ClosingFailure { get; init; }
        internal Exception? DisposingFailure { get; init; }
        internal bool SuppressClosing { get; init; }
        internal bool SuppressDisposing { get; init; }

        public override InterceptionResult DataReaderClosing(
            DbCommand command,
            DataReaderClosingEventData eventData,
            InterceptionResult result
        )
        {
            Closing = eventData;

            if (ClosingFailure is { } failure)
            {
                throw failure;
            }

            return SuppressClosing
                ? InterceptionResult.Suppress()
                : result;
        }

        public override ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command,
            DataReaderClosingEventData eventData,
            InterceptionResult result
        )
        {
            // WHY: This diagnostic fake records synchronous state without asynchronous I/O.
            // ReSharper disable once MethodHasAsyncOverload
            return ValueTask.FromResult(DataReaderClosing(command, eventData, result));
        }

        public override InterceptionResult DataReaderDisposing(
            DbCommand command,
            DataReaderDisposingEventData eventData,
            InterceptionResult result
        )
        {
            Disposing = eventData;

            if (DisposingFailure is { } failure)
            {
                throw failure;
            }

            return SuppressDisposing
                ? InterceptionResult.Suppress()
                : result;
        }
    }
}
