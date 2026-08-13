namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Centralizes the provider-owned telemetry vocabulary. Keeping tag names and
/// bounded values in one place prevents logs, traces, and metrics from drifting
/// into independently named contracts.
/// </summary>
internal static class MySqlDiagnosticTags
{
    public const string DatabaseSystem = "db.system.name";
    public const string OperationName = "db.operation.name";
    public const string ErrorType = "error.type";
    public const string RetryAttempt = "doka.mysql.retry.attempt";
    public const string CancellationPath = "doka.mysql.cancellation.path";
    public const string ConnectionState = "doka.mysql.connection.state";
    public const string EngineFamilyName = "doka.mysql.engine.family";
    public const string ServerVersion = "doka.mysql.server.version";
    public const string SupportStatus = "doka.mysql.support.status";
    public const string CompatibilityMode = "doka.mysql.compatibility.mode";
    public const string Outcome = "outcome";
    public const string Path = "path";
    public const string Engine = "engine";
    public const string MetricSupportStatus = "support_status";
    public const string MetricCompatibilityMode = "compatibility_mode";
    public const string MigrationHandlerId = "doka.mysql.migrations.handler.id";
    public const string MigrationOperationType = "doka.mysql.migrations.operation.type";
    public const string MigrationGenerationMode = "doka.mysql.migrations.generation.mode";
    public const string MigrationHandlerOutcome = "doka.mysql.migrations.handler.outcome";

    public const string MySql = "mysql";
    public const string MariaDb = "mariadb";
    public const string Acquired = "acquired";
    public const string Timeout = "timeout";
    public const string Failed = "failed";
    public const string Attempt = "attempt";
    public const string Soft = "soft";
    public const string Hard = "hard";

    public static string GetDatabaseSystem(
        EngineFamily engineFamily
    ) => engineFamily switch
    {
        EngineFamily.MySql => MySql,
        EngineFamily.MariaDb => MariaDb,
        _ => throw new ArgumentOutOfRangeException(nameof(engineFamily), engineFamily, "Unknown database engine."),
    };

    public static KeyValuePair<string, object?> CreateEngineMetricTag(
        EngineFamily engineFamily
    ) => new(Engine, GetDatabaseSystem(engineFamily));
}
