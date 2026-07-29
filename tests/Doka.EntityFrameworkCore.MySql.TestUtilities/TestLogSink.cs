using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Thread-safe log sink for capturing log entries in tests.
/// </summary>
public sealed class TestLogSink
{
    public ConcurrentQueue<TestLogEntry> Entries { get; } = new();
}

/// <summary>
/// Immutable record representing a captured log entry.
/// </summary>
public sealed record TestLogEntry(
    string Category,
    LogLevel LogLevel,
    EventId EventId,
    string Message
);

/// <summary>
/// Logger provider that routes all log output to a <see cref="TestLogSink"/>.
/// </summary>
public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly TestLogSink _sink;

    public TestLoggerProvider(
        TestLogSink sink
    )
    {
        _sink = sink;
    }

    public ILogger CreateLogger(
        string categoryName
    ) => new TestLogger(categoryName, _sink);

    public void Dispose() { }
}

/// <summary>
/// Logger implementation that enqueues all log entries into a <see cref="TestLogSink"/>.
/// </summary>
public sealed class TestLogger : ILogger
{
    private readonly string _categoryName;
    private readonly TestLogSink _sink;

    public TestLogger(
        string categoryName,
        TestLogSink sink
    )
    {
        _categoryName = categoryName;
        _sink = sink;
    }

    public IDisposable BeginScope<TState>(
        TState state
    )
        where TState : notnull => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public bool IsEnabled(
        LogLevel logLevel
    ) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => _sink.Entries.Enqueue(new TestLogEntry(_categoryName, logLevel, eventId, formatter(state, exception)));
}
