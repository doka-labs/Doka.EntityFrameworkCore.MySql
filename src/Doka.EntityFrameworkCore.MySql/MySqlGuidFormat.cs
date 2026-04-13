namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Defines the supported default storage formats for <see cref="Guid"/> values.
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
}
