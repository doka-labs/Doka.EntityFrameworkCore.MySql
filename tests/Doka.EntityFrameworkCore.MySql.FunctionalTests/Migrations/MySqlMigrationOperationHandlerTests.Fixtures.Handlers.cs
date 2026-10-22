namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    private sealed class BaselineRenderingHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.baseline";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <summary>
        /// Gets the context observed by the most recent handler invocation.
        /// </summary>
        public MySqlMigrationOperationContext? LastContext { get; private set; }

        /// <summary>
        /// Gets or sets the my sql migration command spec.
        /// </summary>
        public IReadOnlyList<MySqlMigrationCommandSpec>? LastBaseline { get; private set; }

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            LastContext = context;
            var operation = (FirstCustomOperation)context.Operation;
            var baseline = context.RenderStandardOperation(
                operation.StandardOperation
                ?? new SqlOperation
                {
                    Sql = "SELECT 1;",
                    SuppressTransaction = true,
                });

            LastBaseline = baseline;

            return MySqlMigrationOperationResult.Generated(
                baseline.Append(MySqlMigrationCommandSpec.Create("SELECT 2;")),
                "generated");
        }
    }

    private sealed class SecondHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.second";

        /// <inheritdoc />
        public Type OperationType => typeof(SecondCustomOperation);

        /// <summary>
        /// Gets the context observed by the most recent handler invocation.
        /// </summary>
        public MySqlMigrationOperationContext? LastContext { get; private set; }

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            LastContext = context;

            return MySqlMigrationOperationResult.Generated(
                [MySqlMigrationCommandSpec.Create("SELECT 3;")],
                "generated");
        }
    }

    private sealed class ConsumedHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.consumed";

        /// <inheritdoc />
        public Type OperationType => typeof(ConsumedCustomOperation);

        /// <summary>
        /// Gets the context observed by the most recent handler invocation.
        /// </summary>
        public MySqlMigrationOperationContext? LastContext { get; private set; }

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            LastContext = context;

            return MySqlMigrationOperationResult.Consumed("operation_consumed");
        }
    }

    private sealed class ScopedCommandHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.scoped";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => MySqlMigrationOperationResult.Generated(
            [
                MySqlMigrationCommandSpec.CreateScoped(
                    [
                        "SET @first = 1;\n",
                        "SET @second = 2;\n",
                    ],
                    "SELECT @first + @second;\n",
                    [
                        "SET @first = NULL;\n",
                        "SET @second = NULL;\n",
                    ],
                    transactionSuppressed: true),
            ],
            "generated");
    }

    private sealed class DuplicateIdHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.baseline";

        /// <inheritdoc />
        public Type OperationType => typeof(SecondCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class DuplicateOperationHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.duplicate_operation";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        ) => throw new NotSupportedException();
    }

    private sealed class ThrowingHandler : IMySqlMigrationOperationHandler
    {
        /// <inheritdoc />
        public string HandlerId => "tests.throwing";

        /// <inheritdoc />
        public Type OperationType => typeof(FirstCustomOperation);

        /// <inheritdoc />
        public MySqlMigrationOperationResult Generate(
            MySqlMigrationOperationContext context
        )
        {
            var exception = new InvalidOperationException("password=never-log-this")
            {
                Data =
                {
                    ["private-context"] = "tenant=never-export-this",
                },
            };

            throw exception;
        }
    }
}
