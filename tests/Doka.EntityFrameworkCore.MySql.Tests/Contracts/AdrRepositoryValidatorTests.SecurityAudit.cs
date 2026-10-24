namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Daily active-line security checks reuse the current branch's SDK and read-only producer.</summary>
    [Fact]
    public void Security_workflow_preserves_daily_branch_native_audit_contract()
    {
        // Arrange
        var workflow = ReadSecurityAuditWorkflow();

        // Act
        var errors = EngineeringSurfaceContract.ValidateSecurityAuditWorkflow(workflow);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Removing an independent security invariant makes the repository contract fail.</summary>
    /// <param name="original">The required workflow fragment to change.</param>
    /// <param name="replacement">A missing or weakened replacement.</param>
    [Theory]
    [InlineData("- cron: \"35 4 * * *\"", "- cron: \"35 4 * * 1\"")]
    [InlineData("  workflow_dispatch:", "  push:")]
    [InlineData("validate-default --workflow security-audit.yml", "echo bypass")]
    [InlineData("--github-output \"${GITHUB_OUTPUT}\"", "")]
    [InlineData("if: needs.schedule-identity.outputs.run_audit == 'true'", "if: true")]
    [InlineData("global-json-file: global.json", "dotnet-version: 12.x")]
    [InlineData("run: bash eng/quality/audit-dependencies.sh", "run: echo skipped")]
    [InlineData("path: artifacts/security-audit", "path: artifacts/unrelated")]
    [InlineData("if: always()", "if: success()")]
    [InlineData("  contents: read", "  contents: write")]
    public void Security_workflow_rejects_missing_or_weakened_invariants(
        string original,
        string replacement
    )
    {
        // Arrange
        var workflow = ReadSecurityAuditWorkflow().Replace(original, replacement, StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateSecurityAuditWorkflow(workflow);

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>No publication privilege, silent failure or new PR gate belongs in the security workflow.</summary>
    /// <param name="fragment">The invalid workflow addition.</param>
    [Theory]
    [InlineData("  pull_request:\n")]
    [InlineData("  pull_request_target:\n")]
    [InlineData("  push:\n")]
    [InlineData("    environment:\n      name: nuget\n")]
    [InlineData("    continue-on-error: true\n")]
    [InlineData("    env:\n      TOKEN: ${{ secrets.NUGET_USER }}\n")]
    [InlineData("    permissions:\n      id-token: write\n")]
    public void Security_workflow_rejects_extra_admission_or_publication_surface(
        string fragment
    )
    {
        // Arrange
        var workflow = fragment.StartsWith("  ", StringComparison.Ordinal)
            && !fragment.StartsWith("    ", StringComparison.Ordinal)
                ? ReadSecurityAuditWorkflow().Replace("on:\n", "on:\n" + fragment, StringComparison.Ordinal)
                : ReadSecurityAuditWorkflow() + fragment;

        // Act
        var errors = EngineeringSurfaceContract.ValidateSecurityAuditWorkflow(workflow);

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>Reads delivered workflow text so regressions cannot hide behind a detached green fixture.</summary>
    private static string ReadSecurityAuditWorkflow()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "security-audit.yml"));
}
