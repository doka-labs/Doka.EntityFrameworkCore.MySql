namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// The provider-owned <see cref="ActivitySource"/>. Every span the provider
/// emits goes through one of the <c>Start*</c> helpers so the
/// <see cref="ActivitySource.HasListeners"/> guard runs on the hot path and
/// keeps the no-listener case allocation-free.
/// </summary>
internal static class MySqlActivitySource
{
    private static readonly ActivitySource s_source = new(
        MySqlDiagnostics.SourceName,
        typeof(MySqlActivitySource)
            .Assembly.GetName()
            .Version?.ToString()
        ?? "0.0.0");

    /// <summary>
    /// Starts the <see cref="MySqlDiagnostics.MigrationLockSpanName"/> span for
    /// a migration-lock acquire. Returns <c>null</c> when no consumer subscribes
    /// to the source so the lock hot path stays zero-cost.
    /// </summary>
    public static Activity? StartMigrationLockAcquire() => !s_source.HasListeners()
        ? null
        : s_source.StartActivity(MySqlDiagnostics.MigrationLockSpanName, ActivityKind.Client);

    /// <summary>
    /// Starts the <see cref="MySqlDiagnostics.RetryAttemptSpanName"/> span for
    /// a retry attempt. The strategy invokes this once per retry, before the
    /// inner operation runs.
    /// </summary>
    public static Activity? StartRetryAttempt(
        int attemptNumber
    )
    {
        if (!s_source.HasListeners())
        {
            return null;
        }

        var activity = s_source.StartActivity(MySqlDiagnostics.RetryAttemptSpanName, ActivityKind.Internal);
        activity?.SetTag("db.retry.attempt_number", attemptNumber);
        return activity;
    }

    /// <summary>
    /// Starts the <see cref="MySqlDiagnostics.ServerVersionResolveSpanName"/>
    /// span for the one-shot resolution of the configured server version into
    /// the runtime <see cref="ProviderProfile"/>.
    /// </summary>
    public static Activity? StartServerVersionResolve() => !s_source.HasListeners()
        ? null
        : s_source.StartActivity(MySqlDiagnostics.ServerVersionResolveSpanName, ActivityKind.Internal);
}
