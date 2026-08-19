namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Proves that EF Core, the provider, and MySqlConnector expose one correlated,
/// privacy-bounded observability path against representative live engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "DriverContract")]
[Trait("Category", "ObservabilityContract")]
public sealed class MySqlCrossLayerObservabilityTests
{
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_correlates_the_complete_observability_stack()
    {
        await AssertCrossLayerContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    private static async Task AssertCrossLayerContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        using var activitySink = new CrossLayerActivitySink();
        using var meterSink = new CrossLayerMeterSink();
        using var diagnosticSink = new EfCoreDiagnosticSink();
        var logSink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(logSink)));
        var connectionStringBuilder =
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
            {
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 4,
            };

        connectionStringBuilder.Remove("Application Name");

        using var rootActivity = new Activity("doka-observability-contract").Start();

        // Server-version resolution is a one-shot singleton initialization signal.
        // Disable EF's provider cache so this test observes that boundary itself.
        await using var context = new ObservabilityContext(
            IntegrationTestDbContextOptions.Create<ObservabilityContext>()
                .EnableServiceProviderCaching(false)
                .UseLoggerFactory(loggerFactory)
                .UseMySql(connectionStringBuilder.ConnectionString, serverVersion)
                .Options);

        Assert.Equal(
            1,
            await context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync()
                .ConfigureAwait(false));

        await using (var migrationLock = await context
                         .GetService<IHistoryRepository>()
                         .AcquireDatabaseLockAsync()
                         .ConfigureAwait(false)) { }

        await context
            .Database.CloseConnectionAsync()
            .ConfigureAwait(false);
        meterSink.RecordObservableInstruments();

        var traceId = rootActivity.TraceId;
        var expectedEngine = serverVersion.IsMariaDb ? MySqlDiagnosticTags.MariaDb : MySqlDiagnosticTags.MySql;
        var providerActivities = activitySink
            .Activities.Where(activity =>
                activity.Source.Name == MySqlDiagnostics.SourceName && activity.TraceId == traceId)
            .ToList();

        var driverActivities = activitySink
            .Activities.Where(activity =>
                activity.Source.Name == MySqlDiagnostics.MySqlConnectorSourceName && activity.TraceId == traceId)
            .ToList();

        var efEvents = diagnosticSink
            .Events.Where(entry => entry.TraceId == traceId)
            .ToList();

        var resolutionLogs = logSink
            .Entries.Where(entry => entry.EventId == MySqlEventId.ServerVersionResolved)
            .ToList();

        var migrationLockLog = Assert.Single(
            logSink.Entries,
            entry => entry.EventId == MySqlEventId.MigrationLockAcquired);

        var normalizedConnectionString = new MySqlConnectionStringBuilder(
            context.Database.GetDbConnection()
                .ConnectionString);

        Assert.Contains(
            providerActivities,
            activity => activity.OperationName == MySqlDiagnostics.ServerVersionResolveSpanName);
        Assert.Contains(
            providerActivities,
            activity => activity.OperationName == MySqlDiagnostics.MigrationLockSpanName);
        Assert.All(
            providerActivities,
            activity => Assert.Equal(expectedEngine, activity.GetTagItem(MySqlDiagnosticTags.DatabaseSystem)));
        Assert.Contains(
            driverActivities,
            activity => activity.OperationName.Contains("Open", StringComparison.Ordinal));
        Assert.Contains(
            driverActivities,
            activity => activity.OperationName.Contains("Execute", StringComparison.Ordinal));
        Assert.Contains(efEvents, entry => entry.Name.Contains("Command", StringComparison.Ordinal));
        Assert.NotEmpty(resolutionLogs);
        Assert.All(resolutionLogs, log => Assert.Equal(traceId.ToString(), log.TraceId));
        Assert.All(
            resolutionLogs,
            log => Assert.Contains(providerActivities, activity => activity.SpanId.ToString() == log.SpanId));
        Assert.Contains(
            meterSink.Measurements,
            measurement => measurement.MeterName == MySqlDiagnostics.SourceName
                && measurement.InstrumentName == MySqlDiagnostics.ServerVersionResolutionTotalMetricName);
        Assert.Contains(
            meterSink.Measurements,
            measurement => measurement.MeterName == MySqlDiagnostics.SourceName
                && measurement.InstrumentName == MySqlDiagnostics.MigrationLockAcquireDurationMetricName);
        Assert.Contains(
            meterSink.Measurements,
            measurement => measurement.MeterName == MySqlDiagnostics.MySqlConnectorSourceName);
        Assert.All(
            meterSink.Measurements.Where(measurement => measurement.MeterName == MySqlDiagnostics.SourceName),
            measurement => Assert.Equal(expectedEngine, measurement.Tags[MySqlDiagnosticTags.Engine]));
        Assert.Equal(MySqlDiagnostics.DefaultDriverPoolName, normalizedConnectionString.ApplicationName);
        Assert.True(migrationLockLog.State.ContainsKey("LockScopeId"));
        Assert.DoesNotContain(normalizedConnectionString.Database, migrationLockLog.Message, StringComparison.Ordinal);
    }

    private sealed class ObservabilityContext : DbContext
    {
        public ObservabilityContext(
            DbContextOptions<ObservabilityContext> options
        ) : base(options) { }
    }

    private sealed class CrossLayerActivitySink : IDisposable
    {
        private readonly ActivityListener _listener;

        public CrossLayerActivitySink()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo =
                    source => source.Name is MySqlDiagnostics.SourceName
                        or MySqlDiagnostics.MySqlConnectorSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentQueue<Activity> Activities { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CrossLayerMeterSink : IDisposable
    {
        private readonly MeterListener _listener = new();

        public CrossLayerMeterSink()
        {
            _listener.InstrumentPublished = (
                instrument,
                listener
            ) =>
            {
                if (instrument.Meter.Name is MySqlDiagnostics.SourceName or MySqlDiagnostics.MySqlConnectorSourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<int>(RecordMeasurement);
            _listener.SetMeasurementEventCallback<long>(RecordMeasurement);
            _listener.SetMeasurementEventCallback<double>(RecordMeasurement);
            _listener.Start();
        }

        public ConcurrentQueue<MetricMeasurement> Measurements { get; } = new();

        public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

        public void Dispose() => _listener.Dispose();

        private void RecordMeasurement<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state
        )
            where T : struct => Measurements.Enqueue(
            new MetricMeasurement(
                instrument.Meter.Name,
                instrument.Name,
                tags
                    .ToArray()
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }

    private sealed record MetricMeasurement(
        string MeterName,
        string InstrumentName,
        IReadOnlyDictionary<string, object?> Tags
    );

    private sealed class EfCoreDiagnosticSink : IObserver<DiagnosticListener>, IDisposable
    {
        private readonly IDisposable _allListenersSubscription;
        private readonly ConcurrentBag<IDisposable> _listenerSubscriptions = [];

        public EfCoreDiagnosticSink()
        {
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public ConcurrentQueue<DiagnosticEvent> Events { get; } = new();

        public void OnNext(
            DiagnosticListener listener
        )
        {
            if (listener.Name == MySqlDiagnostics.EfCoreDiagnosticSourceName)
            {
                _listenerSubscriptions.Add(listener.Subscribe(new DiagnosticEventObserver(Events)));
            }
        }

        public void OnCompleted() { }

        public void OnError(
            Exception error
        )
        { }

        public void Dispose()
        {
            _allListenersSubscription.Dispose();

            foreach (var subscription in _listenerSubscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private sealed class DiagnosticEventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly ConcurrentQueue<DiagnosticEvent> _events;

        public DiagnosticEventObserver(
            ConcurrentQueue<DiagnosticEvent> events
        )
        {
            _events = events;
        }

        public void OnNext(
            KeyValuePair<string, object?> value
        ) => _events.Enqueue(new DiagnosticEvent(value.Key, Activity.Current?.TraceId));

        public void OnCompleted() { }

        public void OnError(
            Exception error
        )
        { }
    }

    private sealed record DiagnosticEvent(
        string Name,
        ActivityTraceId? TraceId
    );
}
