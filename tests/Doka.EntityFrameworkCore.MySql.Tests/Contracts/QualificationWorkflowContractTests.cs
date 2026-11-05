namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects complete reusable qualification and its read-only caller boundary.</summary>
public sealed class QualificationWorkflowContractTests
{
    /// <summary>Accepts the delivered common workflow with every qualification producer required.</summary>
    [Fact]
    public void Shared_workflow_satisfies_the_complete_qualification_contract()
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(workflow);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Permits full-line explanations of authority that remain outside qualification.</summary>
    /// <param name="explanation">The authority-related text inside a nonexecuting comment.</param>
    [Theory]
    [InlineData("uses: $/.github/actions/publish-release")]
    [InlineData("uses: ./.github/actions/publish-release")]
    [InlineData("python3 -m eng.release.publication admit")]
    [InlineData("python3 -m eng.release.nuget publish")]
    [InlineData("bash eng/performance/benchmark.sh")]
    [InlineData("bash eng/benchmark.sh")]
    [InlineData("NuGet/login@publisher")]
    [InlineData("dotnet nuget push package.nupkg")]
    [InlineData("git push --tags")]
    [InlineData("secrets.NUGET_API_KEY")]
    [InlineData("secrets: inherit")]
    [InlineData("permissions: { contents: write }")]
    [InlineData("environment: nuget")]
    public void Shared_workflow_accepts_explanatory_authority_comments(
        string explanation
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var changed = workflow.Replace(
            "  workflow_call:\n",
            $"  workflow_call:\n    # {explanation}\n",
            StringComparison.Ordinal);

        changed += $"\n# {explanation}\n    # {explanation}\n          # {explanation}\n";

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Empty(errors);
    }

