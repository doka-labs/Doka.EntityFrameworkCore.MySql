namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the retry execution-strategy baseline.
/// </summary>
public sealed class MySqlExecutionStrategyTests
{
    /// <summary>
    /// Verifies that retries stay opt-in and the provider switches execution strategies only when configured.
    /// </summary>
    /// <param name="enableRetry">Whether retrying execution is explicitly configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_execution_strategy_is_retrying_only_when_retry_is_enabled(
        bool enableRetry
    )
    {
        // Arrange
        using var context = new ExecutionStrategyContext(CreateOptions(enableRetry));

        // Act
        var strategy = context.Database.CreateExecutionStrategy();

        // Assert
        Assert.Equal(enableRetry, strategy.RetriesOnFailure);
        Assert.StartsWith(
            "MySql",
            strategy.GetType()
                .Name,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the retry strategy replays transient failures and surfaces retry diagnostics.
    /// </summary>
    [Fact]
    public async Task Retrying_execution_strategy_retries_transient_failures_and_logs_attempts()
    {
        // Arrange
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        await using var context =
            new ExecutionStrategyContext(CreateOptions(enableRetry: true, loggerFactory: loggerFactory));

        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;

        // Act
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

        // Assert
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
        // Arrange
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        await using var context =
            new ExecutionStrategyContext(CreateOptions(enableRetry: true, loggerFactory: loggerFactory));

        var strategy = context.Database.CreateExecutionStrategy();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            strategy.ExecuteAsync<int>(() => throw new SocketException((int)SocketError.ConnectionReset)));

        // Assert
        Assert.IsType<RetryLimitExceededException>(exception);
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
        // Arrange
        using var context = new ExecutionStrategyContext(CreateOptions(enableRetry: true));
        var dependencies = context.GetService<ExecutionStrategyDependencies>();
        var retryOptions = Assert.IsType<MySqlRetryOptions>(
            dependencies.Options.FindExtension<MySqlOptionsExtension>()?.RetryOptions);

        // Act
        var exception = Record.Exception(
            () => new MySqlExecutionStrategy(
                dependencies,
                retryOptions,
                EngineFamily.MySql,
                transientExceptionDetector: null!));

        // Assert
        Assert.Equal("transientExceptionDetector", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    /// <summary>
    /// Verifies timeout, cancellation and transport timeout classification independently.
    /// </summary>
    /// <param name="scenario">The exception category passed to the detector.</param>
    /// <param name="expectedRetry">Whether this exception is considered transient.</param>
    [Theory]
    [InlineData("timeout", false)]
    [InlineData("cancellation", false)]
    [InlineData("socket_timeout", true)]
    public void Transient_detector_classifies_failure_category(
        string scenario,
        bool expectedRetry
    )
    {
        // Arrange
        var detector = new MySqlTransientExceptionDetector();
        Exception exception = scenario switch
        {
            "timeout" => new TimeoutException("timeout"),
            "cancellation" => new OperationCanceledException("canceled"),
            "socket_timeout" => new SocketException((int)SocketError.TimedOut),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        // Act
        var shouldRetry = detector.ShouldRetryOn(exception);

        // Assert
        Assert.Equal(expectedRetry, shouldRetry);
    }

    /// <summary>
    /// Verifies that a wrapped cancellation executes the synchronous delegate
    /// once and emits the cancellation diagnostic instead of a retry attempt.
    /// </summary>
    [Fact]
    public void Retrying_execution_strategy_does_not_replay_wrapped_sync_cancellation()
    {
        // Arrange
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        using var context = new ExecutionStrategyContext(CreateOptions(enableRetry: true, loggerFactory));
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        var exception = new IOException("transport wrapper", new OperationCanceledException("cancelled operation"));

        // Act
        // WHY: cancellation must also bypass retry in the synchronous execution path.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = Record.Exception(() => strategy.Execute<int>(() =>
        {
            attempts++;
            throw exception;
        }));

        // Assert
        Assert.IsType<IOException>(actual);
        Assert.Same(exception, actual);
        Assert.Equal(1, attempts);
        Assert.Contains(
            sink.Entries,
            entry => entry.EventId.Id == MySqlEventId.HardCancellation.Id
                && entry.Category == MySqlLoggerCategory.Resilience);
        Assert.DoesNotContain(sink.Entries, entry => entry.EventId.Id == MySqlEventId.RetryAttempt.Id);
    }

    /// <summary>
    /// Verifies that a wrapped cancellation executes the asynchronous delegate
    /// once without treating its transport wrapper as a transient failure.
    /// </summary>
    [Fact]
    public async Task Retrying_execution_strategy_does_not_replay_wrapped_async_cancellation()
    {
        // Arrange
        await using var context = new ExecutionStrategyContext(CreateOptions(enableRetry: true));
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        var exception = new IOException("transport wrapper", new OperationCanceledException("cancelled operation"));

        // Act
        var actual = await Record.ExceptionAsync(() => strategy.ExecuteAsync<int>(() =>
        {
            attempts++;

            return Task.FromException<int>(exception);
        }));

        // Assert
        Assert.IsType<IOException>(actual);
        Assert.Same(exception, actual);
        Assert.Equal(1, attempts);
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
