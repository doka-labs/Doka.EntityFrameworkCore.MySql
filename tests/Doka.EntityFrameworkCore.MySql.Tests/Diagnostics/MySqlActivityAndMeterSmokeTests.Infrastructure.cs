using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlActivityAndMeterSmokeTests
{
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

    private sealed class OutcomeOperation(
        bool consume
    ) : MigrationOperation
    {
        public bool Consume { get; } = consume;
    }

    private sealed class OutcomeHandler : IMySqlMigrationOperationHandler
    {
        public string HandlerId => "tests.outcome";

        public Type OperationType => typeof(OutcomeOperation);

        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (OutcomeOperation)context.Operation;

            return operation.Consume
                ? MySqlMigrationOperationResult.Consumed("consumed")
                : MySqlMigrationOperationResult.Generated(
                    [MySqlMigrationCommandSpec.Create("SELECT 1;")],
                    "generated");
        }
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