    /// <summary>Requires both callers to use explicit optional string inputs instead of inherited configuration.</summary>
    /// <param name="input">The reusable workflow input to weaken.</param>
    [Theory]
    [InlineData("version")]
    [InlineData("authorization_artifact")]
    public void Shared_workflow_rejects_untyped_inputs(
        string input
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var declaration = Regex.Match(
                workflow,
                $"^      {input}:\\n(?:(?:        [^\\n]*|)\\n)*",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Value;

        var changed = workflow.Replace(
            declaration,
            declaration.Replace("type: string", "type: boolean", StringComparison.Ordinal),
            StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Contains(errors, error => error.Contains($"{input} must be", StringComparison.Ordinal));
    }

    /// <summary>Rejects privilege expansion even when all qualification jobs remain present.</summary>
    /// <param name="mutation">The authority escalation to inject.</param>
    [Theory]
    [InlineData("contents-write")]
    [InlineData("oidc")]
    [InlineData("flow-permissions")]
    [InlineData("environment")]
    [InlineData("secrets")]
    [InlineData("publisher")]
    [InlineData("benchmark")]
    public void Shared_workflow_rejects_authority_outside_reversible_qualification(
        string mutation
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        const string aggregateStep = "      - name: Require the complete declared successful job set\n";
        var changed = mutation switch
        {
            "contents-write" => workflow.Replace("contents: read", "contents: write", StringComparison.Ordinal),
            "oidc" => workflow.Replace("permissions:\n", "permissions:\n  id-token: write\n", StringComparison.Ordinal),
            "flow-permissions" => workflow.Replace(
                "  required:\n",
                "  required:\n    permissions: { contents: write }\n",
                StringComparison.Ordinal),
            "environment" => workflow.Replace(
                "  required:\n",
                "  required:\n    environment: nuget\n",
                StringComparison.Ordinal),
            "secrets" => workflow.Replace(
                "  required:\n",
                "  required:\n    secrets: inherit\n",
                StringComparison.Ordinal),
            "publisher" => workflow.Replace(
                aggregateStep,
                "      - name: Publish from qualification\n"
                + "        run: python3 -m eng.release.publication admit\n"
                + aggregateStep,
                StringComparison.Ordinal),
            "benchmark" => workflow.Replace(
                aggregateStep,
                "      - name: Benchmark from qualification\n"
                + "        run: bash eng/performance/benchmark.sh\n"
                + aggregateStep,
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"Unknown mutation '{mutation}'."),
        };

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Contains(
            errors,
            static error => error.Contains("read", StringComparison.Ordinal)
                || error.Contains("authority", StringComparison.Ordinal));
    }

    /// <summary>Rejects a publisher action without disturbing the existing job inventory or aggregate checks.</summary>
    /// <param name="reference">The canonical or deprecated same-repository publisher reference.</param>
    [Theory]
    [InlineData("$/.github/actions/publish-release")]
    [InlineData("./.github/actions/publish-release")]
    public void Shared_workflow_rejects_a_publisher_step_inside_the_required_gate(
        string reference
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var required = GetJob(workflow, "required");
        const string aggregateStep = "      - name: Require the complete declared successful job set\n";
        var publisherStep = "      - name: Publish qualified release\n"
            + $"        uses: {reference}\n";

        // WHY: Insert a real action step while leaving every producer and success check intact.
        var changed = workflow.Replace(
            required,
            required.Replace(aggregateStep, publisherStep + aggregateStep, StringComparison.Ordinal),
            StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Equal(
            ".github/workflows/quality-gates.yml: shared qualification must remain reversible "
            + "and free of publication or benchmark authority.",
            Assert.Single(errors));
    }

    /// <summary>Rejects a required check that omits any producer, including package and engine qualification.</summary>
    /// <param name="producer">The dependency removed from the final aggregator.</param>
    [Theory]
    [InlineData("quality-gates")]
    [InlineData("repo-tests")]
    [InlineData("spec-test-suite")]
    [InlineData("integration")]
    [InlineData("foundation")]
    [InlineData("engine-contracts")]
    [InlineData("sbom")]
    [InlineData("coverage-gate")]
    public void Shared_workflow_rejects_omitted_required_producers(
        string producer
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var required = GetJob(workflow, "required");
        var incomplete = required
            .Replace($"      - {producer}\n", string.Empty, StringComparison.Ordinal)
            .Replace($"[{producer}, ", "[", StringComparison.Ordinal)
            .Replace($", {producer}, ", ", ", StringComparison.Ordinal)
            .Replace($", {producer}]", "]", StringComparison.Ordinal);

        var changed = workflow.Replace(required, incomplete, StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Contains(errors, static error => error.Contains("fail closed", StringComparison.Ordinal));
    }

    /// <summary>Rejects skipped aggregators and partial result views even when their dependency lists are complete.</summary>
    /// <param name="original">The delivered fail-closed safeguard.</param>
    /// <param name="replacement">The weakened replacement.</param>
    [Theory]
    [InlineData("if: always()", "if: success()")]
    [InlineData("RESULTS: ${{ toJSON(needs) }}", "RESULTS: '{}'")]
    [InlineData("bash eng/quality/require-qualification.sh", "echo")]
    public void Shared_workflow_rejects_partial_or_skipped_aggregation(
        string original,
        string replacement
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var required = GetJob(workflow, "required");
        var changed = workflow.Replace(
            required,
            required.Replace(original, replacement, StringComparison.Ordinal),
            StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(changed);

        // Assert
        Assert.NotEqual(workflow, changed);
        Assert.Contains(errors, static error => error.Contains("fail closed", StringComparison.Ordinal));
    }

    /// <summary>Prevents silently adding an optional producer outside the declared complete gate inventory.</summary>
    [Fact]
    public void Shared_workflow_rejects_unaggregated_jobs()
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml") + "\n  optional-contract:\n    runs-on: ubuntu-26.04\n";

        // Act
        var errors = EngineeringSurfaceContract.ValidateQualificationWorkflow(workflow);

        // Assert
        Assert.Contains(
            errors,
            static error => error.Contains("exactly the complete required job set", StringComparison.Ordinal));
    }

    /// <summary>Keeps both entrypoints on one qualification implementation without implicit secrets.</summary>
    /// <param name="workflowName">The caller workflow.</param>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release-candidate.yml")]
    public void Callers_delegate_qualification_without_duplicating_producers(
        string workflowName
    )
    {
        // Arrange
        var workflow = ReadWorkflow(workflowName);

        // Act
        var qualification = GetJob(workflow, "qualification");
        var jobs = Regex
            .Matches(
                workflow[workflow.IndexOf("\njobs:\n", StringComparison.Ordinal)..],
                "^  ([a-z][a-z0-9-]*):$",
                RegexOptions.Multiline)
            .Select(static match => match.Groups[1].Value)
            .ToArray();

        // Assert
        Assert.Contains("uses: $/.github/workflows/quality-gates.yml", qualification, StringComparison.Ordinal);
        Assert.Equal(
            1,
            workflow.Split("uses: $/.github/workflows/quality-gates.yml")
                .Length
            - 1);

        Assert.DoesNotContain("secrets:", qualification, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", qualification, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token:", qualification, StringComparison.Ordinal);
        Assert.DoesNotContain("foundation", jobs);
        Assert.DoesNotContain("engine-contracts", jobs);
        Assert.DoesNotContain("sbom", jobs);
    }

    /// <summary>Keeps target-framework selection inside branch-native scripts rather than reusable YAML.</summary>
    [Fact]
    public void Shared_workflow_uses_branch_native_framework_entrypoints()
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");
        var releaseCandidate = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng/release/release-candidate.sh"));

        // Act
        var frameworks = Regex.Matches(workflow, "net(?:10|11)[.]0", RegexOptions.CultureInvariant);

        // Assert
        Assert.Empty(frameworks);
        Assert.Contains("eng/testing/test-spec-target.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/testing/qualify-engine.sh --list", workflow, StringComparison.Ordinal);
        Assert.Contains("eng/testing/qualify-engine.sh\" --all-stages", releaseCandidate, StringComparison.Ordinal);
    }

    /// <summary>Uses one tested checkout for both aggregate validation and the immutable PR tree receipt.</summary>
    [Fact]
    public void Outer_qualification_reuses_the_tested_checkout_for_the_gate_and_receipt()
    {
        // Arrange
        var workflow = ReadWorkflow("ci.yml");

        // Act
        var qualification = GetJob(workflow, "repository-qualification");
        var checkouts = Regex.Matches(qualification, "^        uses: actions/checkout@", RegexOptions.Multiline);
        var checkout = qualification.IndexOf(
            "- name: Checkout the tested source and aggregate contract",
            StringComparison.Ordinal);

        var gate = qualification.IndexOf("run: bash eng/quality/require-qualification.sh", StringComparison.Ordinal);
        var receipt = qualification.IndexOf("qualified_commit=\"$(git rev-parse HEAD)\"", StringComparison.Ordinal);

        // Assert
        Assert.Single(checkouts);
        Assert.True(checkout >= 0 && gate > checkout && receipt > gate);
        Assert.DoesNotContain(
            "if: github.event_name == 'pull_request'",
            qualification[checkout..gate],
            StringComparison.Ordinal);
    }

    /// <summary>Prevents normal manual qualification from repeating independent maintenance producers.</summary>
    /// <param name="job">The standalone maintenance producer.</param>
    [Theory]
    [InlineData("migration-deployment")]
    [InlineData("efcore-patch-matrix")]
    [InlineData("mysqlconnector-patch-matrix")]
    public void Standalone_maintenance_producers_require_the_maintenance_dispatch_input(
        string job
    )
    {
        // Arrange
        var workflow = ReadWorkflow("ci.yml");

        // Act
        var producer = GetJob(workflow, job);

        // Assert
        Assert.Contains(
            "if: >-\n      github.event_name == 'schedule'\n"
            + "      || (github.event_name == 'workflow_dispatch' && inputs.maintenance)\n",
            producer,
            StringComparison.Ordinal);

        Assert.Contains("needs: schedule-identity", producer, StringComparison.Ordinal);
    }

    /// <summary>Retains stage-specific limits and routes direct PR execution through the existing deadline helper.</summary>
    [Fact]
    public void Shared_engine_contracts_preserve_stage_deadlines_for_both_caller_modes()
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");

        // Act
        var engines = GetJob(workflow, "engine-contracts");
        var command = string.Join(
            " ",
            engines
                .Replace("\\\n", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains(
            "timeout-minutes: ${{ matrix.stage == 'runtime' && 40 "
            + "|| matrix.stage == 'mysqlconnector-patch-matrix' && 45 || 60 }}",
            engines,
            StringComparison.Ordinal);

        Assert.Contains(
            "DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS: ${{ matrix.stage == 'runtime' && 2100 "
            + "|| matrix.stage == 'mysqlconnector-patch-matrix' && 2400 || 3300 }}",
            engines,
            StringComparison.Ordinal);

        Assert.Contains(
            "if [[ -n \"${AUTHORIZATION_ARTIFACT}\" ]]; then "
            + "bash ./eng/release-candidate.sh --stage \"${QUALIFICATION_STAGE}\" else "
            + "python3 -m eng.common.deadline --seconds \"${DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS}\" "
            + "--label \"qualification ${QUALIFICATION_STAGE}\" -- "
            + "bash eng/testing/qualify-engine.sh \"${QUALIFICATION_STAGE}\"",
            command,
            StringComparison.Ordinal);
    }

    /// <summary>Applies package and SBOM deadlines to direct PR recipes while retaining direct admitted RC stages.</summary>
    /// <param name="jobName">The reusable workflow job owning the stage.</param>
    /// <param name="stage">The admitted RC stage and deadline label.</param>
    /// <param name="recipe">The direct PR qualification recipe.</param>
    /// <param name="maximumSeconds">The reviewed deadline shared with the RC stage.</param>
    [Theory]
    [InlineData("foundation", "package", "qualify-packages.sh", 2100)]
    [InlineData("sbom", "sbom", "generate-package-sbom.sh", 900)]
    public void Shared_package_and_sbom_jobs_preserve_deadlines_and_exact_recipe_arguments(
        string jobName,
        string stage,
        string recipe,
        int maximumSeconds
    )
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");

        // Act
        var job = GetJob(workflow, jobName);
        var command = string.Join(
            " ",
            job
                .Replace("\\\n", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains(
            $"DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS: '{maximumSeconds}'",
            job,
            StringComparison.Ordinal);

        Assert.Contains(
            "if [[ -n \"${AUTHORIZATION_ARTIFACT}\" ]]; then "
            + "bash ./eng/release-candidate.sh --stage "
            + stage
            + " else "
            + "python3 -m eng.common.deadline --seconds \"${DOKA_RELEASE_CANDIDATE_MAXIMUM_DURATION_SECONDS}\" "
            + "--label \"qualification "
            + stage
            + "\" -- bash eng/testing/"
            + recipe
            + " \"${QUALIFIED_VERSION}\" \"artifacts/release-candidate/${DOKA_RELEASE_CANDIDATE_RUN_ID}\" fi",
            command,
            StringComparison.Ordinal);
    }

    /// <summary>Connects required jobs to the same executable recipes in PR and admitted RC modes.</summary>
    [Fact]
    public void Shared_jobs_execute_the_common_recipes_for_both_caller_modes()
    {
        // Arrange
        var workflow = ReadWorkflow("quality-gates.yml");

        // Act
        var foundation = GetJob(workflow, "foundation");
        var engines = GetJob(workflow, "engine-contracts");
        var sbom = GetJob(workflow, "sbom");
        var integration = GetJob(workflow, "integration");
        var specifications = GetJob(workflow, "spec-test-suite");

        // Assert
        foreach (var job in new[] { foundation, engines, sbom })
        {
            Assert.Contains(
                "AUTHORIZATION_ARTIFACT: ${{ inputs.authorization_artifact }}",
                job,
                StringComparison.Ordinal);

            Assert.Contains("if [[ -n \"${AUTHORIZATION_ARTIFACT}\" ]]; then", job, StringComparison.Ordinal);
        }

        Assert.Contains("bash ./eng/release-candidate.sh --stage package", foundation, StringComparison.Ordinal);
        Assert.Contains(
            "bash eng/testing/qualify-packages.sh \"${QUALIFIED_VERSION}\"",
            foundation,
            StringComparison.Ordinal);

        Assert.Contains(
            "stage: ${{ fromJSON(needs.foundation.outputs.engine_stages) }}",
            engines,
            StringComparison.Ordinal);

        Assert.Contains(
            "bash ./eng/release-candidate.sh --stage \"${QUALIFICATION_STAGE}\"",
            engines,
            StringComparison.Ordinal);

        Assert.Contains(
            "bash eng/testing/qualify-engine.sh \"${QUALIFICATION_STAGE}\"",
            engines,
            StringComparison.Ordinal);

        Assert.Contains("restore-release-stage-artifacts.sh", sbom, StringComparison.Ordinal);
        Assert.Contains("--stage package", sbom, StringComparison.Ordinal);
        Assert.Contains("bash ./eng/release-candidate.sh --stage sbom", sbom, StringComparison.Ordinal);
        Assert.Contains(
            "bash eng/testing/generate-package-sbom.sh \"${QUALIFIED_VERSION}\"",
            sbom,
            StringComparison.Ordinal);

        Assert.Contains("bash eng/testing/qualify-engine.sh integration", integration, StringComparison.Ordinal);
        Assert.Contains("bash eng/testing/test-spec-target.sh", specifications, StringComparison.Ordinal);
        foreach (var target in new[]
            {
                "mysql84",
                "mysql97",
                "mariadb1011",
                "mariadb114",
                "mariadb118",
                "mariadb123",
            })
        {
            Assert.Contains($"target: \"{target}\"", specifications, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("continue-on-error:", workflow, StringComparison.Ordinal);
    }

    private static string ReadWorkflow(
        string name
    ) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", name));

    private static string GetJob(
        string workflow,
        string name
    )
    {
        var jobs = workflow.IndexOf("\njobs:\n", StringComparison.Ordinal);
        var match = Regex.Match(
            workflow[jobs..],
            $"^  {Regex.Escape(name)}:\\n.*?(?=^  [a-z][a-z0-9-]*:|\\z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"Missing workflow job '{name}'.");

        return match.Value;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
