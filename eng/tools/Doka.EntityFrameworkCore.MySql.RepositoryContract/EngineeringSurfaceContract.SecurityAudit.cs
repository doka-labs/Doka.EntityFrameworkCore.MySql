namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class EngineeringSurfaceContract
{
    /// <summary>
    /// Keeps active-line security work independent of PRs and expensive compatibility matrices.
    /// </summary>
    /// <param name="workflow">The complete branch-owned security workflow source.</param>
    /// <returns>Contract violations that would silently narrow scheduled audit coverage.</returns>
    public static IReadOnlyList<string> ValidateSecurityAuditWorkflow(
        string workflow
    )
    {
        var errors = new List<string>();
        var triggers = TriggerBlock(workflow);
        if (!triggers.Contains("  workflow_dispatch:\n", StringComparison.Ordinal)
            || !triggers.Contains("- cron: \"35 4 * * *\"", StringComparison.Ordinal)
            || triggers.Contains("pull_request", StringComparison.Ordinal)
            || triggers.Contains("  push:", StringComparison.Ordinal))
        {
            errors.Add("security-audit.yml: security audits require daily/manual execution without another PR gate.");
        }

        var identity = JobBlock(workflow, "schedule-identity");
        if (!identity.Contains("validate-default --workflow security-audit.yml", StringComparison.Ordinal)
            || !identity.Contains("--github-output \"${GITHUB_OUTPUT}\"", StringComparison.Ordinal)
            || !identity.Contains("github.event_name == 'workflow_dispatch' && !inputs.maintenance", StringComparison.Ordinal))
        {
            errors.Add("security-audit.yml: active default, active child and explicit manual audit identities are required.");
        }

        var audit = JobBlock(workflow, "dependency-vulnerability-audit");
        if (!audit.Contains("if: needs.schedule-identity.outputs.run_audit == 'true'", StringComparison.Ordinal)
            || !audit.Contains("needs: schedule-identity", StringComparison.Ordinal)
            || !audit.Contains("global-json-file: global.json", StringComparison.Ordinal)
            || !audit.Contains("run: bash eng/quality/audit-dependencies.sh", StringComparison.Ordinal)
            || !audit.Contains("if: always()", StringComparison.Ordinal)
            || !audit.Contains("path: artifacts/security-audit", StringComparison.Ordinal))
        {
            errors.Add("security-audit.yml: guarded branch-native audits and failure evidence must remain executable.");
        }

        // WHY: Auditing is read-only. Publication identities or write grants in
        // the producer would turn routine dependency inspection into a new trust boundary.
        var coordinator = JobBlock(workflow, "scheduled-release-lines");
        var outsideCoordinator = coordinator.Length > 0
            ? workflow.Replace(coordinator, string.Empty, StringComparison.Ordinal)
            : workflow;

        if (outsideCoordinator.Contains(": write", StringComparison.Ordinal)
            || workflow.Contains("    environment:", StringComparison.Ordinal)
            || workflow.Contains("secrets.", StringComparison.Ordinal)
            || workflow.Contains("continue-on-error:", StringComparison.Ordinal))
        {
            errors.Add("security-audit.yml: security auditing must remain nonpublishing, read-only and fail-closed.");
        }

        return errors;
    }
}
