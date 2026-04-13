namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlLoggerMessages
{
    private static readonly Action<ILogger, string, string, string, Exception?> s_invalidConfiguration =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            MySqlEventId.InvalidConfiguration,
            "{Message} ConnectionPath={ConnectionPath} RedactedConnectionString={RedactedConnectionString}");

    private static readonly Action<ILogger, string, string, string, Exception?> s_serverVersionResolved =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            MySqlEventId.ServerVersionResolved,
            "Resolved {DatabaseEngine} server version {ServerVersion}. Capabilities={Capabilities}");

    private static readonly Action<ILogger, string, Exception?> s_schemaUnsupported =
        LoggerMessage.Define<string>(LogLevel.Error, MySqlEventId.SchemaUnsupported, "{Message}");

    private static readonly Action<ILogger, string, Exception?> s_keyOrIndexMaxLengthRequired =
        LoggerMessage.Define<string>(LogLevel.Error, MySqlEventId.KeyOrIndexMaxLengthRequired, "{Message}");

    private static readonly Action<ILogger, string, Exception?> s_implicitDecimalPrecisionDefaulted =
        LoggerMessage.Define<string>(LogLevel.Warning, MySqlEventId.ImplicitDecimalPrecisionDefaulted, "{Message}");

    private static readonly Action<ILogger, int, int, double, string, Exception?> s_retryAttempt =
        LoggerMessage.Define<int, int, double, string>(
            LogLevel.Warning,
            MySqlEventId.RetryAttempt,
            "Retrying transient MySQL operation. Attempt={Attempt} MaxRetryCount={MaxRetryCount} DelayMs={DelayMs} ExceptionType={ExceptionType}");

    private static readonly Action<ILogger, int, int, string, Exception?> s_retryLimitExceeded =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Error,
            MySqlEventId.RetryLimitExceeded,
            "MySQL retry budget exhausted. Attempts={Attempts} MaxRetryCount={MaxRetryCount} ExceptionType={ExceptionType}");

    private static readonly Action<ILogger, string, int, string, Exception?> s_softCancellation =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Information,
            MySqlEventId.SoftCancellation,
            "MySQL command cancellation completed through the soft-cancel path. ExecuteMethod={ExecuteMethod} CommandTimeout={CommandTimeout} ConnectionState={ConnectionState}");

    private static readonly Action<ILogger, string, int, string, Exception?> s_hardCancellation =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Warning,
            MySqlEventId.HardCancellation,
            "MySQL command cancellation escalated to the hard-cancel path. ExecuteMethod={ExecuteMethod} CommandTimeout={CommandTimeout} ConnectionState={ConnectionState}");

    private static readonly Action<ILogger, string, int, string, Exception?> s_commandTimeoutExhausted =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Warning,
            MySqlEventId.CommandTimeoutExhausted,
            "MySQL command timeout exhausted. ExecuteMethod={ExecuteMethod} CommandTimeout={CommandTimeout} ConnectionState={ConnectionState}");

    private static readonly Action<ILogger, Guid, string, string, Exception?> s_commitUnknown =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Warning,
            MySqlEventId.CommitUnknown,
            "MySQL transaction commit failed with an unknown outcome. TransactionId={TransactionId} ConnectionState={ConnectionState} Guidance={Guidance}");

    private static readonly Action<ILogger, string, Exception?> s_missingSpatialPackageDuringScaffolding =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            MySqlEventId.MissingSpatialPackageDuringScaffolding,
            "{Message}");

    private static readonly Action<ILogger, string, Exception?> s_invalidSpatialIndexConfiguration =
        LoggerMessage.Define<string>(LogLevel.Error, MySqlEventId.InvalidSpatialIndexConfiguration, "{Message}");

    private static readonly Action<ILogger, string, Exception?> s_missingSpatialTranslation =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            MySqlEventId.MissingSpatialTranslation,
            "No supported spatial translation exists for '{MemberOrMethod}'.");

    private static readonly Action<ILogger, string, string, string, Exception?>
        s_foreignKeyPrincipalTableNotScaffolded = LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            MySqlEventId.ForeignKeyPrincipalTableNotScaffolded,
            "Skipping foreign key '{ForeignKeyName}' on table '{TableName}' because principal table '{PrincipalTableName}' is not included in the scaffolding filter.");

    public static void InvalidConfiguration(
        ILogger logger,
        string message,
        string connectionPath,
        string redactedConnectionString
    ) => s_invalidConfiguration(logger, message, connectionPath, redactedConnectionString, null);

    public static void ServerVersionResolved(
        ILogger logger,
        MySqlServerVersion serverVersion
    )
    {
        var capabilities = serverVersion.Capabilities;

        s_serverVersionResolved(
            logger,
            serverVersion.IsMariaDb ? "MariaDB" : "MySQL",
            serverVersion.Version.ToString(),
            FormattableString.Invariant(
                $"CTE={capabilities.SupportsCommonTableExpressions};WindowFunctions={capabilities.SupportsWindowFunctions};NativeJson={capabilities.SupportsNativeJsonType};JsonAlias={capabilities.UsesJsonAliasForJsonColumns};Returning={capabilities.SupportsReturningClause};DateTime6={capabilities.SupportsDateTime6};GeneratedInvisiblePrimaryKeys={capabilities.SupportsGeneratedInvisiblePrimaryKeys};Savepoints={capabilities.SupportsSavepoints};GeneratedColumnNullabilityClause={capabilities.SupportsGeneratedColumnNullabilityClause};VirtualGeneratedColumns={capabilities.SupportsVirtualGeneratedColumns};StoredGeneratedColumns={capabilities.SupportsStoredGeneratedColumns};SpatialColumnSridAttribute={capabilities.SupportsSpatialColumnSridAttribute};NativeSequences={capabilities.SupportsNativeSequences};IntersectExcept={capabilities.SupportsIntersectExcept};SystemVersioning={capabilities.SupportsSystemVersioning};FullTextIndex={capabilities.SupportsFullTextIndex}"),
            null);
    }

    public static void SchemaUnsupported(
        ILogger logger,
        string message
    ) => s_schemaUnsupported(logger, message, null);

    public static void KeyOrIndexMaxLengthRequired(
        ILogger logger,
        string message
    ) => s_keyOrIndexMaxLengthRequired(logger, message, null);

    public static void ImplicitDecimalPrecisionDefaulted(
        ILogger logger,
        string message
    ) => s_implicitDecimalPrecisionDefaulted(logger, message, null);

    public static void RetryAttempt(
        ILogger logger,
        int attempt,
        int maxRetryCount,
        TimeSpan? delay,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_retryAttempt(
            logger,
            attempt,
            maxRetryCount,
            delay?.TotalMilliseconds ?? 0,
            exception.GetType()
                .Name,
            exception);
    }

    public static void RetryLimitExceeded(
        ILogger logger,
        int attempts,
        int maxRetryCount,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_retryLimitExceeded(
            logger,
            attempts,
            maxRetryCount,
            exception.GetType()
                .Name,
            exception);
    }

    public static void SoftCancellation(
        ILogger logger,
        string executeMethod,
        int commandTimeout,
        string connectionState
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_softCancellation(logger, executeMethod, commandTimeout, connectionState, null);
    }

    public static void HardCancellation(
        ILogger logger,
        string executeMethod,
        int commandTimeout,
        string connectionState
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_hardCancellation(logger, executeMethod, commandTimeout, connectionState, null);
    }

    public static void CommandTimeoutExhausted(
        ILogger logger,
        string executeMethod,
        int commandTimeout,
        string connectionState,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_commandTimeoutExhausted(logger, executeMethod, commandTimeout, connectionState, exception);
    }

    public static void CommitUnknown(
        ILogger logger,
        Guid transactionId,
        string connectionState,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_commitUnknown(
            logger,
            transactionId,
            connectionState,
            "Use Database.CreateExecutionStrategy().ExecuteInTransaction(...) or ExecuteInTransactionAsync(..., verifySucceeded: ...) to verify whether the commit succeeded.",
            exception);
    }

    public static void MissingSpatialPackageDuringScaffolding(
        ILogger logger,
        string message
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_missingSpatialPackageDuringScaffolding(logger, message, null);
    }

    public static void InvalidSpatialIndexConfiguration(
        ILogger logger,
        string message
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_invalidSpatialIndexConfiguration(logger, message, null);
    }

    public static void MissingSpatialTranslation(
        ILogger logger,
        string memberOrMethod
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberOrMethod);

        s_missingSpatialTranslation(logger, memberOrMethod, null);
    }

    public static void ForeignKeyPrincipalTableNotScaffolded(
        ILogger logger,
        string foreignKeyName,
        string tableName,
        string principalTableName
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_foreignKeyPrincipalTableNotScaffolded(logger, foreignKeyName, tableName, principalTableName, null);
    }
}
