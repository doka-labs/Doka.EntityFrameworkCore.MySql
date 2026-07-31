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
    public static Activity? StartMigrationLockAcquire(
        EngineFamily engineFamily
    ) => StartActivity(
        MySqlDiagnostics.MigrationLockSpanName,
        ActivityKind.Client,
        "GET_LOCK",
        engineFamily);

    /// <summary>
    /// Starts a bounded failure span when explicit advisory-lock release fails.
    /// </summary>
    public static Activity? StartMigrationLockReleaseFailed(
        EngineFamily engineFamily,
        Exception exception
    ) => StartFailureActivity(
        MySqlDiagnostics.MigrationLockReleaseFailedSpanName,
        "RELEASE_LOCK",
        engineFamily,
        exception);

    /// <summary>
    /// Starts the <see cref="MySqlDiagnostics.RetryAttemptSpanName"/> span for
    /// a retry attempt. The strategy invokes this once per retry, before the
    /// inner operation runs.
    /// </summary>
    public static Activity? StartRetryAttempt(
        int attemptNumber,
        EngineFamily engineFamily
    )
    {
        var activity = StartActivity(
            MySqlDiagnostics.RetryAttemptSpanName,
            ActivityKind.Internal,
            "RETRY",
            engineFamily);

        activity?.SetTag(MySqlDiagnosticTags.RetryAttempt, attemptNumber);

        return activity;
    }

    /// <summary>
    /// Starts the <see cref="MySqlDiagnostics.ServerVersionResolveSpanName"/>
    /// span for the one-shot resolution of the configured server version into
    /// the runtime <see cref="ProviderProfile"/>.
    /// </summary>
    public static Activity? StartServerVersionResolve(
        EngineFamily engineFamily
    ) => StartActivity(
        MySqlDiagnostics.ServerVersionResolveSpanName,
        ActivityKind.Internal,
        "RESOLVE_SERVER_VERSION",
        engineFamily);

    /// <summary>
    /// Starts a bounded failure span for retry-budget exhaustion.
    /// </summary>
    public static Activity? StartRetryLimitExceeded(
        EngineFamily engineFamily,
        Exception exception
    ) => StartFailureActivity(
        MySqlDiagnostics.RetryLimitExceededSpanName,
        "RETRY",
        engineFamily,
        exception);

    /// <summary>
    /// Starts a bounded failure span for a soft or hard cancellation path.
    /// </summary>
    public static Activity? StartCancellation(
        string path,
        string connectionState,
        EngineFamily engineFamily,
        Exception exception
    )
    {
        var activity = StartFailureActivity(
            MySqlDiagnostics.CancellationSpanName,
            "CANCEL",
            engineFamily,
            exception);

        activity?.SetTag(MySqlDiagnosticTags.CancellationPath, path);
        activity?.SetTag(MySqlDiagnosticTags.ConnectionState, connectionState);

        return activity;
    }

    /// <summary>
    /// Starts a bounded failure span for command-timeout exhaustion.
    /// </summary>
    public static Activity? StartCommandTimeout(
        string connectionState,
        EngineFamily engineFamily,
        Exception exception
    )
    {
        var activity = StartFailureActivity(
            MySqlDiagnostics.CommandTimeoutSpanName,
            "COMMAND",
            engineFamily,
            exception);

        activity?.SetTag(MySqlDiagnosticTags.ConnectionState, connectionState);

        return activity;
    }

    /// <summary>
    /// Starts a bounded failure span for an indeterminate transaction commit.
    /// </summary>
    public static Activity? StartCommitUnknown(
        string connectionState,
        EngineFamily engineFamily,
        Exception exception
    )
    {
        var activity = StartFailureActivity(
            MySqlDiagnostics.CommitUnknownSpanName,
            "COMMIT",
            engineFamily,
            exception);

        activity?.SetTag(MySqlDiagnosticTags.ConnectionState, connectionState);

        return activity;
    }

    /// <summary>
    /// Marks an existing provider span as failed without recording exception
    /// messages, stack traces, SQL, or connection metadata.
    /// </summary>
    public static void RecordException(
        Activity? activity,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        activity?.SetTag(MySqlDiagnosticTags.ErrorType, exception.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static Activity? StartFailureActivity(
        string spanName,
        string operationName,
        EngineFamily engineFamily,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);

        var activity = StartActivity(spanName, ActivityKind.Internal, operationName, engineFamily);
        RecordException(activity, exception);
        return activity;
    }

    private static Activity? StartActivity(
        string spanName,
        ActivityKind kind,
        string operationName,
        EngineFamily engineFamily
    )
    {
        if (!s_source.HasListeners())
        {
            return null;
        }

        var activity = s_source.StartActivity(spanName, kind);

        activity?.SetTag(MySqlDiagnosticTags.DatabaseSystem, MySqlDiagnosticTags.GetDatabaseSystem(engineFamily));
        activity?.SetTag(MySqlDiagnosticTags.OperationName, operationName);

        return activity;
    }
}
