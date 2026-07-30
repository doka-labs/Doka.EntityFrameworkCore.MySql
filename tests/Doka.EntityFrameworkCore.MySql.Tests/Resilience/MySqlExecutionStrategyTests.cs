namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the retry execution-strategy baseline.
/// </summary>
public sealed class MySqlExecutionStrategyTests
{
    /// <summary>
    /// Verifies that retries stay opt-in and the provider switches execution strategies only when configured.
    /// </summary>
    [Fact]
    public void Create_execution_strategy_is_retrying_only_when_retry_is_enabled()
    {
        using var nonRetryingContext = new ExecutionStrategyContext(CreateOptions(enableRetry: false));
        using var retryingContext = new ExecutionStrategyContext(CreateOptions(enableRetry: true));

        var nonRetryingStrategy = nonRetryingContext.Database.CreateExecutionStrategy();
        var retryingStrategy = retryingContext.Database.CreateExecutionStrategy();

        Assert.False(nonRetryingStrategy.RetriesOnFailure);
        Assert.True(retryingStrategy.RetriesOnFailure);
        Assert.StartsWith(
            "MySql",
            retryingStrategy.GetType()
                .Name,
            StringComparison.Ordinal);
        Assert.True(retryingStrategy.RetriesOnFailure);
    }

    /// <summary>
    /// Verifies that the retry strategy replays transient failures and surfaces retry diagnostics.
    /// </summary>
    [Fact]
    public async Task Retrying_execution_strategy_retries_transient_failures_and_logs_attempts()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        await using var context =
            new ExecutionStrategyContext(CreateOptions(enableRetry: true, loggerFactory: loggerFactory));
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        var result = await strategy.ExecuteAsync(async () =>
        {
            attempts++;

            if (attempts < 3)
            {
                throw new SocketException((int)SocketError.TimedOut);
            }

            await Task.Yield();

            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Contains(
            sink.Entries,
            entry => entry.EventId.Id == MySqlEventId.RetryAttempt.Id
                && entry.Category == MySqlLoggerCategory.Resilience);
    }

    /// <summary>
    /// Verifies that the retry strategy logs retry exhaustion after the configured budget is consumed.
    /// </summary>
    [Fact]
    public async Task Retrying_execution_strategy_logs_retry_exhaustion()
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        await using var context =
            new ExecutionStrategyContext(CreateOptions(enableRetry: true, loggerFactory: loggerFactory));
        var strategy = context.Database.CreateExecutionStrategy();

        await Assert.ThrowsAsync<RetryLimitExceededException>(() =>
            strategy.ExecuteAsync<int>(() => throw new SocketException((int)SocketError.ConnectionReset)));

        Assert.Contains(
            sink.Entries,
            entry => entry.EventId.Id == MySqlEventId.RetryLimitExceeded.Id
                && entry.Category == MySqlLoggerCategory.Resilience);
    }

    /// <summary>
    /// Verifies that the retry strategy rejects a missing transient-failure
    /// classifier before an execution attempt can start.
    /// </summary>
    [Fact]
    public void Retrying_execution_strategy_requires_a_transient_failure_detector()
    {
        using var context = new ExecutionStrategyContext(CreateOptions(enableRetry: true));
        var dependencies = context.GetService<ExecutionStrategyDependencies>();
        var singletonOptions = context
            .GetService<IEnumerable<ISingletonOptions>>()
            .OfType<MySqlSingletonOptions>()
            .Single();

        var exception = Assert.Throws<ArgumentNullException>(
            "transientExceptionDetector",
            () => new MySqlExecutionStrategy(
                dependencies,
                singletonOptions,
                transientExceptionDetector: null!));

        Assert.Equal("transientExceptionDetector", exception.ParamName);
    }

    /// <summary>
    /// Verifies that timeout and cancellation conditions remain outside the retry classifier.
    /// </summary>
    [Fact]
    public void Transient_detector_does_not_retry_timeout_or_cancellation_paths()
    {
        var detector = new MySqlTransientExceptionDetector();

        Assert.False(detector.ShouldRetryOn(new TimeoutException("timeout")));
        Assert.False(detector.ShouldRetryOn(new OperationCanceledException("canceled")));
        Assert.True(detector.ShouldRetryOn(new SocketException((int)SocketError.TimedOut)));
    }

    private static DbContextOptions<ExecutionStrategyContext> CreateOptions(
        bool enableRetry,
        ILoggerFactory? loggerFactory = null
    )
    {
        var builder = new DbContextOptionsBuilder<ExecutionStrategyContext>();

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options =>
            {
                if (enableRetry)
                {
                    options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1));
                }
            });

        return builder.Options;
    }

    private sealed class ExecutionStrategyContext : DbContext
    {
        public ExecutionStrategyContext(
            DbContextOptions<ExecutionStrategyContext> options
        ) : base(options) { }
    }
}
