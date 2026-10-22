using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Guards the resource boundaries required by class-level parallel execution.
/// </summary>
public sealed class IntegrationTestSchedulingContractTests
{
    /// <summary>Supplies only the supported engines selected for this test run.</summary>
    public static IEnumerable<object[]> SelectedSupportedTargets =>
        IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(IntegrationTestEnvironment.GetSupportedTargets().Contains)
            .Select(target => new object[] { target.ToString() });

    /// <summary>
    /// Guards the selected-target theory discovery used by the split live tests.
    /// </summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedSupportedTargets))]
    public void Selected_supported_target_theory_reports_the_selected_engine(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);

        // Act
        var isSelected = IntegrationTestEnvironment.IsTargetSelected(target);

        // Assert
        Assert.True(isSelected);
        Assert.Contains(target, IntegrationTestEnvironment.GetSupportedTargets());
    }

    /// <summary>Verifies database classes run in bounded parallel collections.</summary>
    [Fact]
    public void Database_classes_run_in_bounded_parallel_collections()
    {
        // Arrange
        var assembly = typeof(IntegrationTestSchedulingContractTests).Assembly;

        // Act
        var behavior = assembly.GetCustomAttribute<CollectionBehaviorAttribute>();
        var ordinaryCollection = typeof(MySqlPoolAndFailoverContractTests)
            .GetCustomAttribute<CollectionAttribute>();

        // Assert
        Assert.NotNull(behavior);
        Assert.False(behavior.DisableTestParallelization);
        Assert.Equal(4, behavior.MaxParallelThreads);
        Assert.Null(ordinaryCollection);
    }

    /// <summary>Verifies process wide pool reset is exclusive.</summary>
    [Fact]
    public void Process_wide_pool_reset_is_exclusive()
    {
        // Arrange
        var resetClass = typeof(MySqlDriverCompatibilityTests);
        var collectionDefinition = typeof(ProcessWideIntegrationTestGroup);

        // Act
        var membership = resetClass.GetCustomAttributesData()
            .Single(attribute => attribute.AttributeType == typeof(CollectionAttribute));

        var definition = collectionDefinition.GetCustomAttribute<CollectionDefinitionAttribute>();

        // Assert
        Assert.Equal(ProcessWideIntegrationTestGroup.Name, membership.ConstructorArguments[0].Value);
        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization);
    }
}
