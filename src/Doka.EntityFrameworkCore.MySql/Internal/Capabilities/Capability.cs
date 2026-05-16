namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Engine-feature capability flags consulted by query-translation, migration, and
/// scaffolding code paths. The sparse keyed shape replaces the previous flat 16-
/// field record so adding a new capability becomes a single static-table append
/// rather than a record-field add plus a per-engine boolean computation. Entries
/// like SupportsDateTime6 / SupportsSavepoints / SupportsFullTextIndex are
/// retained because the provider's transaction surface and the
/// MariaDb / MySql baseline tests genuinely consume them, even though every
/// supported version advertises them as true.
/// </summary>
internal enum Capability
{
    SupportsCommonTableExpressions,
    SupportsWindowFunctions,
    SupportsNativeJsonType,
    UsesJsonAliasForJsonColumns,
    SupportsReturningClause,
    SupportsDateTime6,
    SupportsGeneratedInvisiblePrimaryKeys,
    SupportsSavepoints,
    SupportsGeneratedColumnNullabilityClause,
    SupportsVirtualGeneratedColumns,
    SupportsStoredGeneratedColumns,
    SupportsSpatialColumnSridAttribute,
    SupportsNativeSequences,
    SupportsIntersectExcept,
    SupportsSystemVersioning,
    SupportsFullTextIndex,
}
