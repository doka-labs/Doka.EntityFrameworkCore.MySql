namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlExecutionStrategy : ExecutionStrategy
{
    private readonly EngineProfile _profile;
    private readonly IMySqlTransientExceptionDetector _transientExceptionDetector;
    private readonly ILogger? _logger;

    // _lastException is written by GetNextDelay (called by EF Core's retry loop on the
    // executing thread) and read by OnRetry (called on the same thread between attempts).
    // The volatile modifier hardens the publish so a future EF Core change that moves
    // OnRetry off the executing thread does not need to re-add a memory fence here.
    private volatile Exception? _lastException;

    // _lastRetryDelay carries the same write-then-read pattern but its TimeSpan struct
    // is not a legal target for the volatile field modifier. The read in OnRetry runs
    // on the same thread as the write in GetNextDelay on every EF Core retry path we
    // exercise; switching strategies would require a long-ticks-with-Interlocked variant.
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
        _profile = singletonOptions.Profile
            ?? throw new InvalidOperationException(
                "MySQL engine profile must be configured before creating the execution strategy.");

        _transientExceptionDetector = transientExceptionDetector
            ?? throw new ArgumentNullException(nameof(transientExceptionDetector));

        _logger = dependencies
            .Options.FindExtension<CoreOptionsExtension>()
            ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Resilience);
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
        var attemptNumber = ExceptionsEncountered.Count;

        if (_logger is not null
            && _lastException is not null)
        {
            MySqlLoggerMessages.RetryAttempt(
                _logger,
                attemptNumber,
                MaxRetryCount,
                _lastRetryDelay,
                _lastException);
        }

        MySqlMeter.RetryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "attempt"));

        using var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber);

        base.OnRetry();
    }

    protected override bool ShouldRetryOn(
        Exception exception
    ) => _transientExceptionDetector.ShouldRetryOn(exception);
}
