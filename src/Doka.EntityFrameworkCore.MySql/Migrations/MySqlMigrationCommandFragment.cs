namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes one immutable, provider-validated fragment of a rendered migration
/// command.
/// </summary>
/// <remarks>
/// Fragment text is an exact slice of the containing
/// <see cref="MySqlMigrationCommandSpec.CommandText"/>. A fragment can describe
/// provider-rendered SQL or a handler-authored scope validated through
/// <see cref="MySqlMigrationCommandSpec.CreateScoped"/>. Its kind describes an
/// execution role, not SQL authorship or provenance. A default struct value has
/// no command text and cannot be attached through the public API.
/// </remarks>
public readonly struct MySqlMigrationCommandFragment
{
    internal MySqlMigrationCommandFragment(
        MySqlMigrationCommandFragmentKind kind,
        ReadOnlyMemory<char> commandText
    )
    {
        Kind = kind;
        CommandText = commandText;
    }

    /// <summary>
    /// Gets the provider-validated execution role.
    /// </summary>
    public MySqlMigrationCommandFragmentKind Kind { get; }

    /// <summary>
    /// Gets the exact SQL slice for this fragment.
    /// </summary>
    public ReadOnlyMemory<char> CommandText { get; }
}
