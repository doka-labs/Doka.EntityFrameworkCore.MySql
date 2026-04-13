namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines the supported provider-level value-generation strategies.
/// </summary>
public enum MySqlValueGenerationStrategy
{
    /// <summary>
    /// Disables provider-managed value generation for the property.
    /// </summary>
    None = 0,

    /// <summary>
    /// Uses MySQL-family auto-increment semantics for integer key values.
    /// </summary>
    AutoIncrement = 1,

    /// <summary>
    /// Uses explicit client-side GUID generation for the property.
    /// </summary>
    ClientGuid = 2,

    /// <summary>
    /// Uses a Hi/Lo pattern backed by a database sequence (table-based emulation on MySQL,
    /// native <c>CREATE SEQUENCE</c> on MariaDB 10.3+). Provides block-allocated unique values
    /// with fewer database round-trips than per-row identity columns.
    /// </summary>
    HiLo = 3,
}
