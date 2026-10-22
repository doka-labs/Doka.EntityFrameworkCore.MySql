namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    private class FirstCustomOperation : MigrationOperation
    {
        /// <summary>
        /// Initializes the first custom operation fixture.
        /// </summary>
        public FirstCustomOperation(
            string payload,
            MigrationOperation? standardOperation = null
        )
        {
            Payload = payload;
            StandardOperation = standardOperation;
        }

        /// <summary>
        /// Gets the handler payload, which must never enter diagnostics.
        /// </summary>
        public string Payload { get; }

        /// <summary>
        /// Gets the provider-owned operation rendered by the test handler.
        /// </summary>
        public MigrationOperation? StandardOperation { get; }
    }

    private sealed class DerivedCustomOperation : FirstCustomOperation
    {
        /// <summary>
        /// Initializes the derived custom operation fixture.
        /// </summary>
        public DerivedCustomOperation() : base("derived") { }
    }

    private sealed class SecondCustomOperation : MigrationOperation;

    private sealed class InvalidResultOperation : MigrationOperation
    {
        /// <summary>
        /// Initializes the invalid result operation fixture.
        /// </summary>
        public InvalidResultOperation(
            int invalidCommandIndex
        )
        {
            InvalidCommandIndex = invalidCommandIndex;
        }

        /// <summary>
        /// Gets the staged command that must fail validation.
        /// </summary>
        public int InvalidCommandIndex { get; }
    }

    private sealed class StatefulResultOperation : MigrationOperation;

    private sealed class ConsumedCustomOperation : MigrationOperation;

    private sealed class MalformedResultOperation : MigrationOperation
    {
        /// <summary>
        /// Initializes the malformed result operation fixture.
        /// </summary>
        public MalformedResultOperation(
            MalformedResultShape shape
        )
        {
            Shape = shape;
        }

        /// <summary>
        /// Gets the inconsistent result shape to construct.
        /// </summary>
        public MalformedResultShape Shape { get; }
    }

    private enum MalformedResultShape
    {
        UndefinedKind,
        GeneratedWithoutCommands,
        ConsumedWithCommands,
    }
}
