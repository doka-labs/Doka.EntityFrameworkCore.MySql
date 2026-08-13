using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// In-process smoke coverage for the Backbone-4 diagnostic triple. Each test
/// subscribes an <see cref="ActivityListener"/> + <see cref="MeterListener"/>
/// to the provider-owned source / meter, triggers the operation that should
/// emit the span and the counter (or histogram), then asserts both surfaces
/// captured the expected entry. The tests stay free of any live DB or live
/// EF Core query pipeline; they exercise the instrumentation helpers directly
/// so the assertions stay deterministic and run in the unit-test suite.
/// </summary>
[Collection(MySqlDiagnosticsTestGroup.Name)]
public sealed class MySqlActivityAndMeterSmokeTests
{
    [Fact]
    public void Retry_attempt_emits_span_and_increments_counter()
    {
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.RetryAttemptsTotalMetricName);

        using (var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber: 3, EngineFamily.MySql))
        {
            activity?.Stop();
        }

        MySqlMeter.RetryAttemptsTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Outcome, MySqlDiagnosticTags.Attempt),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.RetryAttemptSpanName);
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Migration_lock_acquire_emits_span_and_records_histogram()
    {
        using var activitySink = new ActivitySink();
        using var histogramSink = new HistogramSink<double>(MySqlDiagnostics.MigrationLockAcquireDurationMetricName);

        using (var activity = MySqlActivitySource.StartMigrationLockAcquire(EngineFamily.MySql))
        {
            activity?.Stop();
        }

        MySqlMeter.MigrationLockAcquireDuration.Record(
            0.42,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Outcome, MySqlDiagnosticTags.Acquired),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.MigrationLockSpanName);
        Assert.Contains(histogramSink.Measurements, m => Math.Abs(m - 0.42) < 0.0001);
        AssertEngineTags(histogramSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Migration_operation_handler_emits_the_complete_diagnostic_surface()
    {
        using var activitySink = new ActivitySink();
        using var calls = new CounterSink<long>(MySqlDiagnostics.MigrationOperationHandlerCallsTotalMetricName);
        using var failures = new CounterSink<long>(MySqlDiagnostics.MigrationOperationHandlerFailuresTotalMetricName);
        using var violations = new CounterSink<long>(
            MySqlDiagnostics.MigrationOperationHandlerContractViolationsTotalMetricName);
        using var duration = new HistogramSink<double>(MySqlDiagnostics.MigrationOperationHandlerDurationMetricName);
        var tags = new TagList
        {
            { MySqlDiagnosticTags.MigrationHandlerId, "tests.handler" },
            { MySqlDiagnosticTags.MigrationOperationType, "Tests.CustomOperation" },
            { MySqlDiagnosticTags.MigrationGenerationMode, "default" },
            { MySqlDiagnosticTags.MigrationHandlerOutcome, "generated" },
            { MySqlDiagnosticTags.Engine, MySqlDiagnosticTags.MySql },
        };

        using (var activity = MySqlActivitySource.StartMigrationOperationHandler(
                   "tests.handler",
                   "Tests.CustomOperation",
                   "default",
                   EngineFamily.MySql))
        {
            activity?.SetTag(MySqlDiagnosticTags.MigrationHandlerOutcome, "generated");
        }

        MySqlMeter.MigrationOperationHandlerCallsTotal.Add(1, tags);
        MySqlMeter.MigrationOperationHandlerFailuresTotal.Add(1, tags);
        MySqlMeter.MigrationOperationHandlerContractViolationsTotal.Add(1, tags);
        MySqlMeter.MigrationOperationHandlerDuration.Record(0.01, tags);

        var recordedActivity = Assert.Single(
            activitySink.Activities,
            candidate => candidate.OperationName == MySqlDiagnostics.MigrationOperationHandlerSpanName);
        Assert.Equal("tests.handler", recordedActivity.GetTagItem(MySqlDiagnosticTags.MigrationHandlerId));
        Assert.Equal(MySqlDiagnosticTags.MySql, recordedActivity.GetTagItem(MySqlDiagnosticTags.DatabaseSystem));
        Assert.Equal(MySqlDiagnosticTags.MySql, recordedActivity.GetTagItem(MySqlDiagnosticTags.EngineFamilyName));
        Assert.True(calls.TotalDelta >= 1);
        Assert.True(failures.TotalDelta >= 1);
        Assert.True(violations.TotalDelta >= 1);
        Assert.Contains(duration.Measurements, measurement => Math.Abs(measurement - 0.01) < 0.0001);
        AssertEngineTags(calls.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Migration_operation_handler_propagates_its_terminal_outcome_to_span_and_metrics()
    {
        using var activitySink = new ActivitySink();
        using var calls = new CounterSink<long>(MySqlDiagnostics.MigrationOperationHandlerCallsTotalMetricName);
        using var duration = new HistogramSink<double>(MySqlDiagnostics.MigrationOperationHandlerDurationMetricName);
        var services = new ServiceCollection();
        services.AddScoped<IMySqlMigrationOperationHandler, OutcomeHandler>();
        services.AddEntityFrameworkDokaMySql();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = new DbContextOptionsBuilder<OutcomeContext>()
            .UseInternalServiceProvider(serviceProvider)
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                MySqlServerVersion.MySql(new Version(8, 4, 11)))
            .Options;
        using var context = new OutcomeContext(options);

        _ = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate([new OutcomeOperation()], context.Model);

        var activity = Assert.Single(
            activitySink.Activities,
            candidate => candidate.OperationName == MySqlDiagnostics.MigrationOperationHandlerSpanName
                && Equals(candidate.GetTagItem(MySqlDiagnosticTags.MigrationHandlerId), "tests.outcome"));
        Assert.Equal("qualified", activity.GetTagItem(MySqlDiagnosticTags.MigrationHandlerOutcome));
        Assert.True(calls.TotalDelta >= 1);
        Assert.Contains(
            calls.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.outcome")
                && Equals(tags[MySqlDiagnosticTags.MigrationHandlerOutcome], "qualified"));
        Assert.NotEmpty(duration.Measurements);
        Assert.Contains(
            duration.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.outcome")
                && Equals(tags[MySqlDiagnosticTags.MigrationHandlerOutcome], "qualified"));
    }

    [Fact]
    public void Migration_operation_handler_failure_keeps_plugin_payload_out_of_integrated_telemetry()
    {
        const string sensitiveMessage = "password=secret;SELECT private_data";
        const string sensitiveData = "tenant=private_tenant";
        using var activitySink = new ActivitySink();
        using var failures = new CounterSink<long>(MySqlDiagnostics.MigrationOperationHandlerFailuresTotalMetricName);
        using var duration = new HistogramSink<double>(MySqlDiagnostics.MigrationOperationHandlerDurationMetricName);
        var logSink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new TestLoggerProvider(logSink)));
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddScoped<IMySqlMigrationOperationHandler, FailureHandler>();
        services.AddEntityFrameworkDokaMySql();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = new DbContextOptionsBuilder<FailureContext>()
            .UseInternalServiceProvider(serviceProvider)
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                MySqlServerVersion.MySql(new Version(8, 4, 11)))
            .Options;
        using var context = new FailureContext(options);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() => context
            .GetService<IMigrationsSqlGenerator>()
            .Generate([new FailureOperation()], context.Model));

        Assert.Equal(MySqlMigrationHandlerFailureCode.HandlerFailed, exception.FailureCode);
        var pluginException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(sensitiveMessage, pluginException.Message);
        Assert.Equal(sensitiveData, pluginException.Data["private-context"]);

        var log = Assert.Single(
            logSink.Entries,
            entry => entry.EventId == MySqlEventId.MigrationOperationHandlerFailed);
        Assert.Null(log.ExceptionType);
        Assert.Contains(nameof(InvalidOperationException), log.Message, StringComparison.Ordinal);

        var activity = Assert.Single(
            activitySink.Activities,
            candidate => candidate.OperationName == MySqlDiagnostics.MigrationOperationHandlerSpanName
                && Equals(candidate.GetTagItem(MySqlDiagnosticTags.MigrationHandlerId), "tests.failure"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(
            nameof(MySqlMigrationHandlerFailureCode.HandlerFailed),
            activity.GetTagItem(MySqlDiagnosticTags.ErrorType));

        var failureTags = Assert.Single(
            failures.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.failure"));
        Assert.Equal(
            nameof(MySqlMigrationHandlerFailureCode.HandlerFailed),
            failureTags[MySqlDiagnosticTags.ErrorType]);
        Assert.Contains(
            duration.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.failure")
                && Equals(tags[MySqlDiagnosticTags.ErrorType], nameof(MySqlMigrationHandlerFailureCode.HandlerFailed)));

        var serializedTelemetry = string.Join(
            Environment.NewLine,
            log.Message,
            string.Join(Environment.NewLine, log.State.Select(field => $"{field.Key}={field.Value}")),
            string.Join(Environment.NewLine, activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")),
            string.Join(
                Environment.NewLine,
                activity.Events.SelectMany(activityEvent =>
                    activityEvent.Tags.Select(tag => $"{activityEvent.Name}:{tag.Key}={tag.Value}"))),
            string.Join(Environment.NewLine, activity.Baggage.Select(item => $"{item.Key}={item.Value}")),
            string.Join(
                Environment.NewLine,
                failures
                    .TagSets.Concat(duration.TagSets)
                    .SelectMany(tags => tags)
                    .Select(tag => $"{tag.Key}={tag.Value}")));
        Assert.DoesNotContain(sensitiveMessage, serializedTelemetry, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveData, serializedTelemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("private-context", serializedTelemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_version_resolve_emits_span()
    {
        using var activitySink = new ActivitySink();

        using (var activity = MySqlActivitySource.StartServerVersionResolve(EngineFamily.MySql))
        {
            activity?.Stop();
        }

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.ServerVersionResolveSpanName);
    }

    [Fact]
    public void Cancellation_counter_increments_with_path_tag()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CancellationTotalMetricName);

        MySqlMeter.CancellationTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Path, MySqlDiagnosticTags.Soft),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));
        MySqlMeter.CancellationTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Path, MySqlDiagnosticTags.Hard),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.True(meterSink.TotalDelta >= 2);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Command_timeout_counter_increments()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommandTimeoutTotalMetricName);

        MySqlMeter.CommandTimeoutTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Commit_unknown_counter_increments()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommitUnknownTotalMetricName);

        MySqlMeter.CommitUnknownTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Retry_exhaustion_emits_failure_span_and_counter()
    {
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.RetryLimitExceededTotalMetricName);
        using var rootActivity = new Activity("retry-exhaustion-smoke").Start();
        var exception = new InvalidOperationException("sensitive-message");

        using (var startedActivity = MySqlActivitySource.StartRetryLimitExceeded(EngineFamily.MySql, exception))
        {
            startedActivity?.Stop();
        }

        MySqlMeter.RetryLimitExceededTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        var recordedActivity = Assert.Single(
            activitySink.Activities,
            candidate => candidate.TraceId == rootActivity.TraceId
                && candidate.OperationName == MySqlDiagnostics.RetryLimitExceededSpanName);

        Assert.Equal(ActivityStatusCode.Error, recordedActivity.Status);
        Assert.Equal(
            exception.GetType().FullName,
            recordedActivity.GetTagItem(MySqlDiagnosticTags.ErrorType));
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Migration_lock_release_failure_emits_failure_span_and_counter()
    {
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.MigrationLockReleaseFailedTotalMetricName);
        var exception = new IOException("sensitive-message");

        using (var activity = MySqlActivitySource.StartMigrationLockReleaseFailed(EngineFamily.MySql, exception))
        {
            activity?.Stop();
        }

        MySqlMeter.MigrationLockReleaseFailedTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        Assert.Contains(
            activitySink.Activities,
            activity => activity.OperationName == MySqlDiagnostics.MigrationLockReleaseFailedSpanName
                && activity.Status == ActivityStatusCode.Error);
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Failure_operations_emit_their_named_spans()
    {
        using var activitySink = new ActivitySink();
        var exception = new InvalidOperationException("sensitive-message");

        using (MySqlActivitySource.StartCancellation(
            MySqlDiagnosticTags.Soft,
            "Open",
            EngineFamily.MySql,
            exception)) { }
        using (MySqlActivitySource.StartCommandTimeout("Open", EngineFamily.MySql, exception)) { }
        using (MySqlActivitySource.StartCommitUnknown("Broken", EngineFamily.MySql, exception)) { }

        var operationNames = activitySink.Activities
            .Select(activity => activity.OperationName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(MySqlDiagnostics.CancellationSpanName, operationNames);
        Assert.Contains(MySqlDiagnostics.CommandTimeoutSpanName, operationNames);
        Assert.Contains(MySqlDiagnostics.CommitUnknownSpanName, operationNames);
    }

    [Fact]
    public void Server_version_resolution_counter_increments_with_bounded_tags()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.ServerVersionResolutionTotalMetricName);

        MySqlMeter.ServerVersionResolutionTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Engine, MySqlDiagnosticTags.MySql),
            new KeyValuePair<string, object?>(
                MySqlDiagnosticTags.MetricSupportStatus,
                MySqlServerVersionSupportStatus.Supported.ToString()),
            new KeyValuePair<string, object?>(
                MySqlDiagnosticTags.MetricCompatibilityMode,
                MySqlServerVersionCompatibilityMode.SupportedOnly.ToString()));

        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    [Fact]
    public void Source_has_no_listeners_when_nothing_subscribed_keeps_start_helpers_returning_null()
    {
        var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber: 0, EngineFamily.MySql);
        Assert.Null(activity);
    }

    [Fact]
    public void Activity_sink_handles_concurrent_activity_completion()
    {
        const int activityCount = 256;
        using var activitySink = new ActivitySink();

        Parallel.For(0, activityCount, attemptNumber =>
        {
            using var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber, EngineFamily.MySql);
            activity?.Stop();
        });

        Assert.True(activitySink.Activities.Count >= activityCount);
    }

    private static void AssertEngineTags(
        IEnumerable<IReadOnlyDictionary<string, object?>> tagSets,
        string expectedEngine,
        string tagName = MySqlDiagnosticTags.Engine
    )
    {
        Assert.NotEmpty(tagSets);
        Assert.All(tagSets, tags => Assert.Equal(expectedEngine, tags[tagName]));
    }

    private sealed class OutcomeContext : DbContext
    {
        public OutcomeContext(
            DbContextOptions<OutcomeContext> options
        ) : base(options) { }
    }

    private sealed class OutcomeOperation : MigrationOperation;

    private sealed class OutcomeHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.outcome";

        public Type OperationType => typeof(OutcomeOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => MySqlMigrationOperationResult.Generated([MySqlMigrationCommandSpec.Create("SELECT 1;")], "qualified");
    }

    private sealed class FailureContext : DbContext
    {
        public FailureContext(
            DbContextOptions<FailureContext> options
        ) : base(options) { }
    }

    private sealed class FailureOperation : MigrationOperation;

    private sealed class FailureHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.failure";

        public Type OperationType => typeof(FailureOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var exception =
                new InvalidOperationException("password=secret;SELECT private_data")
                {
                    Data =
                    {
                        ["private-context"] = "tenant=private_tenant",
                    },
                };
            throw exception;
        }
    }

    private sealed class ActivitySink : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivitySink()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MySqlDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        // The source is process-wide, so callbacks can overlap with activities
        // emitted by parallel test collections. Concurrent capture keeps both
        // writes and assertion enumeration safe without disabling parallelism.
        public ConcurrentQueue<Activity> Activities { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CounterSink<T> : IDisposable
        where T : struct
    {
        private readonly MeterListener _listener;
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _tagSets = new();
        private long _total;

        public CounterSink(
            string instrumentName
        )
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MySqlDiagnostics.SourceName
                        && instrument.Name == instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                Interlocked.Add(ref _total, measurement);
                _tagSets.Enqueue(ToDictionary(tags));
            });
            _listener.Start();
        }

        public long TotalDelta => Interlocked.Read(ref _total);

        public IReadOnlyCollection<IReadOnlyDictionary<string, object?>> TagSets => _tagSets;

        public void Dispose() => _listener.Dispose();
    }

    private sealed class HistogramSink<T> : IDisposable
        where T : struct
    {
        private readonly MeterListener _listener;
        private readonly List<double> _measurements = new();
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _tagSets = new();
        private readonly Lock _lock = new();

        public HistogramSink(
            string instrumentName
        )
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MySqlDiagnostics.SourceName
                        && instrument.Name == instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
            {
                lock (_lock)
                {
                    _measurements.Add(measurement);
                }

                _tagSets.Enqueue(ToDictionary(tags));
            });
            _listener.Start();
        }

        public IReadOnlyCollection<IReadOnlyDictionary<string, object?>> TagSets => _tagSets;

        public IReadOnlyList<double> Measurements
        {
            get
            {
                lock (_lock)
                {
                    return _measurements.ToList();
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private static Dictionary<string, object?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    ) => tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
