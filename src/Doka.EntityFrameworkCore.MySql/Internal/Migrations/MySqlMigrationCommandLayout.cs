namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationCommandLayout
{
    private MySqlMigrationCommandLayout(
        string commandText,
        System.Collections.ObjectModel.ReadOnlyCollection<MySqlMigrationCommandFragment> fragments,
        ReadOnlyMemory<char> body
    )
    {
        CommandText = commandText;
        Fragments = fragments;
        Body = body;
        IsScoped = fragments.Count > 1;
    }

    public string CommandText { get; }

    public IReadOnlyList<MySqlMigrationCommandFragment> Fragments { get; }

    public ReadOnlyMemory<char> Body { get; }

    public bool IsScoped { get; }

    public static MySqlMigrationCommandLayout CreateBodyOnly(
        string commandText
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        var body = commandText.AsMemory();
        var fragments = Array.AsReadOnly(
        [
            new MySqlMigrationCommandFragment(MySqlMigrationCommandFragmentKind.Body, body),
        ]);

        return new MySqlMigrationCommandLayout(commandText, fragments, body);
    }

    public static MySqlMigrationCommandLayout CreateScoped(
        string commandText,
        IReadOnlyList<string> setupCommands,
        IReadOnlyList<string> cleanupCommands
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentNullException.ThrowIfNull(cleanupCommands);

        if (setupCommands.Count == 0
            || cleanupCommands.Count == 0)
        {
            throw new ArgumentException("A scoped migration command requires setup and cleanup commands.");
        }

        var fragments = new MySqlMigrationCommandFragment[setupCommands.Count + cleanupCommands.Count + 1];
        var offset = 0;
        var fragmentIndex = 0;

        foreach (var setupCommand in setupCommands)
        {
            ValidateFragment(commandText, setupCommand, offset, MySqlMigrationCommandFragmentKind.Setup);
            fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(
                MySqlMigrationCommandFragmentKind.Setup,
                commandText.AsMemory(offset, setupCommand.Length));
            offset += setupCommand.Length;
        }

        var cleanupLength = 0;
        foreach (var cleanupCommand in cleanupCommands)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cleanupCommand);
            cleanupLength = checked(cleanupLength + cleanupCommand.Length);
        }

        var bodyLength = commandText.Length - offset - cleanupLength;
        if (bodyLength <= 0
            || commandText
                .AsSpan(offset, bodyLength)
                .IsWhiteSpace())
        {
            throw new InvalidOperationException("A scoped migration command must contain one non-empty body fragment.");
        }

        var body = commandText.AsMemory(offset, bodyLength);
        fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(MySqlMigrationCommandFragmentKind.Body, body);
        offset += bodyLength;

        foreach (var cleanupCommand in cleanupCommands)
        {
            ValidateFragment(commandText, cleanupCommand, offset, MySqlMigrationCommandFragmentKind.Cleanup);
            fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(
                MySqlMigrationCommandFragmentKind.Cleanup,
                commandText.AsMemory(offset, cleanupCommand.Length));
            offset += cleanupCommand.Length;
        }

        if (offset != commandText.Length)
        {
            throw new InvalidOperationException(
                "Provider-owned migration fragments must cover the complete command text.");
        }

        return new MySqlMigrationCommandLayout(commandText, Array.AsReadOnly(fragments), body);
    }

    private static void ValidateFragment(
        string commandText,
        string fragmentText,
        int offset,
        MySqlMigrationCommandFragmentKind kind
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentText);

        // Subtraction keeps the bounds check overflow-safe before the slice is created.
        if (offset > commandText.Length - fragmentText.Length
            || !commandText
                .AsSpan(offset, fragmentText.Length)
                .SequenceEqual(fragmentText))
        {
            throw new InvalidOperationException(
                $"The provider-owned {kind} fragment does not match the aggregate command text.");
        }
    }
}
