namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlExecutionStrategyFactory : IExecutionStrategyFactory
{
    private readonly IMySqlTransientExceptionDetector _transientExceptionDetector;
    private readonly ExecutionStrategyDependencies _dependencies;
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlExecutionStrategyFactory(
        ExecutionStrategyDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions,
        IMySqlTransientExceptionDetector transientExceptionDetector
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        _singletonOptions = (singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions)))
            .OfType<MySqlSingletonOptions>()
            .Single();

        _transientExceptionDetector = transientExceptionDetector
            ?? throw new ArgumentNullException(nameof(transientExceptionDetector));
    }

    public IExecutionStrategy Create()
    {
        var retryOptions = _singletonOptions.RetryOptions;
        IExecutionStrategy innerStrategy = retryOptions is null
            ? new NonRetryingExecutionStrategy(_dependencies)
            : new MySqlExecutionStrategy(_dependencies, _singletonOptions, _transientExceptionDetector);

        return new MySqlLoggingExecutionStrategy(
            _dependencies,
            innerStrategy,
            retryOptions,
            _singletonOptions,
            _dependencies
                .Options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Resilience),
            _transientExceptionDetector);
    }
}
