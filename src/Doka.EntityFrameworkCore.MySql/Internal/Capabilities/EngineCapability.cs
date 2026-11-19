namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Engine facts and dialect traits that change provider runtime behavior. A
/// declaration belongs here only when a production path consumes it directly or
/// maps it to a <see cref="ProviderCapability" />.
/// </summary>
internal enum EngineCapability
{
    NativeJsonType,

    /// <summary>JSON membership compares decoded string values rather than their escape spelling.</summary>
    JsonContainsComparesDecodedStrings,
    NativeUuidType,
    JsonValidationFunction,
    ReturningClause,
    Savepoints,
    GeneratedColumnNullabilityClause,
    VirtualGeneratedColumns,
    StoredGeneratedColumns,
    StoredGeneratedColumnUsesPersistentKeyword,
    SpatialColumnSridAttribute,
    SpatialSridCheckConstraint,
    SpatialWktAxisOrderOptions,
    SpatialIsValidFunction,
    SpatialBufferStrategies,
    SpatialCollectAggregate,
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
    SharedUca1400Collations,
    SelfReferencingMutationRequiresIsolation,
    MariaDbSpatialSemantics,
    ColumnCommentPrecedesVisibilityAttribute,
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

    /// <summary>The engine implements the FOR SHARE locking-read grammar.</summary>
    ForShareClause,

    /// <summary>The shared-read dialect uses LOCK IN SHARE MODE rather than FOR SHARE.</summary>
    SharedLockingReadUsesLockInShareMode,

    /// <summary>The engine implements FOR UPDATE NOWAIT.</summary>
    ForUpdateNoWaitClause,

    /// <summary>The engine implements FOR UPDATE SKIP LOCKED for InnoDB.</summary>
    ForUpdateSkipLockedClause,

    /// <summary>The engine implements native SQL window functions.</summary>
    WindowFunctions,

    /// <summary>LAG and LEAD accept an explicit third argument for an absent row.</summary>
    WindowNavigationDefaultArgument,

    /// <summary>Prepared window frames accept parameter markers as ROWS and RANGE offsets.</summary>
    WindowFrameParameterOffsets,

    /// <summary>INSERT accepts an incoming-row alias for ON DUPLICATE KEY UPDATE.</summary>
    InsertRowAlias,

    /// <summary>The selected UPSERT dialect references incoming columns through VALUES.</summary>
    OnDuplicateKeyValuesFunction,

    /// <summary>The recursive CTE limit uses the max_recursive_iterations session variable.</summary>
    RecursiveCteLimitUsesMaxRecursiveIterations,

    /// <summary>WITH can precede UPDATE and DELETE statements with a physical target.</summary>
    CteUpdateDelete,

    /// <summary>The recursive CTE limit uses the cte_max_recursion_depth session variable.</summary>
    RecursiveCteLimitUsesCteMaxRecursionDepth,
}
