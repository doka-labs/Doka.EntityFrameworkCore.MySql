namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Identifies the execution role of validated SQL inside a rendered
/// migration command.
/// </summary>
public enum MySqlMigrationCommandFragmentKind
{
    /// <summary>
    /// Identifies the unclassified default value of a migration command
    /// fragment.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Initializes scoped state before the operation body.
    /// </summary>
    Setup = 1,

    /// <summary>
    /// Contains the SQL that implements the rendered migration operation.
    /// </summary>
    Body = 2,

    /// <summary>
    /// Restores scoped state after the operation body.
    /// </summary>
    Cleanup = 3,
}
