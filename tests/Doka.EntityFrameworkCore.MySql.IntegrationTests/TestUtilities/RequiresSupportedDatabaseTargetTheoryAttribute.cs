namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Skips a supported-engine theory when the run selects only a legacy target.
/// </summary>
internal sealed class RequiresSupportedDatabaseTargetTheoryAttribute : TheoryAttribute
{
    public RequiresSupportedDatabaseTargetTheoryAttribute()
    {
        var supportedTargets = IntegrationTestEnvironment.GetSupportedTargets();
        var selectedTargets = IntegrationTestEnvironment.GetSelectedTargets();

        // xUnit v2 reports an empty MemberData source as a test failure. An
        // explicit mysql80-only run should skip these six-engine contracts.
        if (!selectedTargets.Any(supportedTargets.Contains))
        {
            Skip = IntegrationTestEnvironment.GetTargetSelectionSkipReason(supportedTargets);
        }
    }
}
