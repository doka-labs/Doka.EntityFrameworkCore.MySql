namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>Counts a late setup or cleanup fragment toward the complete scoped SQL payload limit.</summary>
    /// <param name="oversizedSetup">Whether the oversized final item belongs to setup instead of cleanup.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScopedPayloadLimitIncludesTheFinalSetupOrCleanupItem(
        bool oversizedSetup
    )
    {
        // Arrange
        var oversized = new string('X', 1_048_574);
        string[] setup = oversizedSetup ? ["S", oversized] : ["S"];
        string[] cleanup = oversizedSetup ? ["C"] : ["C", oversized];

        // Act
        var exception = Record.Exception(() => MySqlMigrationCommandSpec.CreateScoped(setup, "B", cleanup));

        // Assert
        var failure = Assert.IsType<ArgumentException>(exception);

        Assert.Equal("bodyCommand", failure.ParamName);
        Assert.Contains("1048576 characters", failure.Message, StringComparison.Ordinal);
    }
}
