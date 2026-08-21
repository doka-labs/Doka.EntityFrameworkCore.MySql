namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationCommandListBuilder : MigrationCommandListBuilder
{
    private readonly List<MigrationCommand> _commands = [];
    private IReadOnlyList<string>? _cleanupCommands;
    private MySqlMigrationCommandLayout? _providedLayout;
    private IReadOnlyList<string>? _setupCommands;

    public MySqlMigrationCommandListBuilder(
        MigrationsSqlGeneratorDependencies dependencies
    ) : base(dependencies) { }

    public void BeginProviderScope(
        IReadOnlyList<string> setupCommands
    )
    {
        ArgumentNullException.ThrowIfNull(setupCommands);

        if (_setupCommands is not null
            || setupCommands.Count == 0)
        {
            throw new InvalidOperationException(
                "A provider migration command scope is already active or has no setup commands.");
        }

        _setupCommands = setupCommands;

        foreach (var setupCommand in setupCommands)
        {
            Append(setupCommand);
        }
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

        foreach (var cleanupCommand in cleanupCommands)
        {
            Append(cleanupCommand);
        }
    }

    public void AppendCommandSpec(
        MySqlMigrationCommandSpec command
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_setupCommands is not null
            || _providedLayout is not null)
        {
            throw new InvalidOperationException("A provider migration command scope is already active.");
        }

        _providedLayout = command.ProviderLayout;

        Append(command.CommandText);
        EndCommand(command.TransactionSuppressed);
    }

    public override MigrationCommandListBuilder EndCommand(
        bool suppressTransaction = false
    )
    {
        var commandCount = base.GetCommandList()
            .Count;
        base.EndCommand(suppressTransaction);
        var baseCommands = base.GetCommandList();

        if (baseCommands.Count == commandCount)
        {
            return this;
        }

        var command = baseCommands[^1];
        if (_providedLayout is not null)
        {
            if (!string.Equals(command.CommandText, _providedLayout.CommandText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A provider-rendered command changed before it was appended.");
            }

            if (_providedLayout.IsScoped)
            {
                command = new MySqlScopedMigrationCommand(_providedLayout, Dependencies, suppressTransaction);
            }
        }
        else if (_setupCommands is not null)
        {
            if (_cleanupCommands is null)
            {
                throw new InvalidOperationException(
                    "A provider migration command scope ended without cleanup commands.");
            }

            var layout = MySqlMigrationCommandLayout.CreateScoped(
                command.CommandText,
                _setupCommands,
                _cleanupCommands);

            command = new MySqlScopedMigrationCommand(layout, Dependencies, suppressTransaction);
        }

        _commands.Add(command);
        _providedLayout = null;
        _setupCommands = null;
        _cleanupCommands = null;

        return this;
    }

    public override IReadOnlyList<MigrationCommand> GetCommandList() => _commands;

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
