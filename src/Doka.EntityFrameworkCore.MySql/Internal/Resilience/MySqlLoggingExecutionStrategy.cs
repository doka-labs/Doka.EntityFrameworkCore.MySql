namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Wraps the inner execution strategy with structured logging for cancellation, retry
/// exhaustion, and command-timeout exhaustion. The diagnostic context (current
/// ConnectionState plus the configured CommandTimeout) is captured BEFORE the try
/// so the exception filters stay allocation-light and never need to walk the
/// DbContext.Database service surface from inside a stack-unwinding filter -- which
/// historically allocated on every retry attempt regardless of whether logging was
/// enabled.
/// </summary>
internal sealed class MySqlLoggingExecutionStrategy : IExecutionStrategy
{
    private readonly ExecutionStrategyDependencies _dependencies;
    private readonly IMySqlTransientExceptionDetector _transientExceptionDetector;
    private readonly IExecutionStrategy _innerStrategy;
    private readonly ILogger? _logger;
    private readonly int? _maxRetryCount;

    public MySqlLoggingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        IExecutionStrategy innerStrategy,
        MySqlRetryOptions? retryOptions,
        ILogger? logger,
        IMySqlTransientExceptionDetector transientExceptionDetector
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _innerStrategy = innerStrategy ?? throw new ArgumentNullException(nameof(innerStrategy));

        _maxRetryCount = retryOptions?.MaxRetryCount;
        _logger = logger;
        _transientExceptionDetector = transientExceptionDetector
            ?? throw new ArgumentNullException(nameof(transientExceptionDetector));
    }

    public bool RetriesOnFailure => _innerStrategy.RetriesOnFailure;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded
    )
    {
        var diagnostic = CaptureDiagnosticContext();

        try
        {
            return _innerStrategy.Execute(state, operation, verifySucceeded);
        }
        catch (OperationCanceledException exception) when (LogCancellation(exception, diagnostic))
        {
            throw;
        }
        catch (RetryLimitExceededException exception) when (LogRetryLimitExceeded(exception))
        {
            throw;
        }
        catch (Exception exception) when (LogCommandTimeout(exception, diagnostic))
        {
            throw;
        }
    }

    public Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default
    ) => ExecuteAsyncCore(state, operation, verifySucceeded, cancellationToken);

    private async Task<TResult> ExecuteAsyncCore<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken
    )
    {
        var diagnostic = CaptureDiagnosticContext();

        try
        {
            return await _innerStrategy.ExecuteAsync(state, operation, verifySucceeded, cancellationToken);
        }
        catch (OperationCanceledException exception) when (LogCancellation(exception, diagnostic))
        {
            throw;
        }
        catch (RetryLimitExceededException exception) when (LogRetryLimitExceeded(exception))
        {
            throw;
        }
        catch (Exception exception) when (LogCommandTimeout(exception, diagnostic))
        {
            throw;
        }
    }

    /// <summary>
    /// Captures the current DbContext-level diagnostic state once per operation so the
    /// exception filters do not have to call back into the service surface during stack
    /// unwinding. Safe under a defensive try because the DbContext may be in an early
    /// disposal state when invoked from a cancellation token's continuation.
    /// </summary>
    private DiagnosticContext CaptureDiagnosticContext()
    {
        try
        {
            var context = _dependencies.CurrentContext.Context;
            var connection = context.Database.GetDbConnection();
            var commandTimeout = context.Database.GetCommandTimeout() ?? 0;
            return new DiagnosticContext(connection.State, commandTimeout);
        }
        catch
        {
            return DiagnosticContext.Unknown;
        }
    }

    private bool LogRetryLimitExceeded(
        RetryLimitExceededException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null
            || _maxRetryCount is null)
        {
            return false;
        }

        MySqlLoggerMessages.RetryLimitExceeded(
            _logger,
            _maxRetryCount.Value + 1,
            _maxRetryCount.Value,
            exception.InnerException ?? exception);

        return false;
    }

    private bool LogCancellation(
        OperationCanceledException exception,
        DiagnosticContext diagnostic
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null)
        {
            return false;
        }

        var connectionStateName = diagnostic.ConnectionStateName;

        if (diagnostic.ConnectionState is ConnectionState.Broken or ConnectionState.Closed)
        {
            MySqlLoggerMessages.HardCancellation(_logger, "Unknown", diagnostic.CommandTimeout, connectionStateName);
            return false;
        }

        MySqlLoggerMessages.SoftCancellation(_logger, "Unknown", diagnostic.CommandTimeout, connectionStateName);
        return false;
    }

    private bool LogCommandTimeout(
        Exception exception,
        DiagnosticContext diagnostic
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null
            || !_transientExceptionDetector.IsCommandTimeout(exception))
        {
            return false;
        }

        MySqlLoggerMessages.CommandTimeoutExhausted(
            _logger,
            "Unknown",
            diagnostic.CommandTimeout,
            diagnostic.ConnectionStateName,
            exception);
        return false;
    }

    /// <summary>
    /// Per-operation diagnostic snapshot. Holds the connection state at the moment the
    /// operation began plus the configured command-timeout in seconds. The
    /// <see cref="Unknown"/> singleton is used when the snapshot cannot be captured
    /// (early-disposal of the DbContext, factory paths that bypass the service surface).
    /// </summary>
    private readonly record struct DiagnosticContext(
        ConnectionState ConnectionState,
        int CommandTimeout
    )
    {
        public static DiagnosticContext Unknown { get; } = new(ConnectionState.Closed, 0);

        public string ConnectionStateName => ConnectionState.ToString();
    }
}
