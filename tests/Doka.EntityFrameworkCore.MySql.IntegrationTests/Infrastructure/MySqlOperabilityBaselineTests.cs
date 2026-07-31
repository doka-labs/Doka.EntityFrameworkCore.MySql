namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the operability baseline against representative live targets.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "DriverContract")]
public sealed class MySqlOperabilityBaselineTests
{
    private const string LongRunningBenchmarkSql = "SELECT BENCHMARK(50000000, SHA2('doka-phase3', 512)) AS Value";

    /// <summary>
    /// Verifies that command timeouts surface the provider timeout diagnostic without being treated as retries.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Command_timeout_logs_timeout_exhaustion()
    {
        await AssertCommandTimeoutAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 surfaces the provider timeout diagnostic without being treated as retries.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_command_timeout_logs_timeout_exhaustion()
    {
        await AssertCommandTimeoutAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that async cancellation honors the supplied token and emits a cancellation diagnostic.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Cancellation_token_honors_driver_cancellation_and_logs_cancellation_path()
    {
        await AssertCancellationAsync(IntegrationDatabaseTarget.MySql84, MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that MariaDB 11.8 honors driver cancellation and emits a cancellation diagnostic.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_cancellation_token_honors_driver_cancellation_and_logs_cancellation_path()
    {
        await AssertCancellationAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    private static async Task AssertCommandTimeoutAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var context = new OperabilityContext(CreateOptions(connectionString, serverVersion, loggerFactory));
        context.Database.SetCommandTimeout(1);
        var query = context.Database.SqlQueryRaw<int>(LongRunningBenchmarkSql);

        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => query.SingleAsync())
            .ConfigureAwait(false);
        var timeoutEntries = sink
            .Entries.Where(entry =>
                entry.EventId.Id == MySqlEventId.CommandTimeoutExhausted.Id
                && entry.Category == MySqlLoggerCategory.Resilience)
            .ToList();
        var resilienceEntries = sink
            .Entries.Where(entry => entry.Category == MySqlLoggerCategory.Resilience)
            .Select(entry => $"{entry.EventId.Id}:{entry.Message}")
            .ToList();

        Assert.Equal(MySqlErrorCode.CommandTimeoutExpired, exception.ErrorCode);
        Assert.True(
            timeoutEntries.Count == 1,
            $"Expected exactly one timeout exhaustion entry, but saw {timeoutEntries.Count}. Resilience entries: {string.Join(" | ", resilienceEntries)}");
        Assert.DoesNotContain(sink.Entries, entry => entry.EventId.Id == MySqlEventId.RetryAttempt.Id);
    }

    private static async Task AssertCancellationAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        var connectionStringBuilder = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            CancellationTimeout = 1,
        };

        await using var context = new OperabilityContext(
            CreateOptions(connectionStringBuilder.ConnectionString, serverVersion, loggerFactory));
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var query = context.Database.SqlQueryRaw<int>(LongRunningBenchmarkSql);

        await Assert
            .ThrowsAnyAsync<OperationCanceledException>(() => query.SingleAsync(cancellationTokenSource.Token))
            .ConfigureAwait(false);

        Assert.Contains(
            sink.Entries,
            entry => (entry.EventId.Id == MySqlEventId.SoftCancellation.Id
                    || entry.EventId.Id == MySqlEventId.HardCancellation.Id)
                && entry.Category == MySqlLoggerCategory.Resilience);
    }

    private static DbContextOptions<OperabilityContext> CreateOptions(
        string connectionString,
        MySqlServerVersion serverVersion,
        ILoggerFactory loggerFactory
    )
    {
        var builder = new DbContextOptionsBuilder<OperabilityContext>();

        builder.UseLoggerFactory(loggerFactory);
        builder.UseMySql(
            connectionString,
            serverVersion,
            options => options.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1)));

        return builder.Options;
    }

    private sealed class OperabilityContext : DbContext
    {
        public OperabilityContext(
            DbContextOptions<OperabilityContext> options
        ) : base(options) { }
    }
}
