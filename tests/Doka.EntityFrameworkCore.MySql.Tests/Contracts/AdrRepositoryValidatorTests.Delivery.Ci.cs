namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Verifies every hosted job uses the deliberately selected Ubuntu image.</summary>
    /// <param name="fileName">The checked-in workflow to inspect.</param>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("benchmark.yml")]
    [InlineData("container-matrix.yml")]
    [InlineData("release-candidate.yml")]
    [InlineData("security-audit.yml")]
    [InlineData("dependency-review.yml")]
    [InlineData("scorecard.yml")]
    public void Hosted_workflows_use_the_explicit_ubuntu_runner(
        string fileName
    )
    {
        // Arrange
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", fileName);

        // Act
        var workflow = File.ReadAllText(workflowPath);
        var runners = workflow.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("runs-on:", StringComparison.Ordinal))
            .Select(static line => line["runs-on:".Length..].Trim())
            .ToArray();

        // Assert
        Assert.NotEmpty(runners);
        Assert.All(runners, static runner => Assert.Equal("ubuntu-26.04", runner));
    }

    /// <summary>Verifies benchmark evidence names the image that executes the workload.</summary>
    [Fact]
    public void Benchmark_runner_identity_matches_its_hosted_image()
    {
        // Arrange
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "benchmark.yml");

        // Act
        var workflow = File.ReadAllText(workflowPath);
        var benchmark = GetWorkflowJob(workflow, "benchmark");
        var runner = benchmark.Split('\n')
            .Select(static line => line.Trim())
            .Single(static line => line.StartsWith("runs-on:", StringComparison.Ordinal))
            ["runs-on:".Length..].Trim();

        // Assert
        Assert.Contains(
            $"DOKA_BENCHMARK_RUNNER_CLASS: github-{runner}-x64",
            benchmark,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies CI invokes the shared repository quality-gate entrypoint.</summary>
    [Fact]
    public void Ci_uses_the_repository_quality_gate_entrypoint()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        // Assert
        Assert.Contains(
            "- name: Run repository quality gates\n" + "        run: bash eng/quality-gates.sh",
            workflow,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps merge-authoritative product qualification on pull requests and
    /// prevents equivalent automatic work from running again after the merge.
    /// </summary>
    [Fact]
    public void Merge_authoritative_workflows_do_not_repeat_on_main()
    {
        // Arrange
        var workflowRoot = Path.Combine(FindRepositoryRoot(), ".github", "workflows");

        // Act
        var ci = File.ReadAllText(Path.Combine(workflowRoot, "ci.yml"));
        var benchmark = File.ReadAllText(Path.Combine(workflowRoot, "benchmark.yml"));
        var scorecard = File.ReadAllText(Path.Combine(workflowRoot, "scorecard.yml"));
        var ciTriggers = GetWorkflowTriggers(ci);
        var benchmarkTriggers = GetWorkflowTriggers(benchmark);
        var scorecardTriggers = GetWorkflowTriggers(scorecard);
        var qualification = GetWorkflowJob(ci, "repository-qualification");

        // Assert
        Assert.Contains("  pull_request:\n", ciTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:\n", ciTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("  merge_group:\n", ciTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("  main-admission:\n", ci, StringComparison.Ordinal);
        foreach (var requiredGate in new[]
                 {
                     "quality-gates",
                     "repo-tests",
                     "spec-test-suite",
                     "integration-smoke",
                     "coverage-gate",
                 })
        {
            Assert.Contains($"      - {requiredGate}\n", qualification, StringComparison.Ordinal);
        }
        Assert.Contains(
            "Checkout tested pull-request merge ref",
            qualification,
            StringComparison.Ordinal);
        Assert.Contains(
            "test \"${DOKA_QUALIFIED_REF}\" = \"${expected_ref}\"",
            qualification,
            StringComparison.Ordinal);
        Assert.Contains(
            "test \"${qualified_commit}\" = \"${GITHUB_SHA}\"",
            qualification,
            StringComparison.Ordinal);
        Assert.Contains(
            "echo \"tree=${qualified_tree}\" >> \"${GITHUB_OUTPUT}\"",
            qualification,
            StringComparison.Ordinal);
        Assert.Contains(
            "name: release-stage-repository-qualification-"
            + "${{ steps.repository-qualification.outputs.tree }}-attempt-"
            + "${{ github.run_attempt }}",
            qualification,
            StringComparison.Ordinal);
        Assert.Contains("retention-days: 30", qualification, StringComparison.Ordinal);
        Assert.Contains("  workflow_dispatch:\n", benchmarkTriggers, StringComparison.Ordinal);
        Assert.Contains("  schedule:\n", benchmarkTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:\n", benchmarkTriggers, StringComparison.Ordinal);
        Assert.Contains("  workflow_dispatch:\n", scorecardTriggers, StringComparison.Ordinal);
        Assert.Contains("  schedule:\n", scorecardTriggers, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:\n", scorecardTriggers, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies tiered ci preserves fast and exhaustive verification lanes.
    /// </summary>
    [Fact]
    public void Tiered_ci_preserves_fast_and_exhaustive_verification_lanes()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        var containerMatrix = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "container-matrix.yml"));

        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release-candidate.yml"));

        var dependabot = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "dependabot.yml"));

        const string exhaustiveCondition =
            "if: >-\n"
            + "      github.event_name == 'schedule'\n"
            + "      || github.event_name == 'workflow_dispatch'";

        var integrationStart = workflow.IndexOf(
            "  integration-smoke:",
            StringComparison.Ordinal);

        var repositoryQualificationStart = workflow.IndexOf(
            "  repository-qualification:",
            integrationStart,
            StringComparison.Ordinal);

        var integration = workflow[integrationStart..repositoryQualificationStart];
        // The required aggregator is the one job that must run under always():
        // GitHub reports a skipped job as success, so a check that merely
        // depended on the gates would pass in exactly the cases it exists to
        // catch. It inspects every producer result explicitly instead, which is
        // what makes it fail closed. Every other job still has to state a real
        // condition, so the prohibition holds outside that job.
        var aggregator = GetWorkflowJob(workflow, "repository-qualification");
        var outsideAggregator = workflow.Replace(aggregator, string.Empty, StringComparison.Ordinal);
        var releasePreflight = GetWorkflowJob(releaseCandidate, "preflight");

        // Assert
        Assert.Contains(
            "schedule:\n    - cron: \"15 1 * * 4\"",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "${{ github.workflow }}-${{ github.event_name }}-"
            + "${{ github.head_ref || github.ref }}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
        AssertFastLaneJob(workflow, "quality-gates");
        AssertFastLaneJob(workflow, "repo-tests");
        AssertFastLaneJob(workflow, "integration-smoke");
        // Specification conformance and coverage are part of every PR and
        // explicit CI execution. Expensive patch matrices remain scheduled or
        // manual and are intentionally outside the merge qualification.
        Assert.DoesNotContain(
            $"  spec-test-suite:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"  coverage-gate:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        AssertFastLaneJob(workflow, "spec-test-suite");
        AssertFastLaneJob(workflow, "coverage-gate");
        Assert.Contains("--filter \"Category=Spec|Category=Live\"", workflow, StringComparison.Ordinal);
        Assert.Equal(
            1,
            workflow.Split("--filter \"Category=Spec|Category=Live\"", StringSplitOptions.None)
                .Length
            - 1);
        Assert.Contains(
            $"  migration-deployment:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            $"  efcore-patch-matrix:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"  runtime-posture:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "bash ./eng/test-runtime-posture.sh --test-only",
            integration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("  benchmark-smoke:\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "    if: >-\n      always()",
            outsideAggregator,
            StringComparison.Ordinal);
        Assert.Contains(
            "always()",
            aggregator,
            StringComparison.Ordinal);
        Assert.Contains(
            "Require every commit-exact gate to have succeeded",
            aggregator,
            StringComparison.Ordinal);
        Assert.Contains(
            "Require successful coverage producers",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("cron: \"0 2 * * 2\"", containerMatrix, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "Verify imported repository qualification",
            releasePreflight,
            StringComparison.Ordinal);
        Assert.Contains("--qualification-only", releasePreflight, StringComparison.Ordinal);
        Assert.Contains("  foundation:\n    needs: preflight", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "groups:\n      github-actions:\n        patterns:\n          - \"*\"\n"
            + "    open-pull-requests-limit: 1",
            dependabot,
            StringComparison.Ordinal);
        Assert.Contains(
            "groups:\n      nuget-dependencies:\n        patterns:\n          - \"*\"\n"
            + "    ignore:",
            dependabot,
            StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-dependencies:", dependabot, StringComparison.Ordinal);
        Assert.DoesNotContain("example-dependencies:", dependabot, StringComparison.Ordinal);
        Assert.DoesNotContain("test-infrastructure:", dependabot, StringComparison.Ordinal);
        Assert.Contains("version-update:semver-patch", dependabot, StringComparison.Ordinal);
        Assert.Contains(
            "version-update:semver-patch\n    open-pull-requests-limit: 1",
            dependabot,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps maintenance dispatch nonpublishing and branch-native without
    /// adding another required pull-request qualification gate.
    /// </summary>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("container-matrix.yml")]
    [InlineData("benchmark.yml")]
    public void Scheduled_lines_dispatch_branch_native_producers(
        string workflowName
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", workflowName);

        // Act
        var workflow = File.ReadAllText(workflowPath);
        var coordinator = GetWorkflowJob(workflow, "scheduled-release-lines");
        var guard = GetWorkflowJob(workflow, "schedule-identity");
        var ordinaryJobs = workflow.Replace(coordinator, string.Empty, StringComparison.Ordinal);

        // Assert
        Assert.Contains("# release-line-schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("if: github.event_name == 'schedule'", coordinator, StringComparison.Ordinal);
        Assert.Contains($"schedules.py run --workflow {workflowName}", coordinator, StringComparison.Ordinal);
        Assert.Contains("actions: write", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("actions: write", ordinaryJobs, StringComparison.Ordinal);
        Assert.Contains($"validate-child --workflow {workflowName}", guard, StringComparison.Ordinal);
        Assert.Contains("inputs.expected_commit", guard, StringComparison.Ordinal);
        Assert.Contains("inputs.schedule_correlation", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ref: ${{ inputs.", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps local hooks opt-in while sharing one quality-gate implementation
    /// with hosted CI and protecting contributor-owned Git configuration.
    /// </summary>
    [Fact]
    public void Git_hooks_reuse_ci_quality_gates_without_global_configuration()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        var qualityGates = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "quality", "quality-gates.sh"));

        var commitMessage = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "commit-msg"));

        var preCommit = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "pre-commit"));

        var prePush = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "pre-push"));

        var installer = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "development", "install-git-hooks.sh"));

        // Assert
        Assert.Contains("run: bash eng/quality-gates.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet format", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet restore \"${solution}\"", qualityGates, StringComparison.Ordinal);
        Assert.Contains("dotnet build \"${solution}\"", qualityGates, StringComparison.Ordinal);
        Assert.Contains("--vulnerable", qualityGates, StringComparison.Ordinal);
        Assert.Contains("audit_project \"${cache_project}\" \"cache\"", qualityGates, StringComparison.Ordinal);
        Assert.Contains("src/Doka.Caching.MySql/Doka.Caching.MySql.csproj", qualityGates, StringComparison.Ordinal);
        Assert.Contains("examples/*/*.csproj", qualityGates, StringComparison.Ordinal);
        Assert.Contains("eng/quality/check-migration-model.sh", qualityGates, StringComparison.Ordinal);
        Assert.Contains("eng/quality-gates.sh\" --fast", preCommit, StringComparison.Ordinal);
        Assert.Contains("git diff --cached --check", preCommit, StringComparison.Ordinal);
        Assert.Contains("exec \"${repo_root}/eng/quality-gates.sh\"", prePush, StringComparison.Ordinal);
        Assert.DoesNotContain("--fast", prePush, StringComparison.Ordinal);
        Assert.Contains("eng/quality/commit-message.sh", commitMessage, StringComparison.Ordinal);
        Assert.Contains("hooks=(commit-msg pre-commit pre-push)", installer, StringComparison.Ordinal);
        Assert.Contains("config --local core.hooksPath", installer, StringComparison.Ordinal);
        Assert.Contains("Refusing to replace contributor-owned hooks", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("config --global", installer, StringComparison.Ordinal);
    }
}
