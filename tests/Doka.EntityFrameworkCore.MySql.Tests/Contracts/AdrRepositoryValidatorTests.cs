namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class AdrRepositoryValidatorTests
{
    [Fact]
    public void Repository_decision_corpus_passes()
    {
        var report = AdrRepositoryValidator.Validate(FindRepositoryRoot());

        Assert.True(report.IsValid, FormatErrors(report));
        Assert.Equal(23, report.Documents.Count);
    }

    [Fact]
    public void Every_delivery_path_invokes_the_same_validator()
    {
        var repositoryRoot = FindRepositoryRoot();
        AssertShellGate(Path.Combine(repositoryRoot, "eng", "build.sh"), "dotnet restore");
        AssertShellGate(Path.Combine(repositoryRoot, "eng", "test.sh"), "dotnet build");
        AssertShellGate(Path.Combine(repositoryRoot, "eng", "release-candidate.sh"), "run_specification_gate");
        AssertShellGate(Path.Combine(repositoryRoot, "eng", "quality-gates.sh"), "dotnet format");

        var releaseCandidateScript = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "release-candidate.sh"));
        Assert.Contains(
            "dotnet tool run sbom-tool --allow-roll-forward -- Generate",
            releaseCandidateScript,
            StringComparison.Ordinal);
        Assert.Contains("-bc \"${sbom_components_dir}\"", releaseCandidateScript, StringComparison.Ordinal);
        Assert.Contains("cp \"${runtime_assets}\"", releaseCandidateScript, StringComparison.Ordinal);
        Assert.Contains("cp \"${spatial_assets}\"", releaseCandidateScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-bc \"${repo_root}\"", releaseCandidateScript, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_BENCHMARK_RUN_ID=\"${release_candidate_run_id}\"",
            releaseCandidateScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_BENCHMARK_GATE_RUN_ID=\"${release_candidate_run_id}\"",
            releaseCandidateScript,
            StringComparison.Ordinal);

        var benchmarkGateScript = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "check-benchmark-ratios.sh"));
        Assert.Contains(
            "report_dir=\"${benchmarks_root}/${target}/reports/${run_id}\"",
            benchmarkGateScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "--reports \"${report_dir}\"",
            benchmarkGateScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "--run-id \"${run_id}\"",
            benchmarkGateScript,
            StringComparison.Ordinal);

        var benchmarkScript = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "benchmark.sh"));
        Assert.Contains(
            "\"${compose_command[@]}\" ps -q \"${benchmark_compose_service}\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "compose_command=(docker compose -p \"${compose_project_name}\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains("DOKA_BENCHMARK_DATABASE_PORT", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains("down --volumes --remove-orphans", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains(
            "docker inspect --format '{{.Config.Image}}'",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_BENCHMARK_SERVER_IMAGE",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "host-preflight",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_BENCHMARK_HOST_CPU_UTILIZATION",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "--host \"${host_evidence}\"",
            benchmarkGateScript,
            StringComparison.Ordinal);

        var hostPreflightIndex = benchmarkScript.IndexOf(
            "    run_host_preflight\n",
            StringComparison.Ordinal);
        var workloadMatrixIndex = benchmarkScript.IndexOf(
            "    run_workload_matrix\n",
            StringComparison.Ordinal);
        var tailConfirmationIndex = benchmarkScript.IndexOf(
            "    confirm_historical_tail_if_required\n",
            StringComparison.Ordinal);
        var benchmarkDotNetIndex = benchmarkScript.IndexOf(
            "    run_benchmarkdotnet\n",
            StringComparison.Ordinal);

        Assert.True(
            hostPreflightIndex >= 0
            && workloadMatrixIndex > hostPreflightIndex
            && tailConfirmationIndex > workloadMatrixIndex
            && benchmarkDotNetIndex > tailConfirmationIndex,
            "Provider workloads and targeted tail confirmation must run after "
            + "host preflight and before BenchmarkDotNet adds sustained host load.");
        Assert.Contains("plan-tail-confirmation", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains("merge-tail-confirmations", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains("--workload \"${workload_id}\"", benchmarkScript, StringComparison.Ordinal);

        var workloadRunner = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Doka.EntityFrameworkCore.MySql.Benchmarks",
                "PerformanceWorkloadRunner.cs"));
        Assert.Contains(
            "performance-workload-diagnostic",
            workloadRunner,
            StringComparison.Ordinal);
        Assert.DoesNotContain("benchmark_container_name", benchmarkScript, StringComparison.Ordinal);

        var benchmarkTarget = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Doka.EntityFrameworkCore.MySql.Benchmarks",
                "BenchmarkDatabaseTarget.cs"));
        Assert.Contains("DOKA_BENCHMARK_DATABASE_PORT", benchmarkTarget, StringComparison.Ordinal);

        var performanceIndex = releaseCandidateScript.IndexOf(
            "run_benchmark_and_gate\n",
            StringComparison.Ordinal);
        var repositoryQualityIndex = releaseCandidateScript.IndexOf(
            "run_repository_quality_gate\n",
            StringComparison.Ordinal);
        var repositoryTestIndex = releaseCandidateScript.IndexOf(
            "run_repository_test_gate\n",
            StringComparison.Ordinal);
        Assert.True(
            performanceIndex >= 0
            && repositoryQualityIndex > performanceIndex
            && repositoryTestIndex > repositoryQualityIndex,
            "Release performance must run before build and database-heavy verification "
            + "can contaminate its host snapshot.");
        Assert.Contains("DOKA_BENCHMARK_PORT=0", releaseCandidateScript, StringComparison.Ordinal);

        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        Assert.Contains(
            "- name: Run repository quality gates\n" + "        run: bash eng/quality-gates.sh",
            workflow,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps model drift, bundle lifecycle coverage, and retained evidence wired
    /// into both exhaustive CI and release-candidate paths.
    /// </summary>
    [Fact]
    public void Migration_deployment_gate_is_wired_into_ci_and_release_candidates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modelGate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "check-migration-model.sh"));
        var deploymentGate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "test-migration-deployment.sh"));
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release-candidate.sh"));
        var qualityGates = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "quality-gates.sh"));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("migrations has-pending-model-changes", modelGate, StringComparison.Ordinal);
        Assert.Contains("migrations bundle", deploymentGate, StringComparison.Ordinal);
        Assert.Contains(
            "run_bundle_command \"${connection_string}\" \"${server_version}\" 0",
            deploymentGate,
            StringComparison.Ordinal);
        Assert.Contains("\"mysql84\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("\"mariadb114\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("\"mariadb118\"", deploymentGate, StringComparison.Ordinal);
        Assert.Contains("run_migration_deployment_gate", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT=\"${migration_deployment_root}\"",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"${repo_root}/eng/check-migration-model.sh\"",
            qualityGates,
            StringComparison.Ordinal);
        Assert.Contains("run: bash eng/quality-gates.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("run: bash eng/test-migration-deployment.sh", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps ordinary execution, full trimming, trimmed execution, and the
    /// resulting immutable evidence inside every release-candidate contract.
    /// </summary>
    [Fact]
    public void Runtime_posture_gate_is_wired_into_release_candidates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimePosture = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "test-runtime-posture.sh"));
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release-candidate.sh"));
        var releaseEvidence = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release_evidence.py"));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("-p:PublishTrimmed=true", runtimePosture, StringComparison.Ordinal);
        Assert.Contains("-p:TrimMode=full", runtimePosture, StringComparison.Ordinal);
        Assert.Contains("write_runtime_evidence", runtimePosture, StringComparison.Ordinal);
        Assert.Contains("runtime-posture-evidence.json", runtimePosture, StringComparison.Ordinal);
        Assert.Contains("run_runtime_posture_gate", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains("run_repository_quality_gate", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_QUALITY_AUDIT_DIR=\"${audit_dir}\"",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_RUNTIME_POSTURE_EVIDENCE_DIR=\"${runtime_dir}\"",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.Contains("validate_runtime_posture", releaseEvidence, StringComparison.Ordinal);
        Assert.Contains("validate_reconciliation", releaseEvidence, StringComparison.Ordinal);
        Assert.Contains("DOKA_RUNTIME_TARGET_IMAGE", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("mysql_container_name", runtimePosture, StringComparison.Ordinal);
    }

    /// <summary>
    /// Prevents an omitted local version override from expanding an empty
    /// array under the nounset semantics of the Bash version shipped by macOS.
    /// </summary>
    [Fact]
    public void Release_candidate_optional_version_is_nounset_safe()
    {
        var repositoryRoot = FindRepositoryRoot();
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release-candidate.sh"));

        Assert.Contains("run_with_release_version()", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "command_arguments+=(\"-p:PackageVersion=${release_version_override}\")",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.DoesNotContain("${version_arguments[@]}", releaseCandidate, StringComparison.Ordinal);
    }

    [Fact]
    public void Tiered_ci_preserves_fast_and_exhaustive_verification_lanes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var containerMatrix = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "container-matrix.yml"));
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release-candidate.yml"));
        var dependabot = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "dependabot.yml"));
        const string exhaustiveCondition =
            "if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'";

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

        Assert.Contains(
            $"  migration-deployment:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            $"  efcore-patch-matrix:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            $"  spec-test-suite:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            $"  runtime-posture:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            $"  benchmark-smoke:\n    {exhaustiveCondition}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "&& (github.event_name == 'schedule'\n"
            + "      || github.event_name == 'workflow_dispatch')",
            workflow,
            StringComparison.Ordinal);

        Assert.Contains("cron: \"0 2 * * 2\"", containerMatrix, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", releaseCandidate, StringComparison.Ordinal);

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
    /// Keeps the expensive configuration and failure matrix outside ordinary
    /// pushes while making its unfiltered three-engine evidence mandatory for
    /// every release candidate.
    /// </summary>
    [Fact]
    public void Integration_configuration_matrix_is_mandatory_for_release_candidates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var integrationRunner = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "test-integration.sh"));
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release-candidate.sh"));
        var sqlModeTests = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "tests",
                "Doka.EntityFrameworkCore.MySql.IntegrationTests",
                "Infrastructure",
                "MySqlSqlModeContractTests.cs"));
        var securityTests = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "tests",
                "Doka.EntityFrameworkCore.MySql.IntegrationTests",
                "Infrastructure",
                "MySqlTlsAuthenticationContractTests.cs"));
        var failureTests = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "tests",
                "Doka.EntityFrameworkCore.MySql.IntegrationTests",
                "Infrastructure",
                "MySqlPoolAndFailoverContractTests.cs"));
        var tlsFixture = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "tests",
                "Doka.EntityFrameworkCore.MySql.IntegrationTests",
                "TestUtilities",
                "TlsDatabaseTestGroup.cs"));

        Assert.Contains("DOKA_INTEGRATION_TEST_FILTER", workflow, StringComparison.Ordinal);
        Assert.Contains("VerificationLane!=FullIntegration", workflow, StringComparison.Ordinal);

        Assert.Contains("validate_full_configuration_matrix", integrationRunner, StringComparison.Ordinal);
        Assert.Contains("The full configuration matrix cannot use", integrationRunner, StringComparison.Ordinal);
        Assert.Contains("Duplicate integration target", integrationRunner, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_INTEGRATION_TARGETS=\"mysql84,mariadb114,mariadb118\"",
            releaseCandidate,
            StringComparison.Ordinal);
        Assert.Contains("DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=1", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains("--filter \"Category=Spec|Category=Live\"", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(
            "run_integration_configuration_and_failure_gate",
            releaseCandidate,
            StringComparison.Ordinal);

        Assert.Contains("[Trait(\"Category\", \"ConfigurationContract\")]", sqlModeTests, StringComparison.Ordinal);
        Assert.Contains("[Trait(\"VerificationLane\", \"FullIntegration\")]", sqlModeTests, StringComparison.Ordinal);
        Assert.Contains(
            "[Trait(\"Category\", \"SecurityConfigurationContract\")]",
            securityTests,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Trait(\"VerificationLane\", \"FullIntegration\")]",
            securityTests,
            StringComparison.Ordinal);
        Assert.Contains("StartAsync(requests, evidenceScope: \"tls\")", tlsFixture, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetId = $\"{request.TargetId}-tls\"", tlsFixture, StringComparison.Ordinal);
        Assert.Contains(
            "[Trait(\"Category\", \"FailureConfigurationContract\")]",
            failureTests,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Trait(\"VerificationLane\", \"FullIntegration\")]",
            failureTests,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps local hooks opt-in while sharing one quality-gate implementation
    /// with hosted CI and protecting contributor-owned Git configuration.
    /// </summary>
    [Fact]
    public void Git_hooks_reuse_ci_quality_gates_without_global_configuration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var qualityGates = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "quality-gates.sh"));
        var commitMessage = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "commit-msg"));
        var preCommit = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "pre-commit"));
        var prePush = File.ReadAllText(
            Path.Combine(repositoryRoot, ".githooks", "pre-push"));
        var installer = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "install-git-hooks.sh"));

        Assert.Contains("run: bash eng/quality-gates.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet format", workflow, StringComparison.Ordinal);

        Assert.Contains("dotnet restore \"${solution}\"", qualityGates, StringComparison.Ordinal);
        Assert.Contains("dotnet build \"${solution}\"", qualityGates, StringComparison.Ordinal);
        Assert.Contains("--vulnerable", qualityGates, StringComparison.Ordinal);
        Assert.Contains("examples/*/*.csproj", qualityGates, StringComparison.Ordinal);
        Assert.Contains("eng/check-migration-model.sh", qualityGates, StringComparison.Ordinal);

        Assert.Contains("eng/quality-gates.sh\" --fast", preCommit, StringComparison.Ordinal);
        Assert.Contains("git diff --cached --check", preCommit, StringComparison.Ordinal);
        Assert.Contains("exec \"${repo_root}/eng/quality-gates.sh\"", prePush, StringComparison.Ordinal);
        Assert.DoesNotContain("--fast", prePush, StringComparison.Ordinal);
        Assert.Contains("eng/validate_commit_message.py", commitMessage, StringComparison.Ordinal);

        Assert.Contains("hooks=(commit-msg pre-commit pre-push)", installer, StringComparison.Ordinal);
        Assert.Contains("config --local core.hooksPath", installer, StringComparison.Ordinal);
        Assert.Contains("Refusing to replace contributor-owned hooks", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("config --global", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_line_write_index_validates_and_generates_artifacts()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");

        var exitCode = Program.Main(
        [
            "--root",
            repository.Root,
            "--write-index",
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(repository.Root, AdrIndexRenderer.ReadmeRelativePath)));
        Assert.True(File.Exists(Path.Combine(repository.Root, AdrIndexRenderer.JsonRelativePath)));
    }

    [Fact]
    public void Command_line_returns_validation_failure_for_invalid_repository()
    {
        using var repository = TestRepository.Create();

        var exitCode = Program.Main(
        [
            "--root",
            repository.Root
        ]);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Command_line_help_succeeds(
        string option
    )
    {
        var exitCode = Program.Main([option]);

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--root")]
    public void Command_line_usage_errors_return_two(
        string option
    )
    {
        var exitCode = Program.Main([option]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Valid_repository_and_generated_artifacts_pass()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amendedBy: ["D-002"]);
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", amends: ["D-001"]);
        repository.WriteGeneratedArtifacts();

        var report = repository.Validate();

        Assert.True(report.IsValid, FormatErrors(report));
        Assert.Equal(2, report.Documents.Count);
    }

    [Fact]
    public void Unknown_or_reordered_metadata_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "id: D-001\n",
                "id: D-001\nunknown: value\n",
                StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Metadata keys must appear exactly", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_confirmation_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "### Confirmation\n\n- Run `eng/validate-adrs.sh`.\n",
                "### Confirmation\n\n",
                StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Confirmation' must not be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Asymmetric_relationship_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", amends: ["D-001"]);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("not bidirectional", StringComparison.Ordinal));
    }

    [Fact]
    public void Undated_external_source_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference)");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Invalid source entry", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_source_retrieval_date_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-13-40)");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("invalid retrieval date", StringComparison.Ordinal));
    }

    [Fact]
    public void Wrapped_external_sources_and_reference_links_are_accepted()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source:
            """
            - [Vendor documentation](https://example.com/reference)
              (primary source; retrieved 2026-07-27)
            - [Vendor reference documentation][vendor-reference]
              (primary source; retrieved 2026-07-27)

            [vendor-reference]:
              https://example.com/reference-link
            """);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.True(report.IsValid, FormatErrors(report));
    }

    [Fact]
    public void Undefined_source_reference_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source:
            """
            - [Vendor documentation][missing-reference]
              (primary source; retrieved 2026-07-27)
            """);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("undefined link", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_primary_source_declaration_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Third-party summary](https://example.com/reference) "
            + "(secondary source; retrieved 2026-07-27)");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Invalid source entry", StringComparison.Ordinal));
    }

    [Fact]
    public void Repository_only_marker_cannot_be_mixed_with_external_sources()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- No external sources; repository evidence only.\n"
            + "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-07-27)");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains(
                "cannot be combined with external sources",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Illegal_status_transition_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- 2026-07-27: Decision recorded with status implemented.",
                "- 2026-07-27: Decision recorded with status proposed.\n"
                + "- 2026-07-27: Status changed from proposed to deprecated.",
                StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("is not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_symmetric_option_tradeoff_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- Bad, because the chosen option requires maintenance.\n\n" + "### Rejected option",
                "### Rejected option",
                StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains(
                "Option 'Chosen option' must contain a '- Bad, because",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Stale_generated_index_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteGeneratedArtifacts();
        File.AppendAllText(Path.Combine(repository.Root, AdrIndexRenderer.JsonRelativePath), "stale");

        var report = repository.Validate();

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Generated decision artifact is stale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Deterministic validation", "Deterministic validati\u00F6n", "Non-ASCII byte")]
    [InlineData("---\nid:", "id:", "must begin with YAML front matter")]
    [InlineData(
        "doka-profile-version: \"1.0\"\n---\n\n#",
        "doka-profile-version: \"1.0\"\n\n#",
        "front matter has no closing delimiter")]
    [InlineData("status: implemented", "status implemented", "Metadata must use one flat")]
    [InlineData("id: D-001", "id: D-001\nid: D-001", "Duplicate metadata key")]
    [InlineData("# D-001 -- First decision", "# First decision", "title must use")]
    [InlineData("## Decision Drivers", "## Drivers", "Missing required heading")]
    [InlineData(
        "## Context and Problem Statement",
        "## Context and Problem Statement\n\n" + "Duplicate context.\n\n" + "## Context and Problem Statement",
        "must appear exactly once")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "### Original record metadata\n\n" + "This fixture exercises the repository validator.",
        "Original record metadata")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Status:** Implemented",
        "must not repeat the front matter 'status' key")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Date:** 2026-07-27",
        "must not repeat the front matter 'date' key")]
    [InlineData(
        "This fixture exercises the repository validator.",
        "This fixture exercises the repository validator.\n\n" + "- **Scope:** Test scope",
        "must not repeat the front matter 'scope' key")]
    [InlineData(
        "This fixture exercises the repository validator.\n\n" + "### Re-evaluation Triggers",
        "This fixture exercises the repository validator.\n" + "### Re-evaluation Triggers",
        "headings must be preceded by a blank line")]
    [InlineData("- Deterministic validation\n- Reviewable trade-offs", "", "Decision Drivers' must not be empty")]
    [InlineData("- Rejected option\n", "", "at least two options")]
    [InlineData("- Rejected option\n", "- Chosen option\n", "option titles must be unique")]
    [InlineData(
        "Chosen option: \"Chosen option\", because",
        "Selected option: \"Chosen option\", because",
        "Decision Outcome must contain")]
    [InlineData(
        "Chosen option: \"Chosen option\", because",
        "Chosen option: \"Missing option\", because",
        "must exactly match")]
    [InlineData(
        "- Good, because governance drift fails deterministically.\n",
        "",
        "Consequences must contain a '- Good, because")]
    [InlineData(
        "- Run `eng/validate-adrs.sh`.",
        "Inspect the result manually.",
        "Confirmation must contain at least one bullet")]
    [InlineData("### Rejected option", "#### Rejected option", "Missing trade-off section")]
    [InlineData(
        "- 2026-07-27: Decision recorded with status implemented.",
        "Decision recorded with status implemented.",
        "Decision History must contain at least one dated entry")]
    [InlineData(
        "- 2026-07-27: Decision recorded with status implemented.",
        "- 2026-13-40: Decision recorded with status implemented.",
        "Decision History entry has an invalid date")]
    [InlineData(
        "Decision recorded with status implemented.",
        "Decision captured with status implemented.",
        "Decision History must begin")]
    [InlineData(
        "- `eng/validate-adrs.sh`",
        "`eng/validate-adrs.sh`",
        "Implementation References must contain at least one bullet")]
    [InlineData(
        "The repository needs a deterministic decision contract.",
        "See https://example.com before deciding.",
        "External URLs must be consolidated")]
    [InlineData("- No external sources; repository evidence only.", "", "Sources must contain repository evidence")]
    [InlineData("status: implemented", "status: unknown", "Unsupported status")]
    [InlineData("date: 2026-07-27", "date: 2026-13-40", "Date must use YYYY-MM-DD")]
    [InlineData(
        "decision-makers: [Doka maintainers]",
        "decision-makers: []",
        "Metadata list 'decision-makers' must not be empty")]
    [InlineData("consulted: []", "consulted: Reviewer", "must use inline YAML list syntax")]
    [InlineData("consulted: []", "consulted: [Reviewer, ]", "contains an empty entry")]
    [InlineData("consulted: []", "consulted: [Reviewer, Reviewer]", "contains duplicate entries")]
    [InlineData("scope: \"Test scope\"", "scope: \"\"", "Scope must not be empty")]
    [InlineData("madr-version: \"4.0.0\"", "madr-version: \"3.0.0\"", "madr-version must be pinned")]
    [InlineData(
        "doka-profile-version: \"1.0\"",
        "doka-profile-version: \"2.0\"",
        "doka-profile-version must be pinned")]
    [InlineData("id: D-001", "id: D-002", "identifiers must match")]
    public void Decision_contract_violation_is_rejected(
        string original,
        string replacement,
        string expectedError
    )
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content =>
            {
                Assert.Contains(original, content, StringComparison.Ordinal);
                return content.Replace(original, replacement, StringComparison.Ordinal);
            });

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(report.Errors, error => error.Message.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Out_of_order_required_heading_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content =>
            {
                var contextStart = content.IndexOf("## Context and Problem Statement", StringComparison.Ordinal);
                var driversStart = content.IndexOf("## Decision Drivers", StringComparison.Ordinal);
                var optionsStart = content.IndexOf("## Considered Options", StringComparison.Ordinal);

                Assert.True(contextStart >= 0);
                Assert.True(driversStart > contextStart);
                Assert.True(optionsStart > driversStart);

                var context = content[contextStart..driversStart];
                var drivers = content[driversStart..optionsStart];
                return content[..contextStart] + drivers + context + content[optionsStart..];
            });

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("out of canonical order", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_status_change_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("status: implemented", "status: accepted", StringComparison.Ordinal)
                .Replace(
                    "- 2026-07-27: Decision recorded with status implemented.",
                    "- 2026-07-27: Decision recorded with status proposed.\n"
                    + "- 2026-07-27: Status changed from proposed into accepted.",
                    StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Status changes must use", StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_status_transition_chain_passes()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content.Replace(
                "- 2026-07-27: Decision recorded with status implemented.",
                "- 2026-07-27: Decision recorded with status proposed.\n"
                + "- 2026-07-27: Status changed from proposed to accepted.\n"
                + "- 2026-07-27: Status changed from accepted to implemented.",
                StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.True(report.IsValid, FormatErrors(report));
    }

    [Fact]
    public void Dated_primary_source_passes_syntactic_validation()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            source: "- [Vendor documentation](https://example.com/reference) "
            + "(primary source; retrieved 2026-07-27)");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.True(report.IsValid, FormatErrors(report));
    }

    [Fact]
    public void Invalid_filename_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "Uppercase-slug", title: "First decision");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("filenames must use a lowercase", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_decision_directory_is_rejected()
    {
        using var repository = TestRepository.Create();
        Directory.Delete(Path.Combine(repository.Root, "docs", "decisions"));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Decision directory does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_decision_directory_is_rejected()
    {
        using var repository = TestRepository.Create();

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("No ADR files were found", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_identifier_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-001", slug: "duplicate-decision", title: "Duplicate decision");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Duplicate ADR identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Identifier_gap_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteDecision(id: "D-003", slug: "third-decision", title: "Third decision");

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("identifiers must be contiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void Self_relationship_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amends: ["D-001"]);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("cannot reference the same ADR", StringComparison.Ordinal));
    }

    [Fact]
    public void Relationship_to_missing_decision_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", amends: ["D-002"]);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("references missing ADR", StringComparison.Ordinal));
    }

    [Fact]
    public void Superseded_by_relationship_requires_superseded_status()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision", supersededBy: ["D-002"]);
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", supersedes: ["D-001"]);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("must use status 'superseded'", StringComparison.Ordinal));
    }

    [Fact]
    public void Superseded_status_requires_successor()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            transform: content => content
                .Replace("status: implemented", "status: superseded", StringComparison.Ordinal)
                .Replace("status implemented.", "status superseded.", StringComparison.Ordinal));

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("must identify its successor", StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_bidirectional_supersession_passes()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(
            id: "D-001",
            slug: "first-decision",
            title: "First decision",
            supersededBy: ["D-002"],
            transform: content => content
                .Replace("status: implemented", "status: superseded", StringComparison.Ordinal)
                .Replace("status implemented.", "status superseded.", StringComparison.Ordinal));
        repository.WriteDecision(id: "D-002", slug: "second-decision", title: "Second decision", supersedes: ["D-001"]);

        var report = repository.Validate(validateGeneratedArtifacts: false);

        Assert.True(report.IsValid, FormatErrors(report));
    }

    [Fact]
    public void Missing_generated_artifacts_are_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");

        var report = repository.Validate();

        Assert.Equal(
            2,
            report.Errors.Count(static error => error.Message.Contains(
                "Generated decision artifact is missing",
                StringComparison.Ordinal)));
    }

    [Fact]
    public void Stale_generated_relationship_graph_is_rejected()
    {
        using var repository = TestRepository.Create();
        repository.WriteDecision(id: "D-001", slug: "first-decision", title: "First decision");
        repository.WriteGeneratedArtifacts();
        File.AppendAllText(Path.Combine(repository.Root, AdrIndexRenderer.ReadmeRelativePath), "stale");

        var report = repository.Validate();

        Assert.Contains(
            report.Errors,
            static error => error.Message.Contains("Generated decision artifact is stale", StringComparison.Ordinal));
    }

    private static string FormatErrors(
        AdrValidationReport report
    ) => string.Join(Environment.NewLine, report.Errors);

    private static void AssertFastLaneJob(
        string workflow,
        string jobName
    )
    {
        var jobStart = workflow.IndexOf($"  {jobName}:", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, $"Workflow job '{jobName}' is missing.");

        var runsOn = workflow.IndexOf("    runs-on:", jobStart, StringComparison.Ordinal);
        Assert.True(runsOn > jobStart, $"Workflow job '{jobName}' has no runs-on declaration.");

        Assert.DoesNotContain(
            "\n    if:",
            workflow[jobStart..runsOn],
            StringComparison.Ordinal);
    }

    private static void AssertShellGate(
        string path,
        string firstFollowingCommand
    )
    {
        var script = File.ReadAllText(path);
        var failFast = script.IndexOf("set -euo pipefail", StringComparison.Ordinal);
        var validator = script.IndexOf("\"${repo_root}/eng/validate-adrs.sh\"", StringComparison.Ordinal);
        var followingCommand = validator < 0
            ? -1
            : script.IndexOf(firstFollowingCommand, validator + 1, StringComparison.Ordinal);

        Assert.True(failFast >= 0, $"{path} must enable fail-fast shell behavior.");
        Assert.True(validator > failFast, $"{path} must invoke the ADR validator.");
        Assert.True(followingCommand > validator, $"{path} must validate ADRs before '{firstFollowingCommand}'.");
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

        throw new DirectoryNotFoundException("Unable to locate the Doka.EntityFrameworkCore.MySql repository root.");
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(
            string root
        )
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "docs", "decisions"));
        }

        public string Root { get; }

        public static TestRepository Create() =>
            new(Path.Combine(Path.GetTempPath(), $"doka-adr-tests-{Guid.NewGuid():N}"));

        public void WriteDecision(
            string id,
            string slug,
            string title,
            IReadOnlyList<string>? supersedes = null,
            IReadOnlyList<string>? supersededBy = null,
            IReadOnlyList<string>? amends = null,
            IReadOnlyList<string>? amendedBy = null,
            string source = "- No external sources; repository evidence only.",
            Func<string, string>? transform = null
        )
        {
            var content = $$"""
                            ---
                            id: {{id}}
                            status: implemented
                            date: 2026-07-27
                            decision-makers: [Doka maintainers]
                            consulted: []
                            informed: [Provider contributors]
                            scope: "Test scope"
                            supersedes: {{RenderList(supersedes)}}
                            superseded-by: {{RenderList(supersededBy)}}
                            amends: {{RenderList(amends)}}
                            amended-by: {{RenderList(amendedBy)}}
                            madr-version: "4.0.0"
                            doka-profile-version: "1.0"
                            ---

                            # {{id}} -- {{title}}

                            ## Context and Problem Statement

                            The repository needs a deterministic decision contract.

                            ## Decision Drivers

                            - Deterministic validation
                            - Reviewable trade-offs

                            ## Considered Options

                            - Chosen option
                            - Rejected option

                            ## Decision Outcome

                            Chosen option: "Chosen option", because it satisfies both drivers.

                            ### Consequences

                            - Good, because governance drift fails deterministically.
                            - Bad, because every decision requires structured maintenance.

                            ### Confirmation

                            - Run `eng/validate-adrs.sh`.

                            ## Pros and Cons of the Options

                            ### Chosen option

                            - Good, because it is deterministic.
                            - Bad, because the chosen option requires maintenance.

                            ### Rejected option

                            - Good, because it requires less initial structure.
                            - Bad, because it cannot reject governance drift.

                            ## More Information

                            This fixture exercises the repository validator.

                            ### Re-evaluation Triggers

                            - Re-evaluate when the upstream MADR major version changes.

                            ### Decision History

                            - 2026-07-27: Decision recorded with status implemented.

                            ### Implementation References

                            - `eng/validate-adrs.sh`

                            ### Sources

                            {{source}}
                            """;

            if (transform is not null)
            {
                content = transform(content);
            }

            File.WriteAllText(
                Path.Combine(Root, "docs", "decisions", $"{id}-{slug}.md"),
                content + Environment.NewLine);
        }

        public AdrValidationReport Validate(
            bool validateGeneratedArtifacts = true
        ) => AdrRepositoryValidator.Validate(Root, validateGeneratedArtifacts);

        public void WriteGeneratedArtifacts()
        {
            var report = Validate(validateGeneratedArtifacts: false);
            Assert.True(report.IsValid, FormatErrors(report));
            AdrIndexRenderer.WriteGeneratedArtifacts(Root, report.Documents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string RenderList(
            IReadOnlyList<string>? values
        ) => values is null || values.Count == 0 ? "[]" : $"[{string.Join(", ", values)}]";
    }
}
