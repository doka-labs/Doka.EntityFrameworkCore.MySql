namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Engine facts and dialect traits that change provider runtime behavior. A
/// declaration belongs here only when a production path consumes it directly or
/// maps it to a <see cref="ProviderCapability"/>.
/// </summary>
internal enum EngineCapability
{
    NativeJsonType,
    JsonValidationFunction,
    ReturningClause,
    Savepoints,
    GeneratedColumnNullabilityClause,
    VirtualGeneratedColumns,
    StoredGeneratedColumns,
    StoredGeneratedColumnUsesPersistentKeyword,
    SpatialColumnSridAttribute,
    CommonTableExpressions,
    SystemVersionedTables,
    ApplicationTimePeriods,
    ApplicationTimeWithoutOverlaps,
    TemporalPeriodCatalog,
    NativeSequences,
    RenameColumnSyntax,
    RegexpLikeFunction,
    LateralDerivedTables,
    JsonTableExistsRequiresWorkaround,
    SelfReferencingMutationRequiresIsolation,
    MariaDbSpatialSemantics,
    CheckConstraintCatalogIncludesTableName,
    FunctionalIndexExpressionMetadata,
    CheckConstraints,
    DescendingIndexes,
    FunctionalIndexes,
    IndexPrefixLengths,
    RenameIndex,
    ExpressionDefaults,
    PreparedDdl,
    AtomicDdl,
}
