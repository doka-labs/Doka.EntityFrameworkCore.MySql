namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class QualificationStageExecutionTests
{
    private const string EfContracts =
        "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts";

    private const string EfProbeEvidence = "artifacts/efcore-patch-matrix/probe";

    private static readonly string[] s_efPackages =
    [
        "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Relational.Specification.Tests",
    ];

    private static readonly string[] s_efTestMethods = ["Base.Test"];

    private static readonly string[] s_efFixtureTypes = ["Fixture"];

    /// <summary>Reports a genuinely new patch without producing qualification evidence or running tests.</summary>
    /// <param name="resolved">The new patch selected by the floating restore.</param>
    /// <param name="reviewed">The highest patch whose contracts have been reviewed.</param>
    [Theory]
    [InlineData("10.0.15", "10.0.14")]
    [InlineData("10.0.100", "10.0.99")]
    [InlineData("10.0.10", "10.0.9")]
    public void Ef_upstream_probe_reports_new_patch_without_qualifying_it(
        string resolved,
        string reviewed
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository(resolved, reviewed);
        repository.Write("artifacts/efcore-patch-matrix/probe/efcore-contract-evidence.json", "stale-success");

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));

        var root = report.RootElement;
        Assert.Equal("not-qualified", root.GetProperty("qualificationStatus").GetString());
        Assert.Equal("upstream-probe", root.GetProperty("validationScope").GetString());
        Assert.Equal(resolved, root.GetProperty("resolvedVersion").GetString());
        Assert.Equal(reviewed, root.GetProperty("highestReviewedVersion").GetString());
        Assert.Empty(root.GetProperty("specificationTargets").EnumerateArray());
        Assert.Empty(root.GetProperty("integrationTargets").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "efcore-contract-evidence.json")));
        Assert.True(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "resolved-packages.json")));
        Assert.Contains(
            "not qualified",
            File.ReadAllText(Path.Combine(repository.Root, "job-summary.md")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            repository.Events,
            static entry => entry is "repository-tests" or "integration"
                || entry.StartsWith("test|", StringComparison.Ordinal));
    }

    /// <summary>Retains full repository, live, specification and integration execution for admitted patches.</summary>
    /// <param name="resolved">The already admitted patch selected by restore.</param>
    [Theory]
    [InlineData("10.0.14")]
    [InlineData("10.0.8")]
    public void Ef_upstream_probe_runs_full_matrix_for_admitted_patch(
        string resolved
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository(resolved, "10.0.14");
        WriteEfContracts(repository, "10.0.14", "10.0.8");

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("repository-tests", repository.Events);
        Assert.Contains("test|mysql84", repository.Events);
        Assert.Contains("test|mariadb118", repository.Events);
        Assert.Contains("integration", repository.Events);
        using var receipt = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repository.Root, EfProbeEvidence, "efcore-contract-evidence.json")));

        Assert.Equal("full", receipt.RootElement.GetProperty("validationScope").GetString());
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
    }

    /// <summary>Uses the same explicit contract root for admission classification and strict validation.</summary>
    [Fact]
    public void Ef_upstream_probe_honors_the_effective_contract_root()
    {
        // Arrange
        using var repository = CreateEfMatrixRepository("10.0.15", "10.0.14");
        WriteEfContracts(repository, "10.0.14", "10.0.15");
        var alternate = Path.Combine(repository.Root, "alternate-contracts");
        Directory.Move(Path.Combine(repository.Root, EfContracts), alternate);
        WriteEfContracts(repository, "10.0.14");
        repository.Environment["DOKA_SPEC_CONTRACTS_ROOT"] = alternate;

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("repository-tests", repository.Events);
        Assert.Contains("integration", repository.Events);
        Assert.True(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "efcore-contract-evidence.json")));
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
    }

    /// <summary>Keeps unregistered patches fatal for every qualification scope, including the default.</summary>
    /// <param name="scope">The explicit scope or null to exercise the default.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("full")]
    [InlineData("dependency-graph")]
    public void Ef_qualification_never_admits_an_unregistered_patch(
        string? scope
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository("10.0.15", "10.0.14");
        repository.Environment["DOKA_EF_CORE_VALIDATION_SCOPE"] = scope ?? string.Empty;

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No committed specification contract", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
        Assert.DoesNotContain("repository-tests", repository.Events);
    }

    /// <summary>Rejects broken admissions, missing older patches and damaged reviewed contracts.</summary>
    /// <param name="mutation">The broken contract state to introduce.</param>
    [Theory]
    [InlineData("orphan-inventory")]
    [InlineData("orphan-discovery")]
    [InlineData("dangling-inventory")]
    [InlineData("entry-only-version")]
    [InlineData("listed-version")]
    [InlineData("missing-reviewed-inventory")]
    [InlineData("missing-reviewed-discovery")]
    [InlineData("invalid-reviewed-inventory")]
    [InlineData("invalid-reviewed-discovery")]
    [InlineData("invalid-baseline")]
    [InlineData("empty-baseline")]
    [InlineData("older-unregistered-patch")]
    public void Ef_upstream_probe_rejects_incomplete_or_corrupt_contracts(
        string mutation
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository(
            mutation == "older-unregistered-patch" ? "10.0.9" : "10.0.15",
            "10.0.14");

        var contractRoot = Path.Combine(repository.Root, EfContracts);
        var baselinePath = Path.Combine(contractRoot, "SpecSuiteBaseline.json");
        switch (mutation)
        {
            case "orphan-inventory":
                repository.Write($"{EfContracts}/SpecSuiteInventory.10.0.15.json", "{}");
                break;
            case "orphan-discovery":
                repository.Write($"{EfContracts}/SpecDiscovery.10.0.15.json", "{}");
                break;
            case "dangling-inventory":
                File.CreateSymbolicLink(Path.Combine(contractRoot, "SpecSuiteInventory.10.0.15.json"), "absent.json");
                break;
            case "entry-only-version":
            case "listed-version":
                var baseline = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(baselinePath))!;
                var versions = mutation == "listed-version"
                    ? baseline["efCoreVersions"]!.AsArray()
                    : baseline["entries"]![0]!["efCoreVersions"]!.AsArray();

                versions.Add("10.0.15");
                File.WriteAllText(baselinePath, baseline.ToJsonString());
                break;
            case "missing-reviewed-inventory":
                File.Delete(Path.Combine(contractRoot, "SpecSuiteInventory.10.0.14.json"));
                break;
            case "missing-reviewed-discovery":
                File.Delete(Path.Combine(contractRoot, "SpecDiscovery.10.0.14.json"));
                break;
            case "invalid-reviewed-inventory":
                repository.Write($"{EfContracts}/SpecSuiteInventory.10.0.14.json", "{}");
                break;
            case "invalid-reviewed-discovery":
                repository.Write($"{EfContracts}/SpecDiscovery.10.0.14.json", "{}");
                break;
            case "invalid-baseline":
                repository.Write($"{EfContracts}/SpecSuiteBaseline.json", "{broken");
                break;
            case "empty-baseline":
                repository.Write($"{EfContracts}/SpecSuiteBaseline.json", "{\"efCoreVersions\":[]}");
                break;
        }

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
        Assert.DoesNotContain("repository-tests", repository.Events);
    }

    /// <summary>Preserves actual producer failures instead of masking them as successful upstream discovery.</summary>
    /// <param name="failure">The external producer that fails.</param>
    [Theory]
    [InlineData("restore")]
    [InlineData("graph")]
    [InlineData("repository")]
    [InlineData("specification")]
    [InlineData("reconciliation")]
    [InlineData("integration")]
    public void Ef_upstream_probe_propagates_execution_failures(
        string failure
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository("10.0.14", "10.0.14");
        repository.Environment["DOKA_STUB_FAIL"] = failure;

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.Equal(31, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "efcore-contract-evidence.json")));
    }

    /// <summary>Rejects an incoherent graph and versions outside the declared stable patch line.</summary>
    /// <param name="version">The invalid resolved patch or the version used in a split graph.</param>
    /// <param name="splitGraph">Whether only one of the required packages has that version.</param>
    [Theory]
    [InlineData("10.0.15", true)]
    [InlineData("11.0.0", false)]
    [InlineData("10.1.0", false)]
    [InlineData("10.0.15-rc.1", false)]
    public void Ef_upstream_probe_rejects_invalid_resolved_graphs(
        string version,
        bool splitGraph
    )
    {
        // Arrange
        using var repository = CreateEfMatrixRepository(version, "10.0.14");
        if (splitGraph)
        {
            var graphPath = Path.Combine(repository.Root, "graph.json");
            var graph = File.ReadAllText(graphPath);
            var firstVersion = graph.IndexOf(version, StringComparison.Ordinal);
            graph = graph[..firstVersion] + "10.0.14" + graph[(firstVersion + version.Length)..];
            File.WriteAllText(graphPath, graph);
        }

        // Act
        var result = repository.RunStage("test-efcore-matrix.sh");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(repository.Root, EfProbeEvidence, "upstream-discovery.json")));
        Assert.DoesNotContain("repository-tests", repository.Events);
    }

    /// <summary>Reuses the existing isolated recipe fixture and the real strict version-contract checker.</summary>
    private static StageRepository CreateEfMatrixRepository(
        string resolved,
        string reviewed
    )
    {
        var repository = new StageRepository();
        foreach (var script in new[] { "test-efcore-matrix.sh", "check-spec-version-contract.sh" })
        {
            repository.Write(
                $"eng/testing/{script}",
                File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng/testing", script)));
        }

        repository.Environment["DokaEfCoreVersion"] = "10.0.*";
        repository.Environment["DOKA_EF_CORE_RESOLVED_PATTERN"] = "^10[.]0[.][0-9]+$";
        repository.Environment["DOKA_EF_CORE_ARTIFACT_SUFFIX"] = "probe";
        repository.Environment["DOKA_EF_CORE_VALIDATION_SCOPE"] = "upstream-probe";
        repository.Environment["DOKA_SPEC_CONTRACTS_ROOT"] = Path.Combine(repository.Root, EfContracts);
        repository.Environment["DOKA_EF_CORE_SPEC_TARGETS"] = "mysql84,mariadb118";
        repository.Environment["DOKA_INTEGRATION_TARGETS"] = "mysql84,mariadb118";
        repository.Environment["GITHUB_STEP_SUMMARY"] = Path.Combine(repository.Root, "job-summary.md");
        repository.Write(
            "graph.json",
            JsonSerializer.Serialize(new
            {
                projects = new[]
                {
                    new
                    {
                        frameworks = new[]
                        {
                            new
                            {
                                topLevelPackages = s_efPackages.Select(id => new
                                {
                                    id,
                                    requestedVersion = "10.0.*",
                                    resolvedVersion = resolved,
                                }),
                            },
                        },
                    },
                },
            }));

        repository.Write(
            "bin/dotnet",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            case "$1" in
                restore)
                    if [[ "${DOKA_STUB_FAIL:-}" == restore ]]; then exit 31; fi
                    ;;
                package)
                    if [[ "${DOKA_STUB_FAIL:-}" == graph ]]; then exit 31; fi
                    cat "${DOKA_STUB_ROOT}/graph.json"
                    ;;
                test)
                    printf 'test|%s\n' "${DOKA_SPEC_TEST_TARGET}" >> "${DOKA_STUB_ROOT}/events"
                    if [[ "${DOKA_STUB_FAIL:-}" == specification ]]; then exit 31; fi
                    ;;
                *) exit 42 ;;
            esac
            """,
            executable: true);

        repository.Write(
            "eng/testing/test.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'repository-tests\n' >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == repository ]]; then exit 31; fi
            """);

        repository.Write(
            "eng/testing/test-integration.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'integration\n' >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == integration ]]; then exit 31; fi
            """);

        WriteEfContracts(repository, reviewed);

        return repository;
    }

    /// <summary>Writes small valid contract documents; the delivered checker validates their real schema.</summary>
    private static void WriteEfContracts(
        StageRepository repository,
        params string[] versions
    )
    {
        string[] targets = ["mysql84", "mariadb118"];
        repository.Write(
            $"{EfContracts}/SpecSuiteBaseline.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                efCoreVersions = versions,
                supportedTargets = targets,
                entries = new[]
                {
                    new
                    {
                        upstreamBaseId = "Base",
                        suiteDomain = "relational",
                        efCoreVersions = versions,
                        targets,
                    },
                },
            }));

        foreach (var version in versions)
        {
            repository.Write(
                $"{EfContracts}/SpecSuiteInventory.{version}.json",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    efCoreVersion = version,
                    testMethods = s_efTestMethods,
                    baseClasses = new[] { new { id = "Base", suiteDomain = "relational" } },
                }));

            repository.Write(
                $"{EfContracts}/SpecDiscovery.{version}.json",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    efCoreVersion = version,
                    providerAssembly = "Doka.EntityFrameworkCore.MySql.FunctionalTests",
                    targets = targets.Select(target => new
                    {
                        target,
                        minimumTestCount = 1,
                        testIds = s_efTestMethods,
                        fixtureTypes = s_efFixtureTypes,
                    }),
                }));
        }
    }
}
