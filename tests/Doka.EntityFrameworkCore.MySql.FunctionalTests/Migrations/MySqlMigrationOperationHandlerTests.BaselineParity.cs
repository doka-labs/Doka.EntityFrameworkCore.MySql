namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationOperationHandlerTests
{
    /// <summary>
    /// Each reserved operation preserves its complete command sequence on each supported engine profile.
    /// </summary>
    [Theory]
    [MemberData(nameof(BaselineOperationCases))]
    public void Baseline_renderer_preserves_reserved_operation_command_sequence(
        string serverVersion,
        Type operationType
    )
    {
        // Arrange
        using var serviceProvider = CreateServiceProvider(
            [typeof(BaselineRenderingHandler)],
            registerBeforeProvider: true);

        using var context = CreateContext(serviceProvider, MySqlServerVersion.Parse(serverVersion));
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateStandardOperationFixtures().Single(candidate => candidate.GetType() == operationType);

        // Act
        var expected = generator.Generate([operation], context.Model);
        var actual = generator.Generate(
            [new FirstCustomOperation("baseline-parity", operation)],
            context.Model);

        // Assert
        Assert.Equal(expected.Count + 1, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].CommandText, actual[index].CommandText);
            Assert.Equal(expected[index].TransactionSuppressed, actual[index].TransactionSuppressed);
        }

        Assert.Equal("SELECT 2;", actual[^1].CommandText);
    }

    /// <summary>
    /// Every provider-reserved operation has exactly one baseline fixture.
    /// </summary>
    [Fact]
    public void Baseline_operation_fixtures_cover_reserved_operation_types_exactly_once()
    {
        // Arrange
        var expected = MySqlStandardMigrationOperations.Types.ToHashSet();

        // Act
        var actual = CreateStandardOperationFixtures().Select(operation => operation.GetType()).ToArray();

        // Assert
        Assert.Equal(expected.Count, actual.Length);
        Assert.True(
            expected.SetEquals(actual),
            "The baseline-renderer fixture set drifted from the reserved operation set.");
        Assert.Equal(actual.Length, actual.Distinct().Count());
    }
}
