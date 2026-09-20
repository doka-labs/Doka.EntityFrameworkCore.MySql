using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal sealed class RequiresDatabaseTargetFactAttribute : FactAttribute
{
    public RequiresDatabaseTargetFactAttribute(
        IntegrationDatabaseTarget target,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : this([target], sourceFilePath, sourceLineNumber) { }

    public RequiresDatabaseTargetFactAttribute(
        IntegrationDatabaseTarget firstTarget,
        IntegrationDatabaseTarget secondTarget,
        IntegrationDatabaseTarget thirdTarget,
        IntegrationDatabaseTarget fourthTarget,
        IntegrationDatabaseTarget fifthTarget,
        IntegrationDatabaseTarget sixthTarget,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : this(
        [firstTarget, secondTarget, thirdTarget, fourthTarget, fifthTarget, sixthTarget],
        sourceFilePath,
        sourceLineNumber)
    { }

    private RequiresDatabaseTargetFactAttribute(
        IntegrationDatabaseTarget[] targets,
        string? sourceFilePath,
        int sourceLineNumber
    ) : base(sourceFilePath, sourceLineNumber)
    {
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
