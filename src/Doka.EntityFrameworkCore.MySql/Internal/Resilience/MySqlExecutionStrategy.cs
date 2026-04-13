namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlExecutionStrategy : ExecutionStrategy
{
    private readonly ServerCapabilities _capabilities;
    private readonly IMySqlTransientExceptionDetector _transientExceptionDetector;
    private readonly ILogger? _logger;
    private Exception? _lastException;
    private TimeSpan? _lastRetryDelay;

    public MySqlExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        MySqlSingletonOptions singletonOptions,
        IMySqlTransientExceptionDetector transientExceptionDetector
    ) : base(
        dependencies,
        singletonOptions.RetryOptions?.MaxRetryCount
        ?? throw new InvalidOperationException("Retry options must be configured for the MySQL execution strategy."),
        singletonOptions.RetryOptions.MaxRetryDelay)
    {
        _capabilities = singletonOptions.Capabilities
            ?? throw new InvalidOperationException(
                "MySQL server capabilities must be configured before creating the execution strategy.");

        _transientExceptionDetector = transientExceptionDetector
            ?? throw new ArgumentNullException(nameof(transientExceptionDetector));

        _logger = dependencies
                .Options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Resilience)
            ?? singletonOptions.ResilienceLogger;
    }

    protected override TimeSpan? GetNextDelay(
        Exception lastException
    )
    {
        ArgumentNullException.ThrowIfNull(lastException);

        var delay = base.GetNextDelay(lastException);
        _lastException = lastException;
        _lastRetryDelay = delay;

        return delay;
    }

    protected override void OnRetry()
    {
        if (_logger is not null
            && _lastException is not null)
        {
            MySqlLoggerMessages.RetryAttempt(
                _logger,
                ExceptionsEncountered.Count,
                MaxRetryCount,
                _lastRetryDelay,
                _lastException);
        }

        base.OnRetry();
    }

    protected override bool ShouldRetryOn(
        Exception exception
    ) => _transientExceptionDetector.ShouldRetryOn(exception, _capabilities);
}
