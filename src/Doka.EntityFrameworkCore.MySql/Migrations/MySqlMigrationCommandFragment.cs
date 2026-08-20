namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes one immutable, provider-owned fragment of a rendered migration
/// command.
/// </summary>
/// <remarks>
/// Fragment text is an exact slice of the containing
/// <see cref="MySqlMigrationCommandSpec.CommandText"/>. Only the provider can
/// create non-empty, classified fragments and attach them to a command
/// specification. A default struct value has no command text and cannot be
/// attached through the public API.
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
    /// Gets the execution role assigned by the provider.
    /// </summary>
    public MySqlMigrationCommandFragmentKind Kind { get; }

    /// <summary>
    /// Gets the exact SQL slice for this fragment.
    /// </summary>
    public ReadOnlyMemory<char> CommandText { get; }
}
