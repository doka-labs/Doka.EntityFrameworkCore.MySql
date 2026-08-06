namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provider behaviors whose implementation route depends on the configured
/// engine profile.
/// </summary>
internal enum ProviderCapability
{
    JsonColumns,
    ReturningClause,
    Savepoints,
    GeneratedColumnNullabilityClause,
    VirtualGeneratedColumns,
    StoredGeneratedColumns,
    SpatialColumnSridAttribute,
    CommonTableExpressions,
    TemporalTables,
    ApplicationTimePeriods,
    BitemporalTables,
    Sequences,
    RenameColumn,
    LateralDerivedTables,
    SelfReferencingMutations,
    FunctionalIndexScaffolding,
}

/// <summary>
/// Describes how the provider supplies a capability on one engine profile.
/// </summary>
internal enum ProviderSupportStatus
{
    Native,
    Emulated,
    UnsupportedByEngine,
}
