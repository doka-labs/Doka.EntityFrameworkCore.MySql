namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlLoggerMessages
{
    // Failure emitters record the exception type as structured data but do not
    // attach the exception object. Driver exception messages can contain SQL or
    // connection metadata and therefore do not belong in provider-owned logs.
    private static readonly Action<ILogger, string, string, string, Exception?> s_invalidConfiguration =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            MySqlEventId.InvalidConfiguration,
            "{Message} ConnectionPath={ConnectionPath} RedactedConnectionString={RedactedConnectionString}");

    private static readonly Action<ILogger, string, string, string, string, Exception?> s_unsupportedServerVersion =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            MySqlEventId.UnsupportedServerVersion,
            "Running outside the supported database matrix by explicit opt-in. "
            + "DatabaseEngine={DatabaseEngine} ServerVersion={ServerVersion} "
            + "SupportStatus={SupportStatus} SupportedMatrix={SupportedMatrix}");

    private static readonly Action<ILogger, string, string, string, Exception?> s_schemaUnsupported =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            MySqlEventId.SchemaUnsupported,
            "MySQL schema configuration is not supported. Scope={Scope} Reason={Reason} Remediation={Remediation}");

    private static readonly Action<ILogger, string, string, string, Exception?> s_keyOrIndexMaxLengthRequired =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            MySqlEventId.KeyOrIndexMaxLengthRequired,
            "The keyed or indexed {PropertyKind} property '{EntityType}.{Property}' must declare an explicit max length.");

    private static readonly Action<ILogger, string, string, int, int, Exception?> s_implicitDecimalPrecisionDefaulted =
        LoggerMessage.Define<string, string, int, int>(
            LogLevel.Warning,
            MySqlEventId.ImplicitDecimalPrecisionDefaulted,
            "The decimal property '{EntityType}.{Property}' does not declare an explicit precision/scale. The provider default 'decimal({DefaultPrecision},{DefaultScale})' will be used.");

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

    private static readonly Action<ILogger, string, int, string, string, Exception?> s_commandTimeoutExhausted =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Warning,
            MySqlEventId.CommandTimeoutExhausted,
            "MySQL command timeout exhausted. ExecuteMethod={ExecuteMethod} "
            + "CommandTimeout={CommandTimeout} ConnectionState={ConnectionState} "
            + "ExceptionType={ExceptionType}");

    private static readonly Action<ILogger, Guid, string, string, string, Exception?> s_commitUnknown =
        LoggerMessage.Define<Guid, string, string, string>(
            LogLevel.Warning,
            MySqlEventId.CommitUnknown,
            "MySQL transaction commit failed with an unknown outcome. "
            + "TransactionId={TransactionId} ConnectionState={ConnectionState} "
            + "ExceptionType={ExceptionType} Guidance={Guidance}");

    private static readonly Action<ILogger, string, string, Exception?> s_missingSpatialPackageDuringScaffolding =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            MySqlEventId.MissingSpatialPackageDuringScaffolding,
            "Skipping spatial column during reverse engineering because the optional Doka.EntityFrameworkCore.MySql.NetTopologySuite package is not active in the design-time service graph. Table={Table} Column={Column}");

    private static readonly Action<ILogger, string, string, Exception?> s_invalidSpatialIndexConfiguration =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            MySqlEventId.InvalidSpatialIndexConfiguration,
            "Spatial index configuration violates the supported provider contract. Index={Index} Reason={Reason}");

    private static readonly Action<ILogger, string, Exception?> s_missingSpatialTranslation =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            MySqlEventId.MissingSpatialTranslation,
            "No supported spatial translation exists for '{MemberOrMethod}'.");

    private static readonly Action<ILogger, int, int, Exception?> s_spatialSridMismatchDetected =
        LoggerMessage.Define<int, int>(
            LogLevel.Warning,
            MySqlEventId.SpatialSridMismatchDetected,
            "ST_Distance arguments declare different SRIDs (FirstSrid={FirstSrid}, SecondSrid={SecondSrid}). MySQL rejects the mismatch with a hard error; MariaDB treats both inputs as Cartesian and returns a numerically meaningless result. Use ST_Transform or align the SRIDs before invoking Distance.");

    private static readonly Action<ILogger, string, string, string, Exception?>
        s_foreignKeyPrincipalTableNotScaffolded = LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            MySqlEventId.ForeignKeyPrincipalTableNotScaffolded,
            "Skipping foreign key '{ForeignKeyName}' on table '{TableName}' because principal table '{PrincipalTableName}' is not included in the scaffolding filter.");

    private static readonly Action<ILogger, int, int, int, Exception?> s_bulkInsertParameterCountCapped =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Warning,
            MySqlEventId.BulkInsertParameterCountCapped,
            "Multi-row INSERT batch split at the MySQL prepared-statement parameter limit. EffectiveBatchSize={EffectiveBatchSize} ProjectedParameterCount={ProjectedParameterCount} MaxParameterCount={MaxParameterCount}");

    private static readonly Action<ILogger, int, int, int, Exception?> s_bulkInsertPacketSizeCapped =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Warning,
            MySqlEventId.BulkInsertPacketSizeCapped,
            "Multi-row INSERT batch split at the conservative max_allowed_packet budget. EffectiveBatchSize={EffectiveBatchSize} EstimatedPacketSizeBytes={EstimatedPacketSizeBytes} MaxPacketSizeBytes={MaxPacketSizeBytes}");

    private static readonly Action<ILogger, string, string, Exception?> s_lockReleaseFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            MySqlEventId.LockReleaseFailed,
            "MySQL migration advisory lock release failed. "
            + "LockScopeId={LockScopeId} ExceptionType={ExceptionType}. "
            + "The dedicated connection is still disposed, which releases the session-scoped lock implicitly.");

    private static readonly Action<ILogger, string, double, Exception?> s_migrationLockAcquired =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            MySqlEventId.MigrationLockAcquired,
            "MySQL migration advisory lock acquired. LockScopeId={LockScopeId} DurationMs={DurationMs}");

    private static readonly Action<ILogger, string, double, string, Exception?> s_migrationLockTimeout =
        LoggerMessage.Define<string, double, string>(
            LogLevel.Warning,
            MySqlEventId.MigrationLockTimeout,
            "MySQL migration advisory lock timed out. "
            + "LockScopeId={LockScopeId} DurationMs={DurationMs} ExceptionType={ExceptionType}");

    private static readonly Action<ILogger, string, double, string, Exception?> s_migrationLockAcquireFailed =
        LoggerMessage.Define<string, double, string>(
            LogLevel.Error,
            MySqlEventId.MigrationLockAcquireFailed,
            "MySQL migration advisory lock acquisition failed. "
            + "LockScopeId={LockScopeId} DurationMs={DurationMs} ExceptionType={ExceptionType}");

    public static void InvalidConfiguration(
        ILogger logger,
        string message,
        string connectionPath,
        string redactedConnectionString
    ) => s_invalidConfiguration(logger, message, connectionPath, redactedConnectionString, null);

    // Order-preserving provider-support snapshot for the resolved-version log
    // payload. Each entry reports Native, Emulated, or UnsupportedByEngine rather
    // than conflating an engine fact with provider availability.
    private static readonly (string Label, ProviderCapability Capability)[] s_capabilitySnapshot =
    [
        ("JsonColumns", ProviderCapability.JsonColumns),
        ("ReturningClause", ProviderCapability.ReturningClause),
        ("Savepoints", ProviderCapability.Savepoints),
        ("GeneratedColumnNullabilityClause", ProviderCapability.GeneratedColumnNullabilityClause),
        ("VirtualGeneratedColumns", ProviderCapability.VirtualGeneratedColumns),
        ("StoredGeneratedColumns", ProviderCapability.StoredGeneratedColumns),
        ("SpatialColumnSridAttribute", ProviderCapability.SpatialColumnSridAttribute),
        ("Sequences", ProviderCapability.Sequences),
        ("RenameColumn", ProviderCapability.RenameColumn),
        ("LateralDerivedTables", ProviderCapability.LateralDerivedTables),
        ("SelfReferencingMutations", ProviderCapability.SelfReferencingMutations),
        ("FunctionalIndexScaffolding", ProviderCapability.FunctionalIndexScaffolding),
    ];

    public static void ServerVersionResolved(
        ILogger logger,
        MySqlServerVersion serverVersion
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serverVersion);

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.Log(
            LogLevel.Information,
            MySqlEventId.ServerVersionResolved,
            new ServerVersionResolvedLogValues(serverVersion),
            exception: null,
            ServerVersionResolvedLogValues.Render);
    }

    public static void UnsupportedServerVersion(
        ILogger logger,
        MySqlServerVersion serverVersion
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serverVersion);

        s_unsupportedServerVersion(
            logger,
            serverVersion.IsMariaDb ? "MariaDB" : "MySQL",
            serverVersion.Version.ToString(),
            serverVersion.SupportStatus.ToString(),
            ServerVersionSupportPolicy.SupportedMatrix,
            null);
    }

    public static void SchemaUnsupported(
        ILogger logger,
        string scope,
        string reason,
        string remediation
    ) => s_schemaUnsupported(logger, scope, reason, remediation, null);

    public static void KeyOrIndexMaxLengthRequired(
        ILogger logger,
        string entityType,
        string property,
        string propertyKind
    ) => s_keyOrIndexMaxLengthRequired(logger, propertyKind, entityType, property, null);

    public static void ImplicitDecimalPrecisionDefaulted(
        ILogger logger,
        string entityType,
        string property,
        int defaultPrecision,
        int defaultScale
    ) => s_implicitDecimalPrecisionDefaulted(logger, entityType, property, defaultPrecision, defaultScale, null);

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
            null);
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
            null);
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

        s_commandTimeoutExhausted(
            logger,
            executeMethod,
            commandTimeout,
            connectionState,
            exception.GetType().Name,
            null);
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
            exception.GetType().Name,
            "Use Database.CreateExecutionStrategy().ExecuteInTransaction(...) or ExecuteInTransactionAsync(..., verifySucceeded: ...) to verify whether the commit succeeded.",
            null);
    }

    public static void MissingSpatialPackageDuringScaffolding(
        ILogger logger,
        string table,
        string column
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_missingSpatialPackageDuringScaffolding(logger, table, column, null);
    }

    public static void InvalidSpatialIndexConfiguration(
        ILogger logger,
        string index,
        string reason
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_invalidSpatialIndexConfiguration(logger, index, reason, null);
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

    public static void SpatialSridMismatchDetected(
        ILogger logger,
        int firstSrid,
        int secondSrid
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_spatialSridMismatchDetected(logger, firstSrid, secondSrid, null);
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

    public static void BulkInsertParameterCountCapped(
        ILogger logger,
        int effectiveBatchSize,
        int projectedParameterCount,
        int maxParameterCount
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_bulkInsertParameterCountCapped(logger, effectiveBatchSize, projectedParameterCount, maxParameterCount, null);
    }

    public static void BulkInsertPacketSizeCapped(
        ILogger logger,
        int effectiveBatchSize,
        int estimatedPacketSizeBytes,
        int maxPacketSizeBytes
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_bulkInsertPacketSizeCapped(logger, effectiveBatchSize, estimatedPacketSizeBytes, maxPacketSizeBytes, null);
    }

    public static void LockReleaseFailed(
        ILogger logger,
        string lockScopeId,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_lockReleaseFailed(logger, lockScopeId, exception.GetType().Name, null);
    }

    public static void MigrationLockAcquired(
        ILogger logger,
        string lockScopeId,
        TimeSpan duration
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        s_migrationLockAcquired(logger, lockScopeId, duration.TotalMilliseconds, null);
    }

    public static void MigrationLockTimeout(
        ILogger logger,
        string lockScopeId,
        TimeSpan duration,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_migrationLockTimeout(
            logger,
            lockScopeId,
            duration.TotalMilliseconds,
            exception.GetType().Name,
            null);
    }

    public static void MigrationLockAcquireFailed(
        ILogger logger,
        string lockScopeId,
        TimeSpan duration,
        Exception exception
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        s_migrationLockAcquireFailed(
            logger,
            lockScopeId,
            duration.TotalMilliseconds,
            exception.GetType().Name,
            null);
    }

    /// <summary>
    /// Carries every server-version-resolution field as a structured key-value
    /// entry so OpenTelemetry sinks can query each provider support status by name.
    /// <c>LoggerMessage.Define</c> caps at six generic parameters which would
    /// force the support snapshot back into a joined string; the
    /// per-capability fields stay queryable here because the struct implements
    /// <see cref="IReadOnlyList{T}"/> over <see cref="KeyValuePair{TKey,TValue}"/>.
    /// </summary>
    private readonly struct ServerVersionResolvedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public static readonly Func<ServerVersionResolvedLogValues, Exception?, string> Render = (
            state,
            _
        ) => state.ToString();

        private readonly MySqlServerVersion _serverVersion;

        public ServerVersionResolvedLogValues(
            MySqlServerVersion serverVersion
        )
        {
            _serverVersion = serverVersion;
        }

        public int Count => 4 + s_capabilitySnapshot.Length;

        public KeyValuePair<string, object?> this[
            int index
        ] =>
            index switch
            {
                0 => new KeyValuePair<string, object?>(
                    "DatabaseEngine",
                    _serverVersion.IsMariaDb ? "MariaDB" : "MySQL"),
                1 => new KeyValuePair<string, object?>("ServerVersion", _serverVersion.Version.ToString()),
                2 => new KeyValuePair<string, object?>("SupportStatus", _serverVersion.SupportStatus.ToString()),
                3 => new KeyValuePair<string, object?>(
                    "CompatibilityMode",
                    _serverVersion.CompatibilityMode.ToString()),
                _ when index < Count => new KeyValuePair<string, object?>(
                    s_capabilitySnapshot[index - 4].Label,
                    _serverVersion.Profile.GetSupport(s_capabilitySnapshot[index - 4].Capability).ToString()),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var profile = _serverVersion.Profile;
            var engine = _serverVersion.IsMariaDb ? "MariaDB" : "MySQL";
            var capabilities = string.Join(
                ';',
                s_capabilitySnapshot.Select(entry => $"{entry.Label}={profile.GetSupport(entry.Capability)}"));

            return $"Resolved {engine} server version {_serverVersion.Version}. Capabilities={capabilities}";
        }
    }
}
