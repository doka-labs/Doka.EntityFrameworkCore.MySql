namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Identifies a migration-facing capability whose implementation route may
/// differ between supported MySQL and MariaDB profiles.
/// </summary>
public enum MySqlMigrationFeature
{
    /// <summary>Independent relational schema namespaces.</summary>
    SchemaOperations,

    /// <summary>Validated JSON column storage.</summary>
    JsonColumns,

    /// <summary>Enforced check constraints.</summary>
    CheckConstraints,

    /// <summary>Descending index key parts.</summary>
    DescendingIndexes,

    /// <summary>Indexes restricted by a row predicate.</summary>
    FilteredIndexes,

    /// <summary>Index key parts defined by SQL expressions.</summary>
    FunctionalIndexes,

    /// <summary>Prefix lengths on index key parts.</summary>
    IndexPrefixLengths,

    /// <summary>Direct column rename syntax.</summary>
    RenameColumn,

    /// <summary>Direct index rename syntax.</summary>
    RenameIndex,

    /// <summary>Explicit nullability clauses on generated columns.</summary>
    GeneratedColumnNullabilityClause,

    /// <summary>Virtual generated columns.</summary>
    VirtualGeneratedColumns,

    /// <summary>Stored generated columns.</summary>
    StoredGeneratedColumns,

    /// <summary>Column-level spatial reference-system identifiers.</summary>
    SpatialColumnSridAttribute,

    /// <summary>Default values expressed as SQL expressions.</summary>
    ExpressionDefaults,

    /// <summary>System-versioned or provider-emulated temporal tables.</summary>
    TemporalTables,

    /// <summary>Application-time periods.</summary>
    ApplicationTimePeriods,

    /// <summary>Combined system-versioned and application-time tables.</summary>
    BitemporalTables,

    /// <summary>Native or provider-emulated database sequences.</summary>
    Sequences,

    /// <summary>Preparing DDL statements through the server protocol.</summary>
    PreparedDdl,

    /// <summary>Crash-safe atomic DDL for supported statement and engine shapes.</summary>
    AtomicDdl,

    /// <summary>DDL whose effects can be rolled back with a transaction.</summary>
    TransactionalDdl,
}
