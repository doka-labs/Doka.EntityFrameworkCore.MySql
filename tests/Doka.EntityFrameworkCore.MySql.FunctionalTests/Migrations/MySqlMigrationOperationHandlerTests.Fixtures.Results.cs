namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    private sealed class NullResultHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.null_result";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => null!;
    }

    private sealed class InvalidCommandResultHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.invalid_command";

        /// <inheritdoc />
        public Type OperationType => typeof(InvalidResultOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (InvalidResultOperation)context.Operation;
            var commands = new[]
            {
                MySqlMigrationCommandSpec.Create("SELECT 1;"),
                MySqlMigrationCommandSpec.Create("SELECT 2;"),
                MySqlMigrationCommandSpec.Create("SELECT 3;"),
            };

            var commandConstructor = typeof(MySqlMigrationCommandSpec).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(string),
                    typeof(bool),
                ],
                modifiers: null);

            commands[operation.InvalidCommandIndex] = (MySqlMigrationCommandSpec)commandConstructor!.Invoke(
            [
                " ",
                false,
            ]);
            var resultConstructor = typeof(MySqlMigrationOperationResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(IReadOnlyList<MySqlMigrationCommandSpec>),
                    typeof(string),
                    typeof(MySqlMigrationOperationResultKind),
                ],
                modifiers: null);

            // The public factories prevent this shape. Reflection models a
            // hostile plugin so the provider's independent trust-boundary
            // validation and all-or-nothing append remain directly tested.

            return (MySqlMigrationOperationResult)resultConstructor!.Invoke(
            [
                Array.AsReadOnly(commands),
                "generated",
                MySqlMigrationOperationResultKind.Generated,
            ]);
        }
    }

    private sealed class StatefulResultHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.stateful_result";

        /// <inheritdoc />
        public Type OperationType => typeof(StatefulResultOperation);

        /// <summary>
        /// Gets the command sequence observed during validation.
        /// </summary>
        public StatefulCommandList? Commands { get; private set; }

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var commandConstructor = typeof(MySqlMigrationCommandSpec).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(string),
                    typeof(bool),
                ],
                modifiers: null);

            var invalidCommand = (MySqlMigrationCommandSpec)commandConstructor!.Invoke([" ", false]);

            Commands = new StatefulCommandList(
                [
                    MySqlMigrationCommandSpec.Create("SELECT 1;"),
                    MySqlMigrationCommandSpec.Create("SELECT 2;"),
                ],
                [
                    MySqlMigrationCommandSpec.Create("SELECT 1;"),
                    invalidCommand,
                ]);

            var resultConstructor = typeof(MySqlMigrationOperationResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(IReadOnlyList<MySqlMigrationCommandSpec>),
                    typeof(string),
                    typeof(MySqlMigrationOperationResultKind),
                ],
                modifiers: null);

            // A stateful list models the strongest IReadOnlyList adversary: a
            // second enumeration yields different data. The provider must copy
            // once and validate exactly the command snapshot it later appends.

            return (MySqlMigrationOperationResult)resultConstructor!.Invoke(
            [
                Commands,
                "generated",
                MySqlMigrationOperationResultKind.Generated,
            ]);
        }
    }

    private sealed class MalformedResultHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.malformed_result";

        /// <inheritdoc />
        public Type OperationType => typeof(MalformedResultOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var operation = (MalformedResultOperation)context.Operation;
            var commands = operation.Shape == MalformedResultShape.ConsumedWithCommands
                ? Array.AsReadOnly([MySqlMigrationCommandSpec.Create("SELECT 1;")])
                : Array.AsReadOnly(Array.Empty<MySqlMigrationCommandSpec>());

            var kind = operation.Shape switch
            {
                MalformedResultShape.UndefinedKind => (MySqlMigrationOperationResultKind)0,
                MalformedResultShape.GeneratedWithoutCommands => MySqlMigrationOperationResultKind.Generated,
                MalformedResultShape.ConsumedWithCommands => MySqlMigrationOperationResultKind.Consumed,
                _ => throw new InvalidOperationException("Unknown malformed-result test shape."),
            };

            var resultConstructor = typeof(MySqlMigrationOperationResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(IReadOnlyList<MySqlMigrationCommandSpec>),
                    typeof(string),
                    typeof(MySqlMigrationOperationResultKind),
                ],
                modifiers: null);

            return (MySqlMigrationOperationResult)resultConstructor!.Invoke(
            [
                commands,
                "malformed_result",
                kind,
            ]);
        }
    }

    private sealed class StatefulCommandList : IReadOnlyList<MySqlMigrationCommandSpec>
    {
        private readonly IReadOnlyList<MySqlMigrationCommandSpec> _firstEnumeration;
        private readonly IReadOnlyList<MySqlMigrationCommandSpec> _laterEnumerations;

        /// <summary>
        /// Initializes the stateful command list fixture.
        /// </summary>
        public StatefulCommandList(
            IReadOnlyList<MySqlMigrationCommandSpec> firstEnumeration,
            IReadOnlyList<MySqlMigrationCommandSpec> laterEnumerations
        )
        {
            _firstEnumeration = firstEnumeration;
            _laterEnumerations = laterEnumerations;
        }

        /// <summary>
        /// Gets how often the stateful command sequence was enumerated.
        /// </summary>
        public int EnumerationCount { get; private set; }

        /// <inheritdoc />
        public int Count => _firstEnumeration.Count;

        /// <inheritdoc />
        public MySqlMigrationCommandSpec this[
            int index
        ] => _firstEnumeration[index];

        /// <inheritdoc />
        public IEnumerator<MySqlMigrationCommandSpec> GetEnumerator()
        {
            EnumerationCount++;

            return (EnumerationCount == 1 ? _firstEnumeration : _laterEnumerations).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
