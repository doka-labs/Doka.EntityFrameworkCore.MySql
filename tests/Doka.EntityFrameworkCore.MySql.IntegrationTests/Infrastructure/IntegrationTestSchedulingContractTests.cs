using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Guards the resource boundaries required by class-level parallel execution.
/// </summary>
public sealed class IntegrationTestSchedulingContractTests
{
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
