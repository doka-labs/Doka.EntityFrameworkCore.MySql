namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies provider-scoped command ordering, coverage, and execution-string ownership.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>
    /// Verifies provider layout preserves exact order and complete coverage.
    /// </summary>
    [Fact]
    public void Provider_layout_preserves_exact_order_and_complete_coverage()
    {
        // Arrange
        var setup = new[] { "SET @scope = 1;\n" };
        var body = "ALTER TABLE `Entries` COMMENT 'safe';\n";
        var cleanup = new[] { "SET @scope = NULL;\n" };
        var commandText = setup[0] + body + cleanup[0];

        // Act
        var layout = MySqlMigrationCommandLayout.CreateProviderScoped(setup, body, cleanup);

        // Assert
        Assert.Collection(
            layout.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
        Assert.Equal(
            commandText,
            string.Concat(layout.Fragments.Select(static fragment => fragment.CommandText.ToString())));
        Assert.Equal("ALTER TABLE `Entries` COMMENT 'safe';\n", layout.BodyCommandText);
        foreach (var fragment in layout.Fragments)
        {
            Assert.True(
                MemoryMarshal.TryGetString(
                    fragment.CommandText,
                    out var backingString,
                    out _,
                    out _));
            Assert.Same(layout.CommandText, backingString);
        }
    }

    /// <summary>
    /// Verifies handler layout retains validated execution strings.
    /// </summary>
    [Fact]
    public void Handler_layout_retains_validated_execution_strings()
    {
        // Arrange
        var setup = new[] { new string('S', 8), new string('T', 8) };
        var body = new string('B', 32);
        var cleanup = new[] { new string('C', 8), new string('D', 8) };

        // Act
        var spec = MySqlMigrationCommandSpec.CreateScoped(setup, body, cleanup);

        // Assert
        var layout = Assert.IsType<MySqlMigrationCommandLayout>(spec.ProviderLayout);
        Assert.Same(setup[0], layout.SetupCommandTexts[0]);
        Assert.Same(setup[1], layout.SetupCommandTexts[1]);
        Assert.Same(body, layout.BodyCommandText);
        Assert.Same(cleanup[1], layout.CleanupCommandTexts[0]);
        Assert.Same(cleanup[0], layout.CleanupCommandTexts[1]);
    }

    /// <summary>
    /// Verifies provider layout rejects an empty body.
    /// </summary>
    [Fact]
    public void Provider_layout_rejects_an_empty_body()
    {
        // Arrange
        Action act = () =>
            MySqlMigrationCommandLayout.CreateProviderScoped(
                ["SET @scope = 1;\n"],
                "   ",
                ["SET @scope = NULL;\n"]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies provider layout requires setup and cleanup.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Provider_layout_requires_setup_and_cleanup(
        bool omitSetup
    )
    {
        // Arrange
        string[] setup = omitSetup ? [] : ["SET @scope = 1;"];
        string[] cleanup = omitSetup ? ["SET @scope = NULL;"] : [];

        // Act
        var recordedException = Record.Exception(() =>
            MySqlMigrationCommandLayout.CreateProviderScoped(setup, "SELECT 1;", cleanup));

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }
}
