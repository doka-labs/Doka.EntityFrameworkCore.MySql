namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>A connector count-conversion failure propagates without replaying the write or claiming rollback.</summary>
    [Fact]
    public async Task Direct_write_propagates_count_overflow_without_a_second_attempt()
    {
        // Arrange
        using var scope = new CommandScope();
        var overflow = new OverflowException("connector-count-conversion-canary");
        scope.Database.NonQueryFailure = overflow;
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual =
            await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken));

        // Assert
        // WHY: This is the exception-propagation seam, not a billion-row native write.
        // The driver receipt boundary is separately qualified against its actual getter.
        Assert.Same(overflow, actual);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(1, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Explicit nonquery interception keeps its native suppression/result contract.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_honors_nonquery_interception(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new DirectWriteInterceptor(suppress: true);
        using var scope = new CommandScope(interceptor);
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        var parameters = new RelationalCommandParameterObject(scope.Connection,
            null,
            null,
            scope.Context,
            scope.Context.GetService<IRelationalCommandDiagnosticsLogger>());

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await command.ExecuteNonQueryAsync(parameters, cancellationToken)
            : command.ExecuteNonQuery(parameters);

        // Assert
        Assert.Equal(37, actual);
        Assert.Equal(1, interceptor.ExecutingCount);
        Assert.Equal(0, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>Diagnostic and cleanup failures retain the original native failure together.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_preserves_failure_when_diagnostics_and_cleanup_also_fail(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new DirectWriteInterceptor(suppress: false);
        using var scope = new CommandScope(interceptor);
        var primary = new IOException("native-error-canary");
        var disposal = new IOException("dispose-error-canary");
        var close = new IOException("close-error-canary");
        scope.Database.NonQueryFailure = primary;
        scope.Database.CommandDisposeFailure = disposal;
        scope.Database.CloseFailure = close;
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        var parameters = new RelationalCommandParameterObject(scope.Connection,
            null,
            null,
            scope.Context,
            scope.Context.GetService<IRelationalCommandDiagnosticsLogger>());

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(parameters));

        scope.Database.CommandDisposeFailure = null;
        scope.Database.CloseFailure = null;

        // Assert
        var failures = Assert
            .IsType<AggregateException>(actual)
            .Flatten()
            .InnerExceptions;

        Assert.Equal(4, failures.Count);
        Assert.Contains(primary, failures);
        Assert.Contains(interceptor.DiagnosticFailure, failures);
        Assert.Contains(disposal, failures);
        Assert.Contains(close, failures);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>Current recursive depth values are checked on each cached direct command before I/O.</summary>
    /// <param name="depth">An invalid current value for the named depth parameter.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData("not-an-integer")]
    [InlineData(null)]
    public async Task Direct_write_rejects_invalid_cached_recursive_depth_before_io(
        object? depth
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        command.RecursiveWriteDepthParameters = new[] { "depth" };
        var parameters = new RelationalCommandParameterObject(scope.Connection,
            new Dictionary<string, object?> { ["depth"] = depth },
            null,
            scope.Context,
            null);

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(actual);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
    }

    /// <summary>A native direct-write error survives a simultaneous close failure, on both entry points.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_preserves_native_failure_and_close_failure(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var primary = new IOException("native-write-canary");
        var cleanup = new IOException("close-write-canary");
        scope.Database.NonQueryFailure = primary;
        scope.Database.CloseFailure = cleanup;
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(scope.Parameters));

        scope.Database.CloseFailure = null;

        // Assert
        var failures = Assert
            .IsType<AggregateException>(actual)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(primary, failures);
        Assert.Contains(cleanup, failures);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
        Assert.Equal(1, scope.Database.CommandDisposeCount);
    }

    /// <summary>Command-disposal failure cannot skip connection cleanup or hide the native write failure.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_preserves_native_dispose_and_close_failures(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var primary = new IOException("native-write-canary");
        var disposal = new IOException("dispose-write-canary");
        var close = new IOException("close-write-canary");
        scope.Database.NonQueryFailure = primary;
        scope.Database.CommandDisposeFailure = disposal;
        scope.Database.CloseFailure = close;
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(scope.Parameters));

        scope.Database.CommandDisposeFailure = null;
        scope.Database.CloseFailure = null;

        // Assert
        var failures = Assert
            .IsType<AggregateException>(actual)
            .Flatten()
            .InnerExceptions;

        Assert.Equal(3, failures.Count);
        Assert.Contains(primary, failures);
        Assert.Contains(disposal, failures);
        Assert.Contains(close, failures);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>The original exception object is rethrown when cleanup succeeds.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_retains_the_original_native_exception_identity(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var primary = new IOException("native-write-canary");
        scope.Database.NonQueryFailure = primary;
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(scope.Parameters));

        // Assert
        Assert.Same(primary, actual);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }

    /// <summary>Already canceled direct writes acquire no connection or native command.</summary>
    [Fact]
    public async Task Direct_write_early_cancellation_performs_no_io()
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;

        // Act
        var actual =
            await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(scope.Parameters, cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(actual);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, scope.Database.NonQueryCount);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
    }

    /// <summary>The exact caller token reaches the single native command.</summary>
    [Fact]
    public async Task Direct_write_passes_the_exact_cancellation_token()
    {
        // Arrange
        using var scope = new CommandScope();
        using var cancellation = new CancellationTokenSource();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;

        // Act
        var actual = await command.ExecuteNonQueryAsync(scope.Parameters, cancellation.Token);

        // Assert
        Assert.Equal(1, actual);
        Assert.Equal(cancellation.Token, scope.Database.LastNonQueryCancellationToken);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>A borrowed connection reference survives disposal of this invocation's write command.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_retains_the_callers_existing_transaction(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();

        // WHY: This test-owned connection performs no I/O; the synchronous setup pins
        // the same borrowed-reference state for both command entry points.
        // ReSharper disable once MethodHasAsyncOverload
        using var transaction = scope.Context.Database.BeginTransaction();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken)
            : command.ExecuteNonQuery(scope.Parameters);

        // Assert
        Assert.Equal(1, actual);
        Assert.Same(transaction, scope.Connection.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.CommitCount);
        Assert.Equal(0, scope.Database.RollbackCount);
    }

    /// <summary>Ordinary command templates clear the opt-in write policy in the shared command pool.</summary>
    /// <param name="providerTemplate">Whether the ordinary template comes from the provider builder.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_command_template_clears_the_direct_write_policy(
        bool providerTemplate
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        var ordinary = providerTemplate
            ? scope.Command(MySqlLockingReadMode.None)
            : new RelationalCommand(scope.Dependencies, "SELECT 2", "SELECT <redacted>", []);

        // Act
        command.PopulateFrom(ordinary);

        // Assert
        Assert.False(command.IsDirectWrite);
        Assert.Equal(ordinary.CommandText, command.CommandText);
    }

    /// <summary>Ordinary unmarked writes continue through EF's existing nonquery path.</summary>
    /// <param name="asynchronous">Whether the command uses its asynchronous entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_nonquery_keeps_existing_execution_behavior(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(MySqlLockingReadMode.None);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await command.ExecuteNonQueryAsync(scope.Parameters, cancellationToken)
            : command.ExecuteNonQuery(scope.Parameters);

        // Assert
        Assert.Equal(1, actual);
        Assert.False(command.IsDirectWrite);
        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>Records suppression and deliberately fails only the command-error diagnostic callback.</summary>
    private sealed class DirectWriteInterceptor : DbCommandInterceptor
    {
        private readonly bool _suppress;

        /// <summary>Initializes the interceptor with the behavior observed by this test.</summary>
        public DirectWriteInterceptor(
            bool suppress
        )
        {
            _suppress = suppress;
        }

        internal int ExecutingCount { get; private set; }
        internal Exception DiagnosticFailure { get; } = new IOException("diagnostic-error-canary");

        /// <inheritdoc />
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            ExecutingCount++;

            return _suppress
                ? InterceptionResult<int>.SuppressWithResult(37)
                : result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            // WHY: This deterministic fake records a callback and performs no asynchronous I/O.
            // ReSharper disable once MethodHasAsyncOverload
            var interception = NonQueryExecuting(command, eventData, result);

            return ValueTask.FromResult(interception);
        }

        /// <inheritdoc />
        public override void CommandFailed(
            DbCommand command,
            CommandErrorEventData eventData
        ) => throw DiagnosticFailure;

        /// <inheritdoc />
        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw DiagnosticFailure;
    }
}
