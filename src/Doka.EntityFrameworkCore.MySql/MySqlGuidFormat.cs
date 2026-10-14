namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines provider-owned storage formats for <see cref="Guid"/> values.
/// </summary>
public enum MySqlGuidFormat
{
    /// <summary>
    /// Stores GUID values in the modern 16-byte binary layout.
    /// </summary>
    Binary16 = 0,

    /// <summary>
    /// Stores GUID values as canonical hyphenated text in <c>char(36)</c>.
    /// </summary>
    Char36 = 1,

    /// <summary>
    /// Stores GUID values in MariaDB's native <c>uuid</c> column type (10.7 or later).
    /// </summary>
    Uuid = 2,
}
