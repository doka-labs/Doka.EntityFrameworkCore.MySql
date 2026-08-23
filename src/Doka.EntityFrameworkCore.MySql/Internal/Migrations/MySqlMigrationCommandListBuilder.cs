namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationCommandListBuilder : MigrationCommandListBuilder
{
    private readonly List<MigrationCommand> _commands = [];
    private IReadOnlyList<string>? _cleanupCommands;
    private IRelationalCommandBuilder _commandBuilder;
    private IReadOnlyList<string>? _setupCommands;

    public MySqlMigrationCommandListBuilder(
        MigrationsSqlGeneratorDependencies dependencies
    ) : base(dependencies)
    {
        _commandBuilder = dependencies.CommandBuilderFactory.Create();
    }

    public void BeginProviderScope(
        IReadOnlyList<string> setupCommands
    )
    {
        ArgumentNullException.ThrowIfNull(setupCommands);

        if (_setupCommands is not null
            || _commandBuilder.CommandTextLength != 0
            || setupCommands.Count == 0)
        {
            throw new InvalidOperationException(
                "A provider migration command scope is already active, has pending SQL, or has no setup commands.");
        }

        _setupCommands = setupCommands;
    }

    public void CompleteProviderScope(
        IReadOnlyList<string> cleanupCommands
    )
    {
        ArgumentNullException.ThrowIfNull(cleanupCommands);

        if (_setupCommands is null
            || _cleanupCommands is not null
            || cleanupCommands.Count == 0)
        {
            throw new InvalidOperationException(
                "A provider migration command scope is missing, complete, or has no cleanup commands.");
        }

        _cleanupCommands = cleanupCommands;
    }

    public void AppendCommandSpec(
        MySqlMigrationCommandSpec command
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_setupCommands is not null
            || _commandBuilder.CommandTextLength != 0)
        {
            throw new InvalidOperationException(
                "A provider migration command scope is already active or ordinary SQL is pending.");
        }

        if (command.ProviderLayout is { IsScoped: true } layout)
        {
            if (!string.Equals(command.CommandText, layout.CommandText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A provider-rendered command changed before it was appended.");
            }

            _commands.Add(
                new MySqlScopedMigrationCommand(
                    layout,
                    Dependencies,
                    command.TransactionSuppressed));

            return;
        }

        Append(command.CommandText);
        EndCommand(command.TransactionSuppressed);
    }

    public override MigrationCommandListBuilder EndCommand(
        bool suppressTransaction = false
    )
    {
        if (_commandBuilder.CommandTextLength == 0)
        {
            return _setupCommands is not null
                ? throw new InvalidOperationException("A provider migration command scope ended without a body command.")
                : this;
        }

        if (_setupCommands is not null
            && _cleanupCommands is null)
        {
            throw new InvalidOperationException(
                "A provider migration command scope ended without cleanup commands.");
        }

        var relationalCommand = _commandBuilder.Build();
        _commandBuilder = Dependencies.CommandBuilderFactory.Create();
        MigrationCommand command;

        if (_setupCommands is not null)
        {
            var layout = MySqlMigrationCommandLayout.CreateProviderScoped(
                _setupCommands,
                relationalCommand.CommandText,
                _cleanupCommands!);

            command = new MySqlScopedMigrationCommand(
                layout,
                Dependencies,
                suppressTransaction,
                relationalCommand);
        }
        else
        {
            command = new MigrationCommand(
                relationalCommand,
                Dependencies.CurrentContext.Context,
                Dependencies.Logger,
                suppressTransaction);
        }

        _commands.Add(command);
        _setupCommands = null;
        _cleanupCommands = null;

        return this;
    }

    public override IReadOnlyList<MigrationCommand> GetCommandList() => _commands;

    public override MigrationCommandListBuilder Append(
        string value
    )
    {
        _commandBuilder.Append(value);

        return this;
    }

    public override MigrationCommandListBuilder AppendLine()
    {
        _commandBuilder.AppendLine();

        return this;
    }

    public override MigrationCommandListBuilder AppendLine(
        string value
    )
    {
        _commandBuilder.AppendLine(value);

        return this;
    }

    public override MigrationCommandListBuilder AppendLine(
        FormattableString value
    )
    {
        _commandBuilder.AppendLine(value);

        return this;
    }

    public override MigrationCommandListBuilder AppendLines(
        string value
    )
    {
        _commandBuilder.AppendLines(value);

        return this;
    }

    public override IDisposable Indent() => _commandBuilder.Indent();

    public override MigrationCommandListBuilder IncrementIndent()
    {
        _commandBuilder.IncrementIndent();

        return this;
    }

    public override MigrationCommandListBuilder DecrementIndent()
    {
        _commandBuilder.DecrementIndent();

        return this;
    }

    public ReadOnlyCollection<MySqlMigrationCommandSpec> GetCommandSpecs()
    {
        var specs = new MySqlMigrationCommandSpec[_commands.Count];

        for (var index = 0; index < _commands.Count; index++)
        {
            var command = _commands[index];
            var layout = command is MySqlScopedMigrationCommand scopedCommand
                ? scopedCommand.Layout
                : MySqlMigrationCommandLayout.CreateBodyOnly(command.CommandText);

            specs[index] = MySqlMigrationCommandSpec.CreateProviderRendered(
                command.CommandText,
                command.TransactionSuppressed,
                layout);
        }

        return Array.AsReadOnly(specs);
    }
}
