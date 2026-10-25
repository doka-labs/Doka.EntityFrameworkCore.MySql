namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlExecutionStrategyFactory : IExecutionStrategyFactory
{
    private readonly IMySqlTransientExceptionDetector _transientExceptionDetector;
    private readonly ExecutionStrategyDependencies _dependencies;

    public MySqlExecutionStrategyFactory(
        ExecutionStrategyDependencies dependencies,
        IMySqlTransientExceptionDetector transientExceptionDetector
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _transientExceptionDetector = transientExceptionDetector
            ?? throw new ArgumentNullException(nameof(transientExceptionDetector));
    }

    public IExecutionStrategy Create()
    {
        var extension = _dependencies.Options.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");


        var serverVersion = extension.ServerVersion
            ?? throw new InvalidOperationException("A MySQL server version must be configured.");

        var retryOptions = extension.RetryOptions;
        IExecutionStrategy innerStrategy = retryOptions is null
            ? new NonRetryingExecutionStrategy(_dependencies)
            : new MySqlExecutionStrategy(
                _dependencies,
                retryOptions,
                serverVersion.Profile.Engine.Family,
                _transientExceptionDetector);

        return new MySqlLoggingExecutionStrategy(
            _dependencies,
            innerStrategy,
            retryOptions,
            serverVersion.Profile.Engine.Family,
            _dependencies
                .Options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Resilience),
            _transientExceptionDetector);
    }
}
