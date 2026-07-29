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
public sealed class MySqlActivityAndMeterSmokeTests
{
    [Fact]
    public void Retry_attempt_emits_span_and_increments_counter()
    {
        using var activitySink = new ActivitySink();
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.RetryAttemptsTotalMetricName);

        using (var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber: 3))
        {
            activity?.Stop();
        }

        MySqlMeter.RetryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "attempt"));

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.RetryAttemptSpanName);
        Assert.True(meterSink.TotalDelta >= 1);
    }

    [Fact]
    public void Migration_lock_acquire_emits_span_and_records_histogram()
    {
        using var activitySink = new ActivitySink();
        using var histogramSink = new HistogramSink<double>(MySqlDiagnostics.MigrationLockAcquireDurationMetricName);

        using (var activity = MySqlActivitySource.StartMigrationLockAcquire())
        {
            activity?.SetTag("db.migration.lock_name", "test_lock");
            activity?.Stop();
        }

        MySqlMeter.MigrationLockAcquireDuration.Record(0.42, new KeyValuePair<string, object?>("outcome", "acquired"));

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.MigrationLockSpanName);
        Assert.Contains(histogramSink.Measurements, m => Math.Abs(m - 0.42) < 0.0001);
    }

    [Fact]
    public void Server_version_resolve_emits_span()
    {
        using var activitySink = new ActivitySink();

        using (var activity = MySqlActivitySource.StartServerVersionResolve())
        {
            activity?.SetTag("db.serverversion.version", "8.4.0");
            activity?.Stop();
        }

        Assert.Contains(activitySink.Activities, a => a.OperationName == MySqlDiagnostics.ServerVersionResolveSpanName);
    }

    [Fact]
    public void Cancellation_counter_increments_with_path_tag()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CancellationTotalMetricName);

        MySqlMeter.CancellationTotal.Add(1, new KeyValuePair<string, object?>("path", "soft"));
        MySqlMeter.CancellationTotal.Add(1, new KeyValuePair<string, object?>("path", "hard"));

        Assert.True(meterSink.TotalDelta >= 2);
    }

    [Fact]
    public void Command_timeout_counter_increments()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommandTimeoutTotalMetricName);

        MySqlMeter.CommandTimeoutTotal.Add(1);

        Assert.True(meterSink.TotalDelta >= 1);
    }

    [Fact]
    public void Commit_unknown_counter_increments()
    {
        using var meterSink = new CounterSink<long>(MySqlDiagnostics.CommitUnknownTotalMetricName);

        MySqlMeter.CommitUnknownTotal.Add(1);

        Assert.True(meterSink.TotalDelta >= 1);
    }

    [Fact]
    public void Source_has_no_listeners_when_nothing_subscribed_keeps_start_helpers_returning_null()
    {
        var activity = MySqlActivitySource.StartRetryAttempt(attemptNumber: 0);
        Assert.Null(activity);
    }

    private sealed class ActivitySink : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivitySink()
        {
            Activities = new List<Activity>();
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MySqlDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Activities.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Activities { get; }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CounterSink<T> : IDisposable
        where T : struct
    {
        private readonly MeterListener _listener;
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
            _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
                Interlocked.Add(ref _total, measurement));
            _listener.Start();
        }

        public long TotalDelta => Interlocked.Read(ref _total);

        public void Dispose() => _listener.Dispose();
    }

    private sealed class HistogramSink<T> : IDisposable
        where T : struct
    {
        private readonly MeterListener _listener;
        private readonly List<double> _measurements = new();
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
            _listener.SetMeasurementEventCallback<double>((_, measurement, _, _) =>
            {
                lock (_lock)
                {
                    _measurements.Add(measurement);
                }
            });
            _listener.Start();
        }

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
}
