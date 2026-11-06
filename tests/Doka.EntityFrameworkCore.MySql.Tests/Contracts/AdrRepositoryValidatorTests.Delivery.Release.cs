namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Verifies release SBOM input and qualification assembly stay package-bound.</summary>
    [Fact]
    public void Release_candidate_assembles_package_scoped_sbom_and_qualification()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var releaseCandidateScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var packageScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "qualify-packages.sh"));

        var sbomScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "generate-package-sbom.sh"));

        var normalizedPackageScript = NormalizeShellLayout(packageScript);
        var normalizedSbomScript = NormalizeShellLayout(sbomScript);

        // Assert
        Assert.Contains(
            "dotnet tool run sbom-tool --allow-roll-forward -- Generate",
            normalizedSbomScript,
            StringComparison.Ordinal);

        Assert.Contains("-bc \"${sbom_components_dir}\"", normalizedSbomScript, StringComparison.Ordinal);
        Assert.Contains(
            "cp \"${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql/project.assets.json\" "
            + "\"${sbom_components_dir}/runtime/project.assets.json\"",
            normalizedPackageScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "cp \"${repo_root}/artifacts/obj/Doka.EntityFrameworkCore.MySql.NetTopologySuite/project.assets.json\" "
            + "\"${sbom_components_dir}/spatial/project.assets.json\"",
            normalizedPackageScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "cp \"${repo_root}/artifacts/obj/Doka.Caching.MySql/project.assets.json\" "
            + "\"${sbom_components_dir}/cache/project.assets.json\"",
            normalizedPackageScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "--assets \"${cache_assets}\" --project \"${cache_project}\" "
            + "--output-directory \"${repo_root}/artifacts/obj/Doka.Caching.MySql\"",
            normalizedSbomScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "python3 -m eng.release.sbom",
            normalizedSbomScript,
            StringComparison.Ordinal);

        Assert.Equal(
            3,
            normalizedSbomScript.Split(
                    "python3 -m eng.release.sbom")
                .Length
            - 1);

        Assert.DoesNotContain("-bc \"${repo_root}\"", normalizedSbomScript, StringComparison.Ordinal);
        Assert.Contains("eng/testing/qualify-packages.sh", releaseCandidateScript, StringComparison.Ordinal);
        Assert.Contains("eng/testing/generate-package-sbom.sh", releaseCandidateScript, StringComparison.Ordinal);
        Assert.Contains(
            "assemble_qualification_manifest",
            releaseCandidateScript,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies release qualification cannot import independent benchmark evidence.</summary>
    [Theory]
    [InlineData("DOKA_BENCHMARK_RUN_ID")]
    [InlineData("DOKA_BENCHMARK_GATE_RUN_ID")]
    [InlineData("run_paired_performance_gate")]
    [InlineData("paired-evaluation.json")]
    [InlineData("required_performance_targets")]
    [InlineData("DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS")]
    public void Release_candidate_does_not_depend_on_benchmark_qualification(
        string retiredReleaseCoupling
    )
    {
        // Arrange
        var scriptPath = Path.Combine(FindRepositoryRoot(), "eng", "release", "release-candidate.sh");

        // Act
        var releaseCandidateScript = File.ReadAllText(scriptPath);

        // Assert
        // Benchmark evidence is intentionally independent of the release
        // boundary. Keep the candidate script unable to invoke, import, or
        // bypass performance qualification.
        Assert.DoesNotContain(retiredReleaseCoupling, releaseCandidateScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps an isolated build against the exact local package bytes inside the
    /// release-candidate package stage.
    /// </summary>
    [Fact]
    public void Release_candidate_package_stage_builds_a_package_only_consumer()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var packageConsumer = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-local-package-consumer.sh"));

        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var packageQualifier = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "qualify-packages.sh"));

        // Assert
        Assert.Contains("all(.libraries[]; .type != \"project\")", packageConsumer, StringComparison.Ordinal);
        Assert.Contains("restored_provider_sha256", packageConsumer, StringComparison.Ordinal);
        Assert.Contains("restored_spatial_sha256", packageConsumer, StringComparison.Ordinal);
        Assert.Contains("eng/testing/qualify-packages.sh", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains("test-local-package-consumer.sh", packageQualifier, StringComparison.Ordinal);
        Assert.Contains("${local_package_consumer_dir}", packageQualifier, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps ordinary execution, full trimming, trimmed execution, and the
    /// resulting immutable evidence inside every release-candidate contract.
    /// </summary>
    [Fact]
    public void Runtime_posture_gate_is_wired_into_release_candidates()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var runtimePosture = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-runtime-posture.sh"));

        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var releaseEvidence = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "evidence.py"));

        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "quality-gates.yml"));

        var engineQualifier = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "qualify-engine.sh"));

        var runtimeSmokeProject = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "tests",
                "Doka.EntityFrameworkCore.MySql.RuntimeSmoke",
                "Doka.EntityFrameworkCore.MySql.RuntimeSmoke.csproj"));

        // Assert
        Assert.Contains("-p:PublishTrimmed=true", runtimePosture, StringComparison.Ordinal);
        Assert.Contains("-p:TrimMode=full", runtimePosture, StringComparison.Ordinal);
        Assert.Contains(
            "<EnableTrimAnalyzer>true</EnableTrimAnalyzer>",
            runtimeSmokeProject,
            StringComparison.Ordinal);

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
            engineQualifier,
            StringComparison.Ordinal);

        Assert.Contains("validate_runtime_posture", releaseEvidence, StringComparison.Ordinal);
        Assert.Contains("validate_reconciliation", releaseEvidence, StringComparison.Ordinal);
        Assert.Contains("qualify-engine.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("mysql_container_name", runtimePosture, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps explicit package-version arguments safe under the nounset semantics of macOS Bash.
    /// </summary>
    [Fact]
    public void Package_qualification_version_arguments_are_nounset_safe()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var packageQualifier = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "qualify-packages.sh"));

        // Assert
        Assert.Contains(
            "DOKA_RELEASE_VERSION must be an explicit candidate version",
            releaseCandidate,
            StringComparison.Ordinal);

        Assert.Contains("run_with_release_version()", packageQualifier, StringComparison.Ordinal);
        Assert.Contains(
            "command_arguments+=(\"-p:PackageVersion=${release_version_override}\")",
            packageQualifier,
            StringComparison.Ordinal);

        Assert.DoesNotContain("${version_arguments[@]}", releaseCandidate + packageQualifier, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps reversible qualification ahead of tag creation and proves the
    /// protected same-run publication path fails closed around immutable
    /// evidence and public package and symbol readback.
    /// </summary>
    [Fact]
    public void Nuget_publication_requires_a_tag_bound_candidate_and_fresh_consumer_readback()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var entrypoint = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release-candidate.yml"));

        var publisher = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "actions", "publish-release", "action.yml"));

        var workflow = entrypoint + publisher;
        var normalizedWorkflow = NormalizeShellLayout(workflow);
        var publication = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "nuget.py"));

        var symbolReadback = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "eng",
                "tools",
                "Doka.EntityFrameworkCore.MySql.NuGetSymbolReadback",
                "SymbolReadbackManifestBuilder.cs"));

        var readback = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-nuget-readback.sh"));

        var providerPackageStep = workflow.IndexOf("- name: Publish provider package", StringComparison.Ordinal);
        var providerSymbolsStep = workflow.IndexOf("- name: Publish provider symbols", StringComparison.Ordinal);
        var spatialPackageStep = workflow.IndexOf("- name: Publish spatial package", StringComparison.Ordinal);
        var spatialSymbolsStep = workflow.IndexOf("- name: Publish spatial symbols", StringComparison.Ordinal);
        var cachePackageStep = workflow.IndexOf("- name: Publish cache package", StringComparison.Ordinal);
        var cacheSymbolsStep = workflow.IndexOf("- name: Publish cache symbols", StringComparison.Ordinal);
        var publishReleaseStep = workflow.IndexOf(
            "- name: Publish and read back immutable GitHub release",
            StringComparison.Ordinal);

        var cachePackage = workflow[cachePackageStep..cacheSymbolsStep];
        var cacheSymbols = workflow[cacheSymbolsStep..publishReleaseStep];

        // Assert
        Assert.Contains("  workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("      version:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate_run_id:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("release_tag:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmation:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: nuget", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("publisher_environment", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841",
            workflow,
            StringComparison.Ordinal);

        Assert.Contains("Freeze independently authorized release line", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Publication requires the qualified commit to remain on its current authorized line history",
            workflow,
            StringComparison.Ordinal);

        Assert.Contains("git merge-base --is-ancestor", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 -m eng.release.nuget bind", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 -m eng.release.github stage", workflow, StringComparison.Ordinal);
        Assert.Contains("gh attestation verify", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "--signer-workflow \"${GITHUB_REPOSITORY}/.github/workflows/release-candidate.yml\"",
            normalizedWorkflow,
            StringComparison.Ordinal);

        Assert.Contains("--signer-digest \"${GITHUB_SHA}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--source-ref \"${GITHUB_REF}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--source-digest \"${GITHUB_SHA}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--deny-self-hosted-runners", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 -m eng.release.nuget preflight", workflow, StringComparison.Ordinal);
        Assert.Contains("python3 -m eng.release.nuget readback", workflow, StringComparison.Ordinal);
        Assert.Contains("--symbol-manifest", workflow, StringComparison.Ordinal);
        Assert.Contains("--timeout-seconds 3600", workflow, StringComparison.Ordinal);
        Assert.Contains("--poll-interval-seconds 30", workflow, StringComparison.Ordinal);
        Assert.Contains("test-nuget-readback.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.NUGET_API_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}",
            workflow,
            StringComparison.Ordinal);

        Assert.DoesNotContain("--api-key", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--symbol-api-key", workflow, StringComparison.Ordinal);
        Assert.True(
            providerPackageStep >= 0
            && providerSymbolsStep > providerPackageStep
            && spatialPackageStep > providerSymbolsStep
            && spatialSymbolsStep > spatialPackageStep
            && cachePackageStep > spatialSymbolsStep
            && cacheSymbolsStep > cachePackageStep
            && publishReleaseStep > cacheSymbolsStep);

        Assert.Contains(
            "--skip-duplicate",
            workflow[providerPackageStep..providerSymbolsStep],
            StringComparison.Ordinal);

        Assert.Contains(
            "--skip-duplicate",
            workflow[spatialPackageStep..spatialSymbolsStep],
            StringComparison.Ordinal);

        Assert.Equal(
            6,
            workflow.Split("--skip-duplicate")
                .Length
            - 1);

        Assert.Contains("steps.preflight.outputs.cache_published != 'true'", cachePackage, StringComparison.Ordinal);
        Assert.Contains("steps.preflight.outputs.cache_package", cachePackage, StringComparison.Ordinal);
        Assert.Contains("--no-symbols", cachePackage, StringComparison.Ordinal);
        Assert.Contains("--skip-duplicate", cachePackage, StringComparison.Ordinal);
        Assert.Contains(
            "steps.preflight.outputs.cache_symbols_published != 'true'",
            cacheSymbols,
            StringComparison.Ordinal);

        Assert.Contains("steps.preflight.outputs.cache_symbols", cacheSymbols, StringComparison.Ordinal);
        Assert.Contains("--skip-duplicate", cacheSymbols, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-symbols", cacheSymbols, StringComparison.Ordinal);
        Assert.Contains(
            "The qualified candidate is no longer on current remote",
            publication,
            StringComparison.Ordinal);

        Assert.Contains("conflicting bytes", publication, StringComparison.Ordinal);
        Assert.Contains("conflicting symbols", publication, StringComparison.Ordinal);
        Assert.Contains("exact provider release version", publication, StringComparison.Ordinal);
        Assert.Contains(
            "https://symbols.nuget.org/download/symbols",
            symbolReadback,
            StringComparison.Ordinal);

        Assert.Contains(
            "ReadPdbChecksumDebugDirectoryData",
            symbolReadback,
            StringComparison.Ordinal);

        Assert.Contains(
            "restore_sources=(--source \"${package_source}\")",
            readback,
            StringComparison.Ordinal);

        Assert.Contains("--no-cache", readback, StringComparison.Ordinal);
        Assert.Contains("CompiledModelAccessor.cs", readback, StringComparison.Ordinal);
        Assert.Contains("CompiledModels", readback, StringComparison.Ordinal);
    }

    /// <summary>
    /// Characterizes the release workflow DAG and least-privilege boundaries,
    /// including the read scopes required to verify PR qualification.
    /// </summary>
    [Fact]
    public void Release_candidate_preserves_its_stage_and_permission_boundaries()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "release-candidate.yml"));

        var preflight = GetWorkflowJob(workflow, "preflight");
        var qualification = GetWorkflowJob(workflow, "qualification");
        var assemble = GetWorkflowJob(workflow, "assemble");
        var attest = GetWorkflowJob(workflow, "attest");
        var publish = GetWorkflowJob(workflow, "publish");

        // Assert
        Assert.Contains(
            "permissions:\n  contents: read\n\nconcurrency:",
            workflow,
            StringComparison.Ordinal);

        Assert.Contains(
            "    permissions:\n"
            + "      actions: read\n"
            + "      checks: read\n"
            + "      contents: read\n"
            + "      pull-requests: read\n",
            preflight,
            StringComparison.Ordinal);

        Assert.Contains("    needs: preflight\n", qualification, StringComparison.Ordinal);
        Assert.Contains("uses: $/.github/workflows/quality-gates.yml", qualification, StringComparison.Ordinal);
        Assert.Contains(
            "    permissions:\n      actions: read\n      contents: read\n",
            qualification,
            StringComparison.Ordinal);

        Assert.Contains(
            "    needs:\n      - preflight\n      - qualification\n",
            assemble,
            StringComparison.Ordinal);

        Assert.Contains(
            "    permissions:\n"
            + "      actions: read\n"
            + "      checks: read\n"
            + "      contents: read\n"
            + "      pull-requests: read\n",
            assemble,
            StringComparison.Ordinal);

        Assert.Contains("    needs:\n      - preflight\n      - assemble\n", attest, StringComparison.Ordinal);
        Assert.Contains(
            "    permissions:\n"
            + "      actions: read\n"
            + "      attestations: write\n"
            + "      artifact-metadata: write\n"
            + "      contents: read\n"
            + "      id-token: write\n",
            attest,
            StringComparison.Ordinal);

        Assert.Contains(
            "    needs:\n      - preflight\n      - assemble\n      - attest\n",
            publish,
            StringComparison.Ordinal);

        Assert.Contains(
            "    permissions:\n"
            + "      actions: read\n"
            + "      attestations: read\n"
            + "      checks: read\n"
            + "      contents: write\n"
            + "      id-token: write\n"
            + "      pull-requests: read\n",
            publish,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps the unfiltered six-target configuration and failure matrix mandatory for every qualifier.
    /// </summary>
    [Fact]
    public void Integration_configuration_matrix_is_mandatory_for_release_candidates()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "quality-gates.yml"));

        var engineQualifier = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "qualify-engine.sh"));

        var integrationRunner = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-integration.sh"));

        var releaseCandidate = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "release", "release-candidate.sh"));

        var specificationTarget = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "testing", "test-spec-target.sh"));

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

        // Assert
        Assert.Contains("qualify-engine.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("VerificationLane!=FullIntegration", workflow, StringComparison.Ordinal);
        Assert.Contains("validate_full_configuration_matrix", integrationRunner, StringComparison.Ordinal);
        Assert.Contains("The full configuration matrix cannot use", integrationRunner, StringComparison.Ordinal);
        Assert.Contains("Duplicate integration target", integrationRunner, StringComparison.Ordinal);
        Assert.Contains(
            "DOKA_INTEGRATION_TARGETS=\"mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123\"",
            engineQualifier,
            StringComparison.Ordinal);

        Assert.Contains("DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=1", engineQualifier, StringComparison.Ordinal);
        Assert.Contains("eng/testing/test-spec-target.sh", releaseCandidate, StringComparison.Ordinal);
        Assert.Contains(SpecificationFilter, specificationTarget, StringComparison.Ordinal);
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

        Assert.Contains(
            "StartAsync(requests, CancellationToken.None, evidenceScope: \"tls\")",
            tlsFixture,
            StringComparison.Ordinal);

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
}
