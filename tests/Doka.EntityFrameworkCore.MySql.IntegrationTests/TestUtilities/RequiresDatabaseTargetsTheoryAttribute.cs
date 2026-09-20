using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Skips a theory when none of its eligible database targets is selected.</summary>
internal sealed class RequiresDatabaseTargetsTheoryAttribute : TheoryAttribute
{
    /// <summary>Restricts a data-driven contract to the supplied database targets.</summary>
    /// <param name="targets">The engines on which the contract is supported.</param>
    /// <param name="sourceFilePath">The compiler-supplied test source path.</param>
    /// <param name="sourceLineNumber">The compiler-supplied attribute line.</param>
    public RequiresDatabaseTargetsTheoryAttribute(
        IntegrationDatabaseTarget[] targets,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : base(sourceFilePath, sourceLineNumber)
    {
        if (targets is null || targets.Length == 0)
        {
            throw new ArgumentException("At least one integration database target must be provided.", nameof(targets));
        }

        // WHY: Empty MemberData is a failure, even when the current matrix
        // selection intentionally excludes every eligible engine.
        if (!targets.Any(IntegrationTestEnvironment.IsTargetSelected))
        {
            Skip = IntegrationTestEnvironment.GetTargetSelectionSkipReason(targets);
        }
    }
}
