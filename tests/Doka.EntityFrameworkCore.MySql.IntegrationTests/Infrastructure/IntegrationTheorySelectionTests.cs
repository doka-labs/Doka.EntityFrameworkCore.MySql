namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies selection-aware theory discovery without opening a database.</summary>
public sealed class IntegrationTheorySelectionTests
{
    /// <summary>A selected eligible engine keeps its theory discoverable.</summary>
    [Fact]
    public void Selected_eligible_target_keeps_theory_enabled()
    {
        // Arrange
        var target = IntegrationTestEnvironment.GetSelectedTargets()[0];

        // Act
        var attribute = new RequiresDatabaseTargetsTheoryAttribute(target);

        // Assert
        Assert.Null(attribute.Skip);
    }

    /// <summary>An empty engine contract is rejected instead of silently skipped.</summary>
    [Fact]
    public void Empty_eligible_targets_are_rejected()
    {
        // Arrange
        IntegrationDatabaseTarget[] targets = [];

        // Act
        var failure = Record.Exception(() => new RequiresDatabaseTargetsTheoryAttribute(targets));

        // Assert
        Assert.IsType<ArgumentException>(failure);
    }

    /// <summary>A null engine contract is rejected instead of silently skipped.</summary>
    [Fact]
    public void Null_eligible_targets_are_rejected()
    {
        // Arrange
        IntegrationDatabaseTarget[] targets = null!;

        // Act
        var failure = Record.Exception(() => new RequiresDatabaseTargetsTheoryAttribute(targets));

        // Assert
        Assert.IsType<ArgumentException>(failure);
    }

    /// <summary>The skip decision reflects the current selection for every declared engine.</summary>
    [Theory]
    [InlineData("MySql80")]
    [InlineData("MySql84")]
    [InlineData("MySql97")]
    [InlineData("MariaDb1011")]
    [InlineData("MariaDb114")]
    [InlineData("MariaDb118")]
    [InlineData("MariaDb123")]
    public void Target_membership_controls_the_skip_decision(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var selected = IntegrationTestEnvironment.IsTargetSelected(target);

        // Act
        var attribute = new RequiresDatabaseTargetsTheoryAttribute(target);

        // Assert
        Assert.Equal(!selected, attribute.Skip is not null);
    }

    /// <summary>One selected target is sufficient even when another eligible target is excluded.</summary>
    [Theory]
    [InlineData("MySql80")]
    [InlineData("MySql84")]
    [InlineData("MySql97")]
    [InlineData("MariaDb1011")]
    [InlineData("MariaDb114")]
    [InlineData("MariaDb118")]
    [InlineData("MariaDb123")]
    public void Any_selected_target_keeps_a_combined_eligibility_set_enabled(
        string additionalTargetName
    )
    {
        // Arrange
        var selectedTarget = IntegrationTestEnvironment.GetSelectedTargets()[0];
        var additionalTarget = Enum.Parse<IntegrationDatabaseTarget>(additionalTargetName);

        // Act
        var attribute = new RequiresDatabaseTargetsTheoryAttribute(selectedTarget, additionalTarget);

        // Assert
        Assert.Null(attribute.Skip);
    }
}
