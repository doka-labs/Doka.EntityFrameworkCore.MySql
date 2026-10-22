namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Injects deterministic TCP failures at the query and transaction protocol
/// boundaries, then proves provider recovery, commit reconciliation, and
/// duplicate-effect protection against representative supported engines.
/// </summary>
[IntegrationDatabaseTest]
[Collection(ProcessWideIntegrationTestGroup.Name)]
[Trait("Category", "DriverContract")]
[Trait("Category", "FaultContract")]
public sealed class MySqlNetworkFaultContractTests
{
    private const string TableName = "DokaNetworkFaultContract";

    /// <summary>Verifies mysql84 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mysql97 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb1011 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb114 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb118 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>Verifies mariadb123 satisfies the complete network fault contract.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_satisfies_the_complete_network_fault_contract()
    {
        await AssertFaultContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123))
            .ConfigureAwait(false);
    }

    private static async Task AssertFaultContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var directConnectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await ResetTableAsync(directConnectionString)
            .ConfigureAwait(false);

        try
        {
            await AssertDisconnectBeforeQueryAsync(directConnectionString, serverVersion)
                .ConfigureAwait(false);
            await AssertDisconnectDuringReadAsync(directConnectionString, serverVersion)
                .ConfigureAwait(false);
            await AssertCommitRequestLossAsync(directConnectionString, serverVersion)
                .ConfigureAwait(false);
            await AssertDisconnectBeforeCommitResponseAsync(directConnectionString, serverVersion)
                .ConfigureAwait(false);
            await AssertDisconnectAfterCommitResponseAsync(directConnectionString, serverVersion)
                .ConfigureAwait(false);
        }
        finally
        {
            await ResetTableAsync(directConnectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertDisconnectBeforeQueryAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var proxy = CreateProxy(directConnectionString);

        await using var context = CreateContext(proxy.BuildConnectionString(directConnectionString), serverVersion);

        await context
            .Database.OpenConnectionAsync()
            .ConfigureAwait(false);

        // Act
        var droppedConnectionCount = proxy.DropActiveConnections();
        var exception = await Record
            .ExceptionAsync(() => context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync())
            .ConfigureAwait(false);

        var directResult = await ExecuteScalarAsync(directConnectionString, "SELECT 1")
            .ConfigureAwait(false);

        // Assert
        Assert.True(droppedConnectionCount > 0);
        Assert.NotNull(exception);
        Assert.Equal(1, directResult);
    }

    private static async Task AssertDisconnectDuringReadAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        await using var proxy = CreateProxy(directConnectionString);
        await using var context = CreateContext(proxy.BuildConnectionString(directConnectionString), serverVersion);

        await context
            .Database.OpenConnectionAsync()
            .ConfigureAwait(false);

        await using var command = context
            .Database.GetDbConnection()
            .CreateCommand();

        command.CommandText = """
                              /* doka_stream_fault */
                              WITH RECURSIVE sequence AS (
                                  SELECT 1 AS value
                                  UNION ALL
                                  SELECT value + 1 FROM sequence WHERE value < 50
                              )
                              SELECT value, value * 2 FROM sequence
                              """;
        command.CommandTimeout = 10;
        proxy.ArmQueryResponseFault("doka_stream_fault", responsePacketsToForward: 5);

        // Act
        var exception = await Record
            .ExceptionAsync(async () =>
            {
                await using var reader = await command
                    .ExecuteReaderAsync(CommandBehavior.SequentialAccess)
                    .ConfigureAwait(false);

                while (await reader
                           .ReadAsync()
                           .ConfigureAwait(false)) { }
            })
            .ConfigureAwait(false);
        await proxy
            .WaitForQueryResponseFaultAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);

        var directResult = await ExecuteScalarAsync(directConnectionString, "SELECT 1")
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(1, directResult);
    }

    private static async Task AssertCommitRequestLossAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var operationId = $"before-request-{Guid.NewGuid():N}";
        await using var proxy = CreateProxy(directConnectionString);
        await using var context = CreateContext(proxy.BuildConnectionString(directConnectionString), serverVersion);

        await context
            .Database.OpenConnectionAsync()
            .ConfigureAwait(false);
        await using var transaction = await context
            .Database.BeginTransactionAsync()
            .ConfigureAwait(false);

        await InsertOperationAsync(context, operationId)
            .ConfigureAwait(false);
        proxy.ArmCommitFault(CommitFaultMode.BeforeRequest);

        // Act
        var exception = await Record
            .ExceptionAsync(() => transaction.CommitAsync())
            .ConfigureAwait(false);

        var faultMode = await proxy.WaitForCommitFaultAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);

        var operationCount = await CountOperationAsync(directConnectionString, operationId)
            .ConfigureAwait(false);

        // Assert
        Assert.True(
            exception is not null,
            $"COMMIT request loss did not fail. Observed queries: {string.Join(" | ", proxy.GetObservedQueries())}");
        Assert.Equal(CommitFaultMode.BeforeRequest, faultMode);
        Assert.Equal(0, operationCount);
    }

    private static async Task AssertDisconnectBeforeCommitResponseAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var operationId = $"before-response-{Guid.NewGuid():N}";
        await using var proxy = CreateProxy(directConnectionString);
        using var telemetry = new CommitUnknownTelemetrySink();
        var logSink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(logSink)));
        using var rootActivity = new Activity("doka-commit-unknown-contract").Start();
        await using var context = CreateContext(
            proxy.BuildConnectionString(directConnectionString),
            serverVersion,
            loggerFactory);

        await context
            .Database.OpenConnectionAsync()
            .ConfigureAwait(false);

        await using var transaction = await context
            .Database.BeginTransactionAsync()
            .ConfigureAwait(false);

        await InsertOperationAsync(context, operationId)
            .ConfigureAwait(false);
        proxy.ArmCommitFault(CommitFaultMode.BeforeResponse);

        // Act
        // WHY: Commit ambiguity and duplicate replay must refer to the same
        // operation ID, otherwise the at-most-once effect is not observed.
        var commitException = await Record
            .ExceptionAsync(() => transaction.CommitAsync())
            .ConfigureAwait(false);

        var faultMode = await proxy.WaitForCommitFaultAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);

        var operationCountBeforeReplay = await CountOperationAsync(directConnectionString, operationId)
            .ConfigureAwait(false);

        var duplicateFailure = await Record
            .ExceptionAsync(() => InsertOperationDirectAsync(directConnectionString, operationId))
            .ConfigureAwait(false);

        var operationCountAfterReplay = await CountOperationAsync(directConnectionString, operationId)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(commitException);
        Assert.Equal(CommitFaultMode.BeforeResponse, faultMode);

        var logEntry = Assert.Single(logSink.Entries, entry => entry.EventId == MySqlEventId.CommitUnknown);
        var activity = Assert.Single(
            telemetry.Activities,
            candidate => candidate.OperationName == MySqlDiagnostics.CommitUnknownSpanName);

        Assert.Equal(rootActivity.TraceId, activity.TraceId);
        Assert.Equal(activity.TraceId.ToString(), logEntry.TraceId);
        Assert.Equal(activity.SpanId.ToString(), logEntry.SpanId);
        Assert.True(telemetry.CommitUnknownMeasurements > 0);
        Assert.All(
            telemetry.CommitUnknownTagSets,
            tags => Assert.Equal(
                serverVersion.IsMariaDb ? MySqlDiagnosticTags.MariaDb : MySqlDiagnosticTags.MySql,
                tags[MySqlDiagnosticTags.Engine]));
        Assert.Equal(1, operationCountBeforeReplay);

        var duplicateException = Assert.IsType<MySqlException>(duplicateFailure);

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, duplicateException.ErrorCode);
        Assert.Equal(1, operationCountAfterReplay);
    }

    private static async Task AssertDisconnectAfterCommitResponseAsync(
        string directConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        // Arrange
        var operationId = $"after-response-{Guid.NewGuid():N}";
        await using var proxy = CreateProxy(directConnectionString);
        await using var context = CreateContext(proxy.BuildConnectionString(directConnectionString), serverVersion);

        await context
            .Database.OpenConnectionAsync()
            .ConfigureAwait(false);

        await using var transaction = await context
            .Database.BeginTransactionAsync()
            .ConfigureAwait(false);

        await InsertOperationAsync(context, operationId)
            .ConfigureAwait(false);
        proxy.ArmCommitFault(CommitFaultMode.AfterResponse);

        // Act
        await transaction
            .CommitAsync()
            .ConfigureAwait(false);

        var faultMode = await proxy.WaitForCommitFaultAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);

        var queryException = await Record
            .ExceptionAsync(() => context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync())
            .ConfigureAwait(false);

        var operationCount = await CountOperationAsync(directConnectionString, operationId)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(CommitFaultMode.AfterResponse, faultMode);
        Assert.NotNull(queryException);
        Assert.Equal(1, operationCount);
    }

    private static FaultContractContext CreateContext(
        string connectionString,
        MySqlServerVersion serverVersion,
        ILoggerFactory? loggerFactory = null
    )
    {
        var options = IntegrationTestDbContextOptions.Create<FaultContractContext>();

        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        options.UseMySql(connectionString, serverVersion);

        return new FaultContractContext(options.Options);
    }

    private static TcpFaultProxy CreateProxy(
        string connectionString
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        return new TcpFaultProxy(builder.Server, checked((int)builder.Port));
    }

    private static async Task InsertOperationAsync(
        DbContext context,
        string operationId
    ) => _ = await context
        .Database.ExecuteSqlRawAsync(
            $"INSERT INTO {TableName} (OperationId, EffectCount) VALUES ({{0}}, 1)",
            operationId)
        .ConfigureAwait(false);

    private static async Task InsertOperationDirectAsync(
        string connectionString,
        string operationId
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {TableName} (OperationId, EffectCount) VALUES (@operationId, 1)";
        command.Parameters.AddWithValue("@operationId", operationId);
        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task<int> CountOperationAsync(
        string connectionString,
        string operationId
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {TableName} WHERE OperationId = @operationId";
        command.Parameters.AddWithValue("@operationId", operationId);

        return Convert.ToInt32(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteScalarAsync(
        string connectionString,
        string sql
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ResetTableAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS {TableName};
                               CREATE TABLE {TableName} (
                                   OperationId varchar(64) NOT NULL PRIMARY KEY,
                                   EffectCount int NOT NULL
                               );
                               """;
        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed class FaultContractContext : DbContext
    {
        public FaultContractContext(
            DbContextOptions<FaultContractContext> options
        ) : base(options) { }
    }

    private sealed class CommitUnknownTelemetrySink : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener = new();
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> _commitUnknownTagSets = new();
        private long _commitUnknownMeasurements;

        public CommitUnknownTelemetrySink()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MySqlDiagnostics.SourceName,
                Sample = (ref _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener.InstrumentPublished = (
                instrument,
                listener
            ) =>
            {
                if (instrument.Meter.Name == MySqlDiagnostics.SourceName
                    && instrument.Name == MySqlDiagnostics.CommitUnknownTotalMetricName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((
                instrument,
                measurement,
                tags,
                state
            ) =>
            {
                Interlocked.Add(ref _commitUnknownMeasurements, measurement);
                _commitUnknownTagSets.Enqueue(
                    tags
                        .ToArray()
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            });
            _meterListener.Start();
        }

        public ConcurrentQueue<Activity> Activities { get; } = new();

        public long CommitUnknownMeasurements => Interlocked.Read(ref _commitUnknownMeasurements);

        public IReadOnlyCollection<IReadOnlyDictionary<string, object?>> CommitUnknownTagSets => _commitUnknownTagSets;

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }
}
