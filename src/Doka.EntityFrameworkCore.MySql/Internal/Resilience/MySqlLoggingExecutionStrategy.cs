namespace Doka.EntityFrameworkCore.MySql;

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
        try
        {
            return _innerStrategy.Execute(state, operation, verifySucceeded);
        }
        catch (OperationCanceledException exception) when (LogCancellation(exception))
        {
            throw;
        }
        catch (RetryLimitExceededException exception) when (LogRetryLimitExceeded(exception))
        {
            throw;
        }
        catch (Exception exception) when (LogCommandTimeout(exception))
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
        try
        {
            return await _innerStrategy.ExecuteAsync(state, operation, verifySucceeded, cancellationToken);
        }
        catch (OperationCanceledException exception) when (LogCancellation(exception))
        {
            throw;
        }
        catch (RetryLimitExceededException exception) when (LogRetryLimitExceeded(exception))
        {
            throw;
        }
        catch (Exception exception) when (LogCommandTimeout(exception))
        {
            throw;
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
        OperationCanceledException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null)
        {
            return false;
        }

        var connectionState = "Unknown";
        var commandTimeout = 0;

        try
        {
            var connection = _dependencies.CurrentContext.Context.Database.GetDbConnection();
            commandTimeout = _dependencies.CurrentContext.Context.Database.GetCommandTimeout() ?? 0;
            connectionState = connection.State.ToString();

            if (connection.State is ConnectionState.Broken or ConnectionState.Closed)
            {
                MySqlLoggerMessages.HardCancellation(_logger, "Unknown", commandTimeout, connectionState);
                return false;
            }
        }
        catch
        {
            // Inside an exception filter -- must not throw. Fall through to soft cancellation
            // with safe defaults.
        }

        MySqlLoggerMessages.SoftCancellation(_logger, "Unknown", commandTimeout, connectionState);
        return false;
    }

    private bool LogCommandTimeout(
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null
            || !_transientExceptionDetector.IsCommandTimeout(exception))
        {
            return false;
        }

        var connectionState = "Unknown";
        var commandTimeout = 0;

        try
        {
            commandTimeout = _dependencies.CurrentContext.Context.Database.GetCommandTimeout() ?? 0;
            connectionState = _dependencies
                .CurrentContext.Context.Database.GetDbConnection()
                .State.ToString();
        }
        catch
        {
            // Inside an exception filter -- must not throw.
        }

        MySqlLoggerMessages.CommandTimeoutExhausted(_logger, "Unknown", commandTimeout, connectionState, exception);
        return false;
    }
}
