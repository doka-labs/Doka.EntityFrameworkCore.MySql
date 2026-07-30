namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines the first stable provider event identifiers for logging and diagnostics.
/// </summary>
public static class MySqlEventId
{
    /// <summary>
    /// Emitted when server-version resolution and capability caching succeed.
    /// </summary>
    public static readonly EventId ServerVersionResolved = new(1000, nameof(ServerVersionResolved));

    /// <summary>
    /// Emitted when provider configuration is invalid.
    /// </summary>
    public static readonly EventId InvalidConfiguration = new(1001, nameof(InvalidConfiguration));

    /// <summary>
    /// Emitted when an explicit compatibility opt-in allows a legacy,
    /// unvalidated, or future server release line.
    /// </summary>
    public static readonly EventId UnsupportedServerVersion = new(1005, nameof(UnsupportedServerVersion));

    /// <summary>
    /// Emitted when unsupported MySQL schema configuration is encountered.
    /// </summary>
    public static readonly EventId SchemaUnsupported = new(1002, nameof(SchemaUnsupported));

    /// <summary>
    /// Emitted when a keyed or indexed text/binary property omits the required explicit max length.
    /// </summary>
    public static readonly EventId KeyOrIndexMaxLengthRequired = new(1003, nameof(KeyOrIndexMaxLengthRequired));

    /// <summary>
    /// Emitted when a decimal property falls back to the provider default precision/scale contract.
    /// </summary>
    public static readonly EventId ImplicitDecimalPrecisionDefaulted = new(
        1004,
        nameof(ImplicitDecimalPrecisionDefaulted));

    /// <summary>
    /// Emitted when the provider retries a transient failure through the execution-strategy pipeline.
    /// </summary>
    public static readonly EventId RetryAttempt = new(1500, nameof(RetryAttempt));

    /// <summary>
    /// Emitted when the provider exhausts the configured retry budget for a transient failure.
    /// </summary>
    public static readonly EventId RetryLimitExceeded = new(1501, nameof(RetryLimitExceeded));

    /// <summary>
    /// Emitted when the driver completes a command cancellation through the soft-cancel path.
    /// </summary>
    public static readonly EventId SoftCancellation = new(1502, nameof(SoftCancellation));

    /// <summary>
    /// Emitted when the driver needs the hard-cancel path to finish command cancellation.
    /// </summary>
    public static readonly EventId HardCancellation = new(1503, nameof(HardCancellation));

    /// <summary>
    /// Emitted when a relational command exhausts its configured timeout budget.
    /// </summary>
    public static readonly EventId CommandTimeoutExhausted = new(1504, nameof(CommandTimeoutExhausted));

    /// <summary>
    /// Emitted when a transaction commit fails transiently and the commit outcome may be unknown.
    /// </summary>
    public static readonly EventId CommitUnknown = new(1505, nameof(CommitUnknown));

    /// <summary>
    /// Emitted when spatial reverse engineering encounters spatial artifacts but the optional package is not installed.
    /// </summary>
    public static readonly EventId MissingSpatialPackageDuringScaffolding =
        new(1600, nameof(MissingSpatialPackageDuringScaffolding));

    /// <summary>
    /// Emitted when spatial index configuration violates the supported provider contract.
    /// </summary>
    public static readonly EventId InvalidSpatialIndexConfiguration = new(
        1601,
        nameof(InvalidSpatialIndexConfiguration));

    /// <summary>
    /// Emitted when a spatial member or method is detected but no supported server translation exists.
    /// </summary>
    public static readonly EventId MissingSpatialTranslation = new(1602, nameof(MissingSpatialTranslation));

    /// <summary>
    /// Emitted when the translator can statically observe that the two
    /// <c>ST_Distance</c> arguments carry different SRIDs. MySQL rejects the
    /// mismatch with a hard error; MariaDB silently treats both inputs as
    /// Cartesian and returns a numerically meaningless result. The warning
    /// gives consumers a signal before the silent-Cartesian path produces a
    /// wrong result.
    /// </summary>
    public static readonly EventId SpatialSridMismatchDetected = new(1603, nameof(SpatialSridMismatchDetected));

    /// <summary>
    /// Emitted when a foreign key is skipped during scaffolding because its principal table
    /// is not included in the scaffolding filter.
    /// </summary>
    public static readonly EventId ForeignKeyPrincipalTableNotScaffolded =
        new(1403, nameof(ForeignKeyPrincipalTableNotScaffolded));

    /// <summary>
    /// Emitted when the migration advisory lock could not be released cleanly via
    /// <c>RELEASE_LOCK</c>. The dedicated connection is still disposed afterwards,
    /// which releases the session-scoped lock implicitly, so the migration outcome
    /// is unaffected; the warning surfaces an unusual server-side state worth
    /// investigating.
    /// </summary>
    public static readonly EventId LockReleaseFailed = new(1102, nameof(LockReleaseFailed));

    /// <summary>
    /// Emitted at most once per <see cref="DbContext.SaveChanges()"/> batch when the
    /// projected prepared-statement parameter count would exceed the MySQL hard limit
    /// (65535 placeholders). The batch is split at the command that would have crossed
    /// the cap; the next command opens a fresh batch.
    /// </summary>
    public static readonly EventId BulkInsertParameterCountCapped = new(1700, nameof(BulkInsertParameterCountCapped));

    /// <summary>
    /// Emitted at most once per <see cref="DbContext.SaveChanges()"/> batch when the
    /// estimated wire-size of the multi-row INSERT would exceed the conservative
    /// <c>max_allowed_packet</c> budget. The batch is split at the command that would
    /// have crossed the cap.
    /// </summary>
    public static readonly EventId BulkInsertPacketSizeCapped = new(1701, nameof(BulkInsertPacketSizeCapped));
}
