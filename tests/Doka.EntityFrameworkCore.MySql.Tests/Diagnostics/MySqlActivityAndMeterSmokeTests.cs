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
public sealed partial class MySqlActivityAndMeterSmokeTests
{
    /// <summary>
    /// Verifies retry attempt emits span and increments counter.
    /// </summary>
    [Fact]
    public void Retry_attempt_emits_span_and_increments_counter()
    {
        // Arrange
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.RetryAttemptsTotalMetricName);

        // Act
        using (var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber: 3, EngineFamily.MySql))
        {
            activity?.Stop();
        }

        MySqlMeter.RetryAttemptsTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Outcome, MySqlDiagnosticTags.Attempt),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.RetryAttemptSpanName);
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies migration lock acquire emits span and records histogram.
    /// </summary>
    [Fact]
    public void Migration_lock_acquire_emits_span_and_records_histogram()
    {
        // Arrange
        using var activitySink = new ActivitySink();
        using var histogramSink = new HistogramSink<double>(MySqlDiagnostics.MigrationLockAcquireDurationMetricName);

        // Act
        using (var activity = MySqlActivitySource.StartMigrationLockAcquire(EngineFamily.MySql))
        {
            activity?.Stop();
        }

        MySqlMeter.MigrationLockAcquireDuration.Record(
            0.42,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Outcome, MySqlDiagnosticTags.Acquired),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.MigrationLockSpanName);
        Assert.Contains(histogramSink.Measurements, m => Math.Abs(m - 0.42) < 0.0001);
        AssertEngineTags(histogramSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies migration operation handler emits the complete diagnostic surface.
    /// </summary>
    [Fact]
    public void Migration_operation_handler_emits_the_complete_diagnostic_surface()
    {
        // Arrange
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

        // Act
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

        // Assert
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

    /// <summary>
    /// Verifies migration operation handler propagates generated and consumed outcomes to span and metrics.
    /// </summary>
    /// <param name="consumeOperation">Whether the handler consumes its operation without SQL.</param>
    /// <param name="expectedOutcome">The bounded outcome emitted across telemetry surfaces.</param>
    [Theory]
    [InlineData(false, "generated")]
    [InlineData(true, "consumed")]
    public void Migration_operation_handler_propagates_generated_and_consumed_outcomes_to_span_and_metrics(
        bool consumeOperation,
        string expectedOutcome
    )
    {
        // Arrange
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

        // Act
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate([new OutcomeOperation(consumeOperation)], context.Model);

        // Assert
        var activity = Assert.Single(
            activitySink.Activities,
            candidate => candidate.OperationName == MySqlDiagnostics.MigrationOperationHandlerSpanName
                && Equals(candidate.GetTagItem(MySqlDiagnosticTags.MigrationHandlerId), "tests.outcome"));

        Assert.Equal(expectedOutcome, activity.GetTagItem(MySqlDiagnosticTags.MigrationHandlerOutcome));
        Assert.Equal(consumeOperation ? 0 : 1, commands.Count);
        Assert.True(calls.TotalDelta >= 1);
        Assert.Contains(
            calls.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.outcome")
                && Equals(tags[MySqlDiagnosticTags.MigrationHandlerOutcome], expectedOutcome));
        Assert.NotEmpty(duration.Measurements);
        Assert.Contains(
            duration.TagSets,
            tags => Equals(tags[MySqlDiagnosticTags.MigrationHandlerId], "tests.outcome")
                && Equals(tags[MySqlDiagnosticTags.MigrationHandlerOutcome], expectedOutcome));
    }

    /// <summary>
    /// Verifies migration operation handler failure keeps plugin payload out of integrated telemetry.
    /// </summary>
    [Fact]
    public void Migration_operation_handler_failure_keeps_plugin_payload_out_of_integrated_telemetry()
    {
        // Arrange
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
        Action act = () => context
            .GetService<IMigrationsSqlGenerator>()
            .Generate([new FailureOperation()], context.Model);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<MySqlMigrationOperationHandlerException>(recordedException);
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

    /// <summary>
    /// Verifies server version resolve emits span.
    /// </summary>
    [Fact]
    public void Server_version_resolve_emits_span()
    {
        // Arrange
        using var activitySink = new ActivitySink();

        // Act
        using (var activity = MySqlActivitySource.StartServerVersionResolve(EngineFamily.MySql))
        {
            activity?.Stop();
        }

        // Assert
        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.ServerVersionResolveSpanName);
    }

    /// <summary>
    /// Verifies cancellation counter increments with path tag.
    /// </summary>
    /// <param name="path">The independently reported soft or hard cancellation path.</param>
    [Theory]
    [InlineData(MySqlDiagnosticTags.Soft)]
    [InlineData(MySqlDiagnosticTags.Hard)]
    public void Cancellation_counter_increments_with_path_tag(
        string path
    )
    {
        // Arrange
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CancellationTotalMetricName);

        // Act
        MySqlMeter.CancellationTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Path, path),
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.True(meterSink.TotalDelta >= 1);
        Assert.Contains(meterSink.TagSets, tags => Equals(tags[MySqlDiagnosticTags.Path], path));
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies command timeout counter increments.
    /// </summary>
    [Fact]
    public void Command_timeout_counter_increments()
    {
        // Arrange
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommandTimeoutTotalMetricName);

        // Act
        MySqlMeter.CommandTimeoutTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies commit unknown counter increments.
    /// </summary>
    [Fact]
    public void Commit_unknown_counter_increments()
    {
        // Arrange
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommitUnknownTotalMetricName);

        // Act
        MySqlMeter.CommitUnknownTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies retry exhaustion emits failure span and counter.
    /// </summary>
    [Fact]
    public void Retry_exhaustion_emits_failure_span_and_counter()
    {
        // Arrange
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.RetryLimitExceededTotalMetricName);
        using var rootActivity = new Activity("retry-exhaustion-smoke").Start();
        var exception = new InvalidOperationException("sensitive-message");

        // Act
        using (var startedActivity = MySqlActivitySource.StartRetryLimitExceeded(EngineFamily.MySql, exception))
        {
            startedActivity?.Stop();
        }

        MySqlMeter.RetryLimitExceededTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
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

    /// <summary>
    /// Verifies migration lock release failure emits failure span and counter.
    /// </summary>
    [Fact]
    public void Migration_lock_release_failure_emits_failure_span_and_counter()
    {
        // Arrange
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.MigrationLockReleaseFailedTotalMetricName);
        var exception = new IOException("sensitive-message");

        // Act
        using (var activity = MySqlActivitySource.StartMigrationLockReleaseFailed(EngineFamily.MySql, exception))
        {
            activity?.Stop();
        }

        MySqlMeter.MigrationLockReleaseFailedTotal.Add(
            1,
            MySqlDiagnosticTags.CreateEngineMetricTag(EngineFamily.MySql));

        // Assert
        Assert.Contains(
            activitySink.Activities,
            activity => activity.OperationName == MySqlDiagnostics.MigrationLockReleaseFailedSpanName
                && activity.Status == ActivityStatusCode.Error);
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies failure operations emit their named spans.
    /// </summary>
    /// <param name="operation">The independently exercised failure helper.</param>
    /// <param name="expectedSpanName">The span name belonging to that helper.</param>
    [Theory]
    [InlineData("cancellation", MySqlDiagnostics.CancellationSpanName)]
    [InlineData("command_timeout", MySqlDiagnostics.CommandTimeoutSpanName)]
    [InlineData("commit_unknown", MySqlDiagnostics.CommitUnknownSpanName)]
    public void Failure_operations_emit_their_named_spans(
        string operation,
        string expectedSpanName
    )
    {
        // Arrange
        using var activitySink = new ActivitySink();
        var exception = new InvalidOperationException("sensitive-message");

        // Act
        using (operation switch
        {
            "cancellation" => MySqlActivitySource.StartCancellation(
                MySqlDiagnosticTags.Soft, "Open", EngineFamily.MySql, exception),
            "command_timeout" => MySqlActivitySource.StartCommandTimeout("Open", EngineFamily.MySql, exception),
            "commit_unknown" => MySqlActivitySource.StartCommitUnknown("Broken", EngineFamily.MySql, exception),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        }) { }

        var operationNames = activitySink.Activities
            .Select(activity => activity.OperationName)
            .ToHashSet(StringComparer.Ordinal);

        // Assert
        Assert.Contains(expectedSpanName, operationNames);
    }

    /// <summary>
    /// Verifies server version resolution counter increments with bounded tags.
    /// </summary>
    [Fact]
    public void Server_version_resolution_counter_increments_with_bounded_tags()
    {
        // Arrange
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.ServerVersionResolutionTotalMetricName);

        // Act
        MySqlMeter.ServerVersionResolutionTotal.Add(
            1,
            new KeyValuePair<string, object?>(MySqlDiagnosticTags.Engine, MySqlDiagnosticTags.MySql),
            new KeyValuePair<string, object?>(
                MySqlDiagnosticTags.MetricSupportStatus,
                MySqlServerVersionSupportStatus.Supported.ToString()),
            new KeyValuePair<string, object?>(
                MySqlDiagnosticTags.MetricCompatibilityMode,
                MySqlServerVersionCompatibilityMode.SupportedOnly.ToString()));

        // Assert
        Assert.True(meterSink.TotalDelta >= 1);
        AssertEngineTags(meterSink.TagSets, MySqlDiagnosticTags.MySql);
    }

    /// <summary>
    /// Verifies source has no listeners when nothing subscribed keeps start helpers returning null.
    /// </summary>
    [Fact]
    public void Source_has_no_listeners_when_nothing_subscribed_keeps_start_helpers_returning_null()
    {
        // Arrange
        const int attemptNumber = 0;

        // Act
        var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber, EngineFamily.MySql);

        // Assert
        Assert.Null(activity);
    }

    /// <summary>
    /// Verifies activity sink handles concurrent activity completion.
    /// </summary>
    [Fact]
    public void Activity_sink_handles_concurrent_activity_completion()
    {
        // Arrange
        const int activityCount = 256;
        using var activitySink = new ActivitySink();

        // Act
        Parallel.For(0, activityCount, attemptNumber =>
        {
            using var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber, EngineFamily.MySql);
            activity?.Stop();
        });

        // Assert
        Assert.True(activitySink.Activities.Count >= activityCount);
    }
}
