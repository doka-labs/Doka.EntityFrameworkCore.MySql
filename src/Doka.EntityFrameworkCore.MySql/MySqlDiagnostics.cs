namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// The public identity surface for the provider's OpenTelemetry-style diagnostic
/// triple (logs, distributed traces, metrics). Consumers subscribe to the
/// provider-owned <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> via these constants -- the
/// names are part of the operational contract per D-010 and renaming them is a
/// breaking change for downstream dashboards and alert rules.
/// </summary>
public static class MySqlDiagnostics
{
    /// <summary>
    /// The name of the provider-owned <see cref="System.Diagnostics.ActivitySource"/>
    /// and <see cref="System.Diagnostics.Metrics.Meter"/>. OpenTelemetry consumers
    /// use this string to subscribe to provider spans and metrics:
    /// <code>builder.WithTracing(t => t.AddSource(MySqlDiagnostics.SourceName));</code>
    /// </summary>
    public const string SourceName = "Doka.EntityFrameworkCore.MySql";

    /// <summary>
    /// The name of the migration-advisory-lock acquire span. Emitted by the
    /// provider's history-repository lock implementation around the
    /// <c>GET_LOCK</c> server-side call.
    /// </summary>
    public const string MigrationLockSpanName = "db.migration.lock";

    /// <summary>
    /// The name of the retry-attempt span. Emitted around each retry the
    /// provider's execution strategy performs before the inner operation runs.
    /// </summary>
    public const string RetryAttemptSpanName = "db.retry.attempt";

    /// <summary>
    /// The name of the server-version resolve span. Emitted around the
    /// one-shot resolution of the configured <see cref="MySqlServerVersion"/>
    /// into the provider's immutable runtime capability profile.
    /// </summary>
    public const string ServerVersionResolveSpanName = "db.serverversion.resolve";

    /// <summary>
    /// The histogram name for migration-advisory-lock acquire wall-time, in
    /// seconds. Emitted once per <c>AcquireLock</c> call, regardless of
    /// success or timeout outcome.
    /// </summary>
    public const string MigrationLockAcquireDurationMetricName = "doka_mysql_migration_lock_acquire_duration_seconds";

    /// <summary>
    /// The counter name for retry attempts. Carries an <c>outcome</c> tag whose
    /// value is <c>attempt</c> for each retry the strategy performs.
    /// </summary>
    public const string RetryAttemptsTotalMetricName = "doka_mysql_retry_attempts_total";

    /// <summary>
    /// The counter name for command cancellations. Carries a <c>path</c> tag
    /// whose value is <c>soft</c> (cooperative cancellation) or <c>hard</c>
    /// (connection-level escalation).
    /// </summary>
    public const string CancellationTotalMetricName = "doka_mysql_cancellation_total";

    /// <summary>
    /// The counter name for command-timeout exhaustions. Emitted once per
    /// command whose configured timeout elapsed without a server response.
    /// </summary>
    public const string CommandTimeoutTotalMetricName = "doka_mysql_command_timeout_total";

    /// <summary>
    /// The counter name for transaction-commit failures with an unknown
    /// outcome (network or connection failure after the commit was issued but
    /// before the server acknowledgement was received).
    /// </summary>
    public const string CommitUnknownTotalMetricName = "doka_mysql_commit_unknown_total";
}
