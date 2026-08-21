namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationCommandLayout
{
    private static readonly ReadOnlyCollection<ReadOnlyMemory<char>> s_emptyCommands =
        Array.AsReadOnly(Array.Empty<ReadOnlyMemory<char>>());

    private MySqlMigrationCommandLayout(
        string commandText,
        ReadOnlyCollection<MySqlMigrationCommandFragment> fragments,
        ReadOnlyMemory<char> body,
        ReadOnlyCollection<ReadOnlyMemory<char>> setup,
        ReadOnlyCollection<ReadOnlyMemory<char>> cleanup,
        MySqlMigrationCommandScopeKind scopeKind
    )
    {
        CommandText = commandText;
        Fragments = fragments;
        Body = body;
        Setup = setup;
        Cleanup = cleanup;
        ScopeKind = scopeKind;
    }

    public string CommandText { get; }

    public IReadOnlyList<MySqlMigrationCommandFragment> Fragments { get; }

    public ReadOnlyMemory<char> Body { get; }

    public IReadOnlyList<ReadOnlyMemory<char>> Setup { get; }

    public IReadOnlyList<ReadOnlyMemory<char>> Cleanup { get; }

    public MySqlMigrationCommandScopeKind ScopeKind { get; }

    public bool IsScoped => ScopeKind != MySqlMigrationCommandScopeKind.None;

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

        return new MySqlMigrationCommandLayout(
            commandText,
            fragments,
            body,
            s_emptyCommands,
            s_emptyCommands,
            MySqlMigrationCommandScopeKind.None);
    }

    public static MySqlMigrationCommandLayout CreateScoped(
        string commandText,
        IReadOnlyList<string> setupCommands,
        IReadOnlyList<string> cleanupCommands
    ) => CreateScopedLayout(
        commandText,
        setupCommands,
        cleanupCommands,
        MySqlMigrationCommandScopeKind.ProviderSqlMode);

    public static MySqlMigrationCommandLayout CreateHandlerScoped(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands
    )
    {
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyCommand);
        ArgumentNullException.ThrowIfNull(cleanupCommands);

        if (setupCommands.Count == 0
            || cleanupCommands.Count == 0)
        {
            throw new ArgumentException("A scoped migration command requires setup and cleanup commands.");
        }

        var commandText = string.Concat(setupCommands) + bodyCommand + string.Concat(cleanupCommands);

        return CreateScopedLayout(
            commandText,
            setupCommands,
            cleanupCommands,
            MySqlMigrationCommandScopeKind.Handler);
    }

    private static MySqlMigrationCommandLayout CreateScopedLayout(
        string commandText,
        IReadOnlyList<string> setupCommands,
        IReadOnlyList<string> cleanupCommands,
        MySqlMigrationCommandScopeKind scopeKind
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
        var setup = new ReadOnlyMemory<char>[setupCommands.Count];
        var cleanup = new ReadOnlyMemory<char>[cleanupCommands.Count];
        var offset = 0;
        var fragmentIndex = 0;

        for (var index = 0; index < setupCommands.Count; index++)
        {
            var setupCommand = setupCommands[index];

            ValidateFragment(commandText, setupCommand, offset, MySqlMigrationCommandFragmentKind.Setup);
            var commandSlice = commandText.AsMemory(offset, setupCommand.Length);

            setup[index] = commandSlice;
            fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(
                MySqlMigrationCommandFragmentKind.Setup,
                commandSlice);
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

        for (var index = 0; index < cleanupCommands.Count; index++)
        {
            var cleanupCommand = cleanupCommands[index];

            ValidateFragment(commandText, cleanupCommand, offset, MySqlMigrationCommandFragmentKind.Cleanup);
            var commandSlice = commandText.AsMemory(offset, cleanupCommand.Length);

            cleanup[index] = commandSlice;
            fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(
                MySqlMigrationCommandFragmentKind.Cleanup,
                commandSlice);
            offset += cleanupCommand.Length;
        }

        if (offset != commandText.Length)
        {
            throw new InvalidOperationException(
                "Provider-owned migration fragments must cover the complete command text.");
        }

        return new MySqlMigrationCommandLayout(
            commandText,
            Array.AsReadOnly(fragments),
            body,
            Array.AsReadOnly(setup),
            Array.AsReadOnly(cleanup),
            scopeKind);
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
                $"The validated {kind} fragment does not match the aggregate command text.");
        }
    }
}

internal enum MySqlMigrationCommandScopeKind
{
    None,
    ProviderSqlMode,
    Handler,
}
