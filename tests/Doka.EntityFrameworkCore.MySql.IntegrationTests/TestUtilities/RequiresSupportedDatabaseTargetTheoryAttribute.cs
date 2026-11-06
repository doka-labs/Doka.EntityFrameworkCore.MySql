using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Skips a supported-engine theory when the run selects only a legacy target.
/// </summary>
internal sealed class RequiresSupportedDatabaseTargetTheoryAttribute : TheoryAttribute
{
    /// <summary>Retains the theory's source while applying supported-target selection.</summary>
    /// <param name="sourceFilePath">The compiler-supplied test source path.</param>
    /// <param name="sourceLineNumber">The compiler-supplied attribute line.</param>
    public RequiresSupportedDatabaseTargetTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : base(sourceFilePath, sourceLineNumber)
    {
        var supportedTargets = IntegrationTestEnvironment.GetSupportedTargets();
        var selectedTargets = IntegrationTestEnvironment.GetSelectedTargets();

        // Empty MemberData is a test failure. An
        // explicit mysql80-only run should skip these six-engine contracts.
        if (!selectedTargets.Any(supportedTargets.Contains))
        {
            Skip = IntegrationTestEnvironment.GetTargetSelectionSkipReason(supportedTargets);
        }
    }
}
