namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies generated and consumed handler results and their input boundaries.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>
    /// Verifies generated result snapshots the caller owned command collection.
    /// </summary>
    [Fact]
    public void Generated_result_snapshots_the_caller_owned_command_collection()
    {
        // Arrange
        var commands = new List<MySqlMigrationCommandSpec>
        {
            MySqlMigrationCommandSpec.Create("SELECT 1;", transactionSuppressed: true),
        };

        var result = MySqlMigrationOperationResult.Generated(commands, "generated");

        // Act
        commands.Add(MySqlMigrationCommandSpec.Create("SELECT 2;"));

        // Assert
        var command = Assert.Single(result.Commands);
        Assert.Equal("SELECT 1;", command.CommandText);
        Assert.True(command.TransactionSuppressed);
    }

    /// <summary>
    /// Verifies consumed results share the immutable empty command sequence.
    /// </summary>
    [Fact]
    public void Consumed_results_share_the_immutable_empty_command_sequence()
    {
        // Arrange
        var first = MySqlMigrationOperationResult.Consumed("first_outcome");

        // Act
        var second = MySqlMigrationOperationResult.Consumed("second_outcome");

        // Assert
        Assert.Empty(first.Commands);
        Assert.Same(first.Commands, second.Commands);
        Assert.Equal("first_outcome", first.OutcomeCode);
    }

    /// <summary>
    /// Verifies invalid outcome codes are rejected.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidOutcomeCases))]
    public void Invalid_outcome_codes_are_rejected(
        string? outcomeCode,
        bool consumed
    )
    {
        // Arrange
        Func<object?> act = () => consumed
            ? MySqlMigrationOperationResult.Consumed(outcomeCode!)
            : MySqlMigrationOperationResult.Generated(
                [MySqlMigrationCommandSpec.Create("SELECT 1;")], outcomeCode!);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(recordedException);
    }

    /// <summary>Returns each invalid outcome code independently for both result factories.</summary>
    public static IEnumerable<object?[]> InvalidOutcomeCases =>
        new string?[] { null, "", "   ", "Generated", "generated-value", "1_generated", "generated value", }
            .SelectMany(code => new[] { new object?[] { code, false }, new object?[] { code, true }, });

    /// <summary>
    /// Verifies empty results are rejected.
    /// </summary>
    [Fact]
    public void Empty_results_are_rejected()
    {
        // Arrange
        Action act = () => MySqlMigrationOperationResult.Generated([], "generated");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies null command elements are rejected at every sequence position.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Null_command_elements_are_rejected_at_every_sequence_position(
        int nullIndex
    )
    {
        // Arrange
        var commands = new[]
        {
            MySqlMigrationCommandSpec.Create("SELECT 1;"),
            MySqlMigrationCommandSpec.Create("SELECT 2;"),
            MySqlMigrationCommandSpec.Create("SELECT 3;"),
        };

        commands[nullIndex] = null!;
        Action act = () => MySqlMigrationOperationResult.Generated(commands, "generated");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies outcome codes longer than 64 ascii characters are rejected.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outcome_codes_longer_than_64_ascii_characters_are_rejected(
        bool consumed
    )
    {
        // Arrange
        var outcomeCode = $"g{new string('a', 64)}";
        // Act
        var recordedException = Record.Exception(() => consumed
            ? MySqlMigrationOperationResult.Consumed(outcomeCode)
            : MySqlMigrationOperationResult.Generated(
                [MySqlMigrationCommandSpec.Create("SELECT 1;")], outcomeCode));

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }
}
