namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provider behaviors whose implementation route depends on the configured
/// engine profile.
/// </summary>
internal enum ProviderCapability
{
    SchemaOperations,
    JsonColumns,
    CheckConstraints,
    DescendingIndexes,
    FilteredIndexes,
    FunctionalIndexes,
    IndexPrefixLengths,
    ReturningClause,
    Savepoints,
    GeneratedColumnNullabilityClause,
    VirtualGeneratedColumns,
    StoredGeneratedColumns,
    SpatialColumnSridEnforcement,
    CommonTableExpressions,
    TemporalTables,
    ApplicationTimePeriods,
    BitemporalTables,
    Sequences,
    RenameColumn,
    RenameIndex,
    ExpressionDefaults,
    PreparedDdl,
    AtomicDdl,
    TransactionalDdl,
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
