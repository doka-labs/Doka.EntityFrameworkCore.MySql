namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal sealed class RequiresDatabaseTargetFactAttribute : FactAttribute
{
    public RequiresDatabaseTargetFactAttribute(
        params IntegrationDatabaseTarget[] targets
    )
    {
        if (targets is null
            || targets.Length == 0)
        {
            throw new ArgumentException("At least one integration database target must be provided.", nameof(targets));
        }

        var selectedTargets = targets
            .Where(IntegrationTestEnvironment.IsTargetSelected)
            .ToArray();

        if (selectedTargets.Length == 0)
        {
            Skip = IntegrationTestEnvironment.GetTargetSelectionSkipReason(targets);
            return;
        }
    }
}
