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
    /// Emitted when a foreign key is skipped during scaffolding because its principal table
    /// is not included in the scaffolding filter.
    /// </summary>
    public static readonly EventId ForeignKeyPrincipalTableNotScaffolded =
        new(1403, nameof(ForeignKeyPrincipalTableNotScaffolded));
}
