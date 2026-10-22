namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Skips a theory when none of its eligible database targets is selected.</summary>
internal sealed class RequiresDatabaseTargetsTheoryAttribute : TheoryAttribute
{
    /// <summary>Restricts a data-driven contract to the supplied database targets.</summary>
    /// <param name="targets">The engines on which the contract is supported.</param>
    public RequiresDatabaseTargetsTheoryAttribute(
        params IntegrationDatabaseTarget[] targets
    )
    {
        if (targets is null || targets.Length == 0)
        {
            throw new ArgumentException("At least one integration database target must be provided.", nameof(targets));
        }

        // WHY: xUnit v2 treats empty MemberData as a failure, even when the
        // current matrix selection intentionally excludes every eligible engine.
        if (!targets.Any(IntegrationTestEnvironment.IsTargetSelected))
        {
            Skip = IntegrationTestEnvironment.GetTargetSelectionSkipReason(targets);
        }
    }
}
