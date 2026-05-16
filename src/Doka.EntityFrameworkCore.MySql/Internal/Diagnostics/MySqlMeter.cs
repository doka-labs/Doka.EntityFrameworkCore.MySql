namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// The provider-owned <see cref="Meter"/>. Holds the five OpenTelemetry-aligned
/// instruments named in D-010: migration-lock acquire duration (histogram) plus
/// four counters for retry attempts, cancellations, command-timeouts, and
/// commit-unknown outcomes. The instruments are recorded unconditionally
/// because <see cref="Meter"/> internally short-circuits on no-listener for
/// instrument writes; no per-call <c>HasListeners</c> guard is necessary.
/// </summary>
internal static class MySqlMeter
{
    private static readonly Meter s_meter = new(
        MySqlDiagnostics.SourceName,
        typeof(MySqlMeter)
            .Assembly.GetName()
            .Version?.ToString()
        ?? "0.0.0");

    /// <summary>
    /// Wall-time in seconds spent inside <c>GET_LOCK</c> per migration-lock
    /// acquire call. Recorded on both success and timeout paths so SLO
    /// dashboards can compare the two.
    /// </summary>
    public static readonly Histogram<double> MigrationLockAcquireDuration = s_meter.CreateHistogram<double>(
        MySqlDiagnostics.MigrationLockAcquireDurationMetricName,
        unit: "s",
        description: "Wall-time spent waiting for the migration advisory lock.");

    /// <summary>
    /// Counter incremented once per retry attempt the execution strategy
    /// performs. The <c>outcome</c> tag carries <c>attempt</c>.
    /// </summary>
    public static readonly Counter<long> RetryAttemptsTotal = s_meter.CreateCounter<long>(
        MySqlDiagnostics.RetryAttemptsTotalMetricName,
        unit: "{attempt}",
        description: "Retry attempts performed by the MySQL execution strategy.");

    /// <summary>
    /// Counter incremented once per command cancellation. The <c>path</c> tag
    /// carries <c>soft</c> (cooperative cancellation completed cleanly) or
    /// <c>hard</c> (connection-level escalation).
    /// </summary>
    public static readonly Counter<long> CancellationTotal = s_meter.CreateCounter<long>(
        MySqlDiagnostics.CancellationTotalMetricName,
        unit: "{cancellation}",
        description: "Command cancellations resolved by the provider.");

    /// <summary>
    /// Counter incremented once per command whose configured timeout elapsed.
    /// </summary>
    public static readonly Counter<long> CommandTimeoutTotal = s_meter.CreateCounter<long>(
        MySqlDiagnostics.CommandTimeoutTotalMetricName,
        unit: "{timeout}",
        description: "Commands whose configured timeout was exhausted.");

    /// <summary>
    /// Counter incremented once per transaction whose commit failed
    /// transiently with an unknown outcome (network or connection failure
    /// after the commit was issued but before the server acknowledgement was
    /// received).
    /// </summary>
    public static readonly Counter<long> CommitUnknownTotal = s_meter.CreateCounter<long>(
        MySqlDiagnostics.CommitUnknownTotalMetricName,
        unit: "{commit}",
        description: "Transaction commits whose outcome is unknown due to a transient failure.");
}
