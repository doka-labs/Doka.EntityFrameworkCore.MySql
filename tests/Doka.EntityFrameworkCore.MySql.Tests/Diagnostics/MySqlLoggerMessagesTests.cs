namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests the structured diagnostic payloads and retry events emitted by
/// <c>MySqlLoggerMessages</c>.
/// </summary>
public sealed class MySqlLoggerMessagesTests
{
    // -- ServerVersionResolved disabled-logger early-return --

    /// <summary>ServerVersionResolved skips emission when the logger is disabled at Information.</summary>
    [Fact]
    public void ServerVersionResolved_with_disabled_logger_does_not_emit()
    {
        var logger = new CapturingLogger { MinLevel = LogLevel.Warning };
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        Assert.Empty(logger.Entries);
    }

    /// <summary>ServerVersionResolved emits a structured Information entry when enabled.</summary>
    [Fact]
    public void ServerVersionResolved_with_enabled_logger_emits_information_entry()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(MySqlEventId.ServerVersionResolved, entry.EventId);
        Assert.Contains("MySQL", entry.RenderedMessage);
    }

    /// <summary>ServerVersionResolved renders MariaDB engine label when given a MariaDB version.</summary>
    [Fact]
    public void ServerVersionResolved_for_mariadb_renders_mariadb_label()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        Assert.Contains("MariaDB", logger.Entries.Single().RenderedMessage);
    }

    // -- ServerVersionResolvedLogValues iteration (drives indexer + GetEnumerator) --

    /// <summary>The LogValues collection captured by the logger walks engine, version, and every capability entry.</summary>
    [Fact]
    public void ServerVersionResolved_log_values_iterate_engine_version_capabilities()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(logger.Entries.Single().State);

        Assert.Equal(4 + Enum.GetValues<ProviderCapability>().Length, state.Count);
        Assert.Equal("DatabaseEngine", state[0].Key);
        Assert.Equal("MySQL", state[0].Value);
        Assert.Equal("ServerVersion", state[1].Key);
        Assert.Equal("SupportStatus", state[2].Key);
        Assert.Equal(MySqlServerVersionSupportStatus.Supported.ToString(), state[2].Value);
        Assert.Equal("CompatibilityMode", state[3].Key);
        Assert.Equal(
            Enum.GetNames<ProviderCapability>(),
            state.Skip(4).Select(entry => entry.Key));

        var iterated = state.ToList();
        Assert.Equal(state.Count, iterated.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => state[state.Count]);
    }

    /// <summary>
    /// Verifies that an explicitly allowed legacy version emits a structured
    /// warning with its support classification.
    /// </summary>
    [Fact]
    public void UnsupportedServerVersion_emits_structured_warning()
    {
        var logger = new CapturingLogger();
        var serverVersion = MySqlServerVersion.MySql(
            new Version(8, 0, 44),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        MySqlLoggerMessages.UnsupportedServerVersion(logger, serverVersion);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(MySqlEventId.UnsupportedServerVersion, entry.EventId);
        Assert.Contains(MySqlServerVersionSupportStatus.Legacy.ToString(), entry.RenderedMessage);
        Assert.Contains(ServerVersionSupportPolicy.SupportedMatrix, entry.RenderedMessage);
    }

    // -- RetryAttempt null-delay path --

    /// <summary>RetryAttempt with null delay reports zero milliseconds without throwing.</summary>
    [Fact]
    public void RetryAttempt_with_null_delay_reports_zero_milliseconds()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.RetryAttempt(logger, attempt: 1, maxRetryCount: 3, delay: null, exception: new InvalidOperationException("test"));
        Assert.Single(logger.Entries);
    }

    /// <summary>RetryAttempt with explicit delay reports the millisecond value.</summary>
    [Fact]
    public void RetryAttempt_with_explicit_delay_emits_entry()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.RetryAttempt(
            logger,
            attempt: 2,
            maxRetryCount: 3,
            delay: TimeSpan.FromMilliseconds(250),
            exception: new InvalidOperationException("retry"));
        Assert.Single(logger.Entries);
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string RenderedMessage, object? State);

    private sealed class CapturingLogger : ILogger
    {
        public LogLevel MinLevel { get; init; } = LogLevel.Trace;

        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), state));
    }
}
