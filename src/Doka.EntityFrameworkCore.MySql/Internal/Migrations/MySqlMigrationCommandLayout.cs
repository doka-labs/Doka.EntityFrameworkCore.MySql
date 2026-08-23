namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationCommandLayout
{
    private static readonly ReadOnlyCollection<string> s_emptyCommandTexts =
        Array.AsReadOnly(Array.Empty<string>());

    private MySqlMigrationCommandLayout(
        string commandText,
        ReadOnlyCollection<MySqlMigrationCommandFragment> fragments,
        string bodyCommandText,
        ReadOnlyCollection<string> setupCommandTexts,
        ReadOnlyCollection<string> cleanupCommandTexts,
        MySqlMigrationCommandScopeKind scopeKind
    )
    {
        CommandText = commandText;
        Fragments = fragments;
        BodyCommandText = bodyCommandText;
        SetupCommandTexts = setupCommandTexts;
        CleanupCommandTexts = cleanupCommandTexts;
        ScopeKind = scopeKind;
    }

    public string CommandText { get; }

    public IReadOnlyList<MySqlMigrationCommandFragment> Fragments { get; }

    public string BodyCommandText { get; }

    public IReadOnlyList<string> SetupCommandTexts { get; }

    public IReadOnlyList<string> CleanupCommandTexts { get; }

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
            commandText,
            s_emptyCommandTexts,
            s_emptyCommandTexts,
            MySqlMigrationCommandScopeKind.None);
    }

    public static MySqlMigrationCommandLayout CreateHandlerScoped(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands
    ) => CreateScopedFromCommands(
        setupCommands,
        bodyCommand,
        cleanupCommands,
        MySqlMigrationCommandScopeKind.Handler);

    public static MySqlMigrationCommandLayout CreateProviderScoped(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands
    ) => CreateScopedFromCommands(
        setupCommands,
        bodyCommand,
        cleanupCommands,
        MySqlMigrationCommandScopeKind.ProviderSqlMode);

    private static MySqlMigrationCommandLayout CreateScopedFromCommands(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands,
        MySqlMigrationCommandScopeKind scopeKind
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

        var commandText = CreateCommandText(setupCommands, bodyCommand, cleanupCommands);

        return CreateScopedLayout(
            commandText,
            setupCommands,
            bodyCommand,
            cleanupCommands,
            scopeKind);
    }

    private static string CreateCommandText(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands
    )
    {
        var totalLength = bodyCommand.Length;

        for (var index = 0; index < setupCommands.Count; index++)
        {
            totalLength = checked(totalLength + setupCommands[index].Length);
        }

        for (var index = 0; index < cleanupCommands.Count; index++)
        {
            totalLength = checked(totalLength + cleanupCommands[index].Length);
        }

        return string.Create(
            totalLength,
            (Setup: setupCommands, Body: bodyCommand, Cleanup: cleanupCommands),
            static (destination, state) =>
            {
                var offset = CopyCommands(state.Setup, destination, offset: 0);
                state.Body.AsSpan().CopyTo(destination[offset..]);
                offset += state.Body.Length;
                offset = CopyCommands(state.Cleanup, destination, offset);

                if (offset != destination.Length)
                {
                    throw new InvalidOperationException(
                        "Provider-owned migration fragments changed while the command text was created.");
                }
            });
    }

    private static int CopyCommands(
        IReadOnlyList<string> commands,
        Span<char> destination,
        int offset
    )
    {
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];

            command.AsSpan().CopyTo(destination[offset..]);
            offset += command.Length;
        }

        return offset;
    }

    private static MySqlMigrationCommandLayout CreateScopedLayout(
        string commandText,
        IReadOnlyList<string> setupCommands,
        string bodyCommandText,
        IReadOnlyList<string> cleanupCommands,
        MySqlMigrationCommandScopeKind scopeKind
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyCommandText);
        ArgumentNullException.ThrowIfNull(cleanupCommands);

        if (setupCommands.Count == 0
            || cleanupCommands.Count == 0)
        {
            throw new ArgumentException("A scoped migration command requires setup and cleanup commands.");
        }

        var fragments = new MySqlMigrationCommandFragment[setupCommands.Count + cleanupCommands.Count + 1];
        var setupCommandTexts = new string[setupCommands.Count];
        var cleanupCommandTexts = new string[cleanupCommands.Count];
        var offset = 0;
        var fragmentIndex = 0;

        for (var index = 0; index < setupCommands.Count; index++)
        {
            var setupCommand = setupCommands[index];

            ValidateFragment(commandText, setupCommand, offset, MySqlMigrationCommandFragmentKind.Setup);
            var commandSlice = commandText.AsMemory(offset, setupCommand.Length);

            setupCommandTexts[index] = setupCommand;
            fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(
                MySqlMigrationCommandFragmentKind.Setup,
                commandSlice);
            offset += setupCommand.Length;
        }

        ValidateFragment(commandText, bodyCommandText, offset, MySqlMigrationCommandFragmentKind.Body);
        var body = commandText.AsMemory(offset, bodyCommandText.Length);

        fragments[fragmentIndex++] = new MySqlMigrationCommandFragment(MySqlMigrationCommandFragmentKind.Body, body);
        offset += bodyCommandText.Length;

        for (var index = 0; index < cleanupCommands.Count; index++)
        {
            var cleanupCommand = cleanupCommands[index];

            ValidateFragment(commandText, cleanupCommand, offset, MySqlMigrationCommandFragmentKind.Cleanup);
            var commandSlice = commandText.AsMemory(offset, cleanupCommand.Length);

            cleanupCommandTexts[index] = cleanupCommand;
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
            bodyCommandText,
            Array.AsReadOnly(setupCommandTexts),
            Array.AsReadOnly(cleanupCommandTexts),
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
