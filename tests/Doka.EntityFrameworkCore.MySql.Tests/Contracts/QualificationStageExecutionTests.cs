namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Executes qualification recipes against isolated deterministic side effects.</summary>
public sealed partial class QualificationStageExecutionTests
{
    /// <summary>Accepts successful dependencies regardless of output order or nested output contents.</summary>
    /// <param name="results">The complete result map supplied by the caller.</param>
    [Theory]
    [InlineData("""{"quality-gates":{"result":"success"},"foundation":{"result":"success"}}""")]
    [InlineData("""{"quality-gates":{"outputs":{},"result":"success"},"foundation":{"result":"success"}}""")]
    [InlineData("""{"quality-gates":{"result":"success","outputs":{}},"foundation":{"result":"success"}}""")]
    [InlineData(
        """
        {"quality-gates":{"result":"success","outputs":{"nested":{"result":"failure"},"empty":{}}},
         "foundation":{"outputs":{},"result":"success"}}
        """)]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},"foundation":{"result":"success",
         "outputs":{"version":"10.0.8-test.1","engine_stages":"[\"runtime\"]"}}}
        """)]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},"foundation":{
         "outputs":{"version":"10.0.8-test.1","engine_stages":"[\"runtime\"]"},"result":"success"}}
        """)]
    public void Required_results_accept_exact_successful_dependencies(
        string results
    )
    {
        // Arrange
        var root = FindRepositoryRoot();
        var environment = new Dictionary<string, string>
        {
            ["RESULTS"] = results,
        };

        // Act
        var result = Run(
            root,
            Path.Combine(root, "eng/quality/require-qualification.sh"),
            environment,
            "quality-gates",
            "foundation");

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("quality-gates: success", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("foundation: success", result.StandardOutput, StringComparison.Ordinal);
    }

    /// <summary>Rejects empty, incomplete, malformed, ambiguous and non-successful dependency results.</summary>
    /// <param name="results">The result map supplied by the caller.</param>
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"}}")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{}}")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"failure\"}}")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"skipped\"}}")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"cancelled\"}}")]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"unknown\"}}")]
    [InlineData(
        "{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"success\"},\"extra\":{\"result\":\"success\"}}")]
    [InlineData(
        "{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"failure\"},\"foundation\":{\"result\":\"success\"}}")]
    [InlineData(
        "{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"failure\",\"result\":\"success\"}}")]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},"foundation":{"result":"success"},
         "foundation":{"result":"success","outputs":{}}}
        """)]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},
         "foundation":{"result":"success","outputs":{},"result":"success"}}
        """)]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},"foundation":{"outputs":{}},"foundation":{"result":"success"}}
        """)]
    [InlineData(
        """
        {"quality-gates":{"result":"success"},"foundation":{"result":"success"},"foundation":{"outputs":{}}}
        """)]
    [InlineData("{\"quality-gates\":{\"result\":\"success\"},\"foundation\":{\"result\":\"success\"}} {}")]
    public void Required_results_fail_closed(
        string results
    )
    {
        // Arrange
        var root = FindRepositoryRoot();
        var environment = new Dictionary<string, string> { ["RESULTS"] = results };

        // Act
        var result = Run(
            root,
            Path.Combine(root, "eng/quality/require-qualification.sh"),
            environment,
            "quality-gates",
            "foundation");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    /// <summary>Rejects an empty declaration and duplicate required job names before qualification.</summary>
    /// <param name="duplicate">Whether to supply the same required job twice.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Required_results_reject_invalid_required_job_inventory(
        bool duplicate
    )
    {
        // Arrange
        var root = FindRepositoryRoot();
        var environment = new Dictionary<string, string> { ["RESULTS"] = "{\"foundation\":{\"result\":\"success\"}}" };
        string[] expected = duplicate
            ? ["foundation", "foundation"]
            : [];

        // Act
        var result = Run(root, Path.Combine(root, "eng/quality/require-qualification.sh"), environment, expected);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    /// <summary>Builds and packs each product once at the explicit version, then retains actual consumer evidence.</summary>
    [Fact]
    public void Package_recipe_owns_exact_version_bytes_frozen_graphs_and_consumer_readback()
    {
        // Arrange
        using var repository = new StageRepository();

        // Act
        var result = repository.RunStage("qualify-packages.sh", "10.0.8-test.1", "artifacts/qualification");

        // Assert
        Assert.True(result.ExitCode == 0, result.StandardError);
        var events = repository.Events;
        Assert.StartsWith("version|", events[0], StringComparison.Ordinal);
        Assert.Equal("locks", events[1]);
        foreach (var id in s_packageIds)
        {
            var project = Path.Combine(repository.Root, "src", id, $"{id}.csproj");
            var pack = Assert.Single(
                repository.DotnetCalls,
                arguments => arguments[0] == "pack"
                    && arguments[1] == project);

            var build = Assert.Single(
                repository.DotnetCalls,
                arguments => arguments[0] == "build"
                    && arguments[1] == project);

            Assert.Contains("--no-build", pack);
            Assert.Contains("--no-restore", pack);
            Assert.Contains("--no-restore", build);
            Assert.Contains("-p:PackageVersion=10.0.8-test.1", pack);
            Assert.Contains("-p:PackageVersion=10.0.8-test.1", build);
            Assert.Equal(Path.Combine(repository.Output, "packages"), ArgumentAfter(pack, "--output"));
            Assert.True(File.Exists(Path.Combine(repository.Output, $"packages/{id}.10.0.8-test.1.nupkg")));
        }

        string[] expectedConsumer =
        [
            "10.0.8-test.1", Path.Combine(repository.Output, "packages"),
            Path.Combine(repository.Output, "local-package-consumer"),
        ];

        string[] expectedReadback =
        [
            "10.0.8-test.1", "v10.0.8-test.1", StubSourceCommit, StubDatabaseImage,
            Path.Combine(repository.Output, "local-package-consumer"), Path.Combine(repository.Output, "packages"),
        ];

        Assert.Equal(expectedConsumer, repository.ConsumerArguments);
        Assert.Equal(expectedReadback, repository.ReadbackArguments);
        Assert.True(File.Exists(Path.Combine(repository.Output, "local-package-consumer/local-package-consumer.json")));
        Assert.True(File.Exists(Path.Combine(repository.Output, "local-package-consumer/local-package-runtime.json")));
        Assert.True(File.Exists(Path.Combine(repository.Output, "resolved-packages.json")));
        foreach (var role in new[] { "runtime", "spatial", "cache" })
        {
            Assert.Contains(
                "qualified",
                File.ReadAllText(
                    Path.Combine(
                        repository.Output,
                        $"sbom-components/{role}/project.assets.json")),
                StringComparison.Ordinal);
        }
    }

    /// <summary>Propagates package failures and prevents downstream commands from masking them.</summary>
    /// <param name="failure">The external operation that fails.</param>
    [Theory]
    [InlineData("build:Doka.EntityFrameworkCore.MySql")]
    [InlineData("pack:Doka.EntityFrameworkCore.MySql.NetTopologySuite")]
    [InlineData("consumer")]
    [InlineData("readback")]
    [InlineData("graph")]
    public void Package_recipe_propagates_failed_operations(
        string failure
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Environment["DOKA_STUB_FAIL"] = failure;

        // Act
        var result = repository.RunStage("qualify-packages.sh", "10.0.8-test.1", "artifacts/qualification");

        // Assert
        Assert.Equal(31, result.ExitCode);
        if (failure != "graph")
        {
            Assert.DoesNotContain(
                repository.Events,
                static entry => entry.StartsWith("dotnet|package list", StringComparison.Ordinal));
        }
    }

    /// <summary>Rejects wrong, missing, ambiguous or extra packages and missing consumer evidence.</summary>
    /// <param name="mutation">The successful but incomplete output to simulate.</param>
    [Theory]
    [InlineData("version")]
    [InlineData("missing-package")]
    [InlineData("ambiguous-package")]
    [InlineData("extra-package")]
    [InlineData("legacy-symbol-package")]
    [InlineData("consumer")]
    [InlineData("readback")]
    public void Package_recipe_rejects_wrong_bytes_or_missing_evidence(
        string mutation
    )
    {
        // Arrange
        using var repository = new StageRepository();
        if (mutation == "version")
        {
            repository.Environment["DOKA_STUB_PACK_VERSION"] = "10.0.9";
        }
        else if (mutation is "ambiguous-package" or "extra-package" or "legacy-symbol-package")
        {
            repository.Environment["DOKA_STUB_EXTRA_PACKAGE"] = mutation switch
            {
                "ambiguous-package" => "Doka.EntityFrameworkCore.MySql.10.0.9.nupkg",
                "legacy-symbol-package" => "Doka.EntityFrameworkCore.MySql.10.0.8-test.1.symbols.nupkg",
                _ => "Unexpected.Package.10.0.8-test.1.nupkg",
            };
        }
        else
        {
            repository.Environment["DOKA_STUB_MISSING"] = mutation == "missing-package"
                ? "package"
                : mutation;
        }

        // Act
        var result = repository.RunStage("qualify-packages.sh", "10.0.8-test.1", "artifacts/qualification");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(
            repository.Events,
            static entry => entry.StartsWith("dotnet|package list", StringComparison.Ordinal));

        if (mutation is not ("consumer" or "readback"))
        {
            Assert.DoesNotContain(
                repository.Events,
                static entry => entry.StartsWith("consumer|", StringComparison.Ordinal)
                    || entry.StartsWith("readback|", StringComparison.Ordinal));
        }
    }

    /// <summary>Refuses reuse of another invocation's package output before building or packing again.</summary>
    [Fact]
    public void Package_recipe_rejects_existing_outputs()
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Write("artifacts/qualification/packages/existing.nupkg", "already-qualified");

        // Act
        var result = repository.RunStage("qualify-packages.sh", "10.0.8-test.1", "artifacts/qualification");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(repository.Events, static entry => entry.StartsWith("dotnet|", StringComparison.Ordinal));
        Assert.Equal("already-qualified", File.ReadAllText(Path.Combine(repository.Output, "packages/existing.nupkg")));
    }

    /// <summary>Rejects roots outside the private evidence boundary before running a build or consumer.</summary>
    /// <param name="output">The unsafe output root.</param>
    [Theory]
    [InlineData(".")]
    [InlineData("../outside")]
    [InlineData(".git/qualification")]
    [InlineData("artifacts/invalid\nroot")]
    public void Package_recipe_rejects_unsafe_output_roots(
        string output
    )
    {
        // Arrange
        using var repository = new StageRepository();

        // Act
        var result = repository.RunStage("qualify-packages.sh", "10.0.8-test.1", output);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(
            repository.Events,
            static entry => entry == "locks"
                || entry.StartsWith("dotnet|", StringComparison.Ordinal)
                || entry.StartsWith("consumer|", StringComparison.Ordinal));
    }

    /// <summary>Generates SBOM evidence from only the three frozen production graphs without a production restore.</summary>
    [Fact]
    public void Sbom_recipe_consumes_frozen_package_inputs()
    {
        // Arrange
        using var repository = new StageRepository();
        repository.PrepareFrozenInputs();
        repository.Write("artifacts/obj/unrelated/project.assets.json", "stale-unrelated");

        // Act
        var result = repository.RunStage("generate-package-sbom.sh", "10.0.8-test.1", "artifacts/qualification");

        // Assert
        Assert.True(result.ExitCode == 0, result.StandardError);
        var materializations = repository
            .Events
            .Where(static entry => entry.StartsWith("materialize|", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, materializations.Length);
        Assert.All(
            materializations,
            static entry => Assert.Contains("/sbom-components/", entry, StringComparison.Ordinal));

        Assert.DoesNotContain(
            repository.Events,
            static entry => entry.StartsWith("dotnet|restore ", StringComparison.Ordinal)
                || entry.StartsWith("dotnet|pack ", StringComparison.Ordinal));

        var generation = Assert.Single(
            repository.Events,
            static entry => entry.StartsWith("dotnet|tool run sbom-tool", StringComparison.Ordinal));

        Assert.Contains("--allow-roll-forward -- Generate", generation, StringComparison.Ordinal);
        Assert.Contains("/sbom-components -m ", generation, StringComparison.Ordinal);
        Assert.Contains("-pv 10.0.8-test.1", generation, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated", generation, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repository.Output, "sbom/manifest.spdx.json")));
    }

    /// <summary>Rejects missing or extra frozen graphs, failed generation and successful commands without evidence.</summary>
    /// <param name="mutation">The failure or invalid input to introduce.</param>
    [Theory]
    [InlineData("missing-graph")]
    [InlineData("extra-graph")]
    [InlineData("symlink-graph")]
    [InlineData("wrong-version")]
    [InlineData("materialize")]
    [InlineData("tool-restore")]
    [InlineData("sbom")]
    [InlineData("missing-evidence")]
    public void Sbom_recipe_fails_closed_on_invalid_inputs_or_generation(
        string mutation
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.PrepareFrozenInputs();
        if (mutation == "missing-graph")
        {
            File.Delete(Path.Combine(repository.Output, "sbom-components/cache/project.assets.json"));
        }
        else if (mutation == "extra-graph")
        {
            repository.Write("artifacts/qualification/sbom-components/extra/project.assets.json", "{}");
        }
        else if (mutation == "symlink-graph")
        {
            var graph = Path.Combine(repository.Output, "sbom-components/cache/project.assets.json");
            File.Delete(graph);
            File.CreateSymbolicLink(
                graph,
                Path.Combine(repository.Root, "artifacts/obj/Doka.Caching.MySql/project.assets.json"));
        }
        else if (mutation == "missing-evidence")
        {
            repository.Environment["DOKA_STUB_MISSING"] = "sbom";
        }
        else
        {
            repository.Environment["DOKA_STUB_FAIL"] = mutation;
        }

        // Act
        var result = repository.RunStage(
            "generate-package-sbom.sh",
            mutation == "wrong-version"
                ? "10.0.9"
                : "10.0.8-test.1",
            "artifacts/qualification");

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(repository.Output, "sbom/manifest.spdx.json")));
    }

    /// <summary>Derives engine and complete qualification inventories from the checked-out branch policy.</summary>
    /// <param name="hasEfMatrix">Whether the branch policy requires the stable EF matrix.</param>
    /// <param name="query">The engine-only or complete stage inventory request.</param>
    [Theory]
    [InlineData(false, "--list")]
    [InlineData(true, "--list")]
    [InlineData(false, "--all-stages")]
    [InlineData(true, "--all-stages")]
    public void Engine_inventory_follows_branch_owned_policy(
        bool hasEfMatrix,
        string query
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.WriteEnginePolicy(hasEfMatrix);

        // Act
        var result = repository.RunStage("qualify-engine.sh", query);

        // Assert
        Assert.Equal(0, result.ExitCode);
        var stages = JsonSerializer.Deserialize<string[]>(result.StandardOutput)!;
        string[] expected = hasEfMatrix
            ? ["migration-deployment", "runtime", "efcore-patch-matrix", "mysqlconnector-patch-matrix"]
            : ["migration-deployment", "runtime", "mysqlconnector-patch-matrix"];

        if (query == "--all-stages")
        {
            expected = [.. expected, "package", "sbom"];
        }

        Assert.Equal(expected, stages);
        Assert.Empty(repository.Events);
    }

    /// <summary>Rejects missing producers and ambiguous or unknown policy gates without executing a producer.</summary>
    /// <param name="mutation">The invalid policy or implementation to introduce.</param>
    /// <param name="query">The engine-only or complete stage inventory request.</param>
    [Theory]
    [InlineData("missing-ef-script", "--list")]
    [InlineData("missing-ef-script", "--all-stages")]
    [InlineData("symlink-ef-script", "--list")]
    [InlineData("symlink-ef-script", "--all-stages")]
    [InlineData("duplicate-policy", "--list")]
    [InlineData("duplicate-policy", "--all-stages")]
    [InlineData("unknown-policy", "--list")]
    [InlineData("unknown-policy", "--all-stages")]
    public void Engine_inventory_rejects_invalid_branch_policy(
        string mutation,
        string query
    )
    {
        // Arrange
        using var repository = new StageRepository();
        var efScript = Path.Combine(repository.Root, "eng/testing/test-efcore-matrix.sh");
        if (mutation is "missing-ef-script" or "symlink-ef-script")
        {
            File.Delete(efScript);
            if (mutation == "symlink-ef-script")
            {
                File.CreateSymbolicLink(efScript, Path.Combine(repository.Root, "eng/testing/test-runtime-posture.sh"));
            }
        }
        else
        {
            var policyPath = Path.Combine(repository.Root, "eng/release/evidence-policy.json");
            using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));
            var gates = policy
                .RootElement
                .GetProperty("gates")
                .EnumerateArray()
                .Select(static gate => gate.Clone())
                .ToList();

            gates.Add(
                mutation == "duplicate-policy"
                    ? gates.Single(static gate => gate
                            .GetProperty("id")
                            .GetString()
                        == "efcore-patch-matrix")
                    : JsonSerializer.SerializeToElement(
                        new
                        {
                            id = "unknown-gate",
                            kind = "candidate-produced",
                            implementation = "eng/testing/unknown.sh",
                        }));

            repository.Write("eng/release/evidence-policy.json", JsonSerializer.Serialize(new { gates }));
        }

        // Act
        var result = repository.RunStage("qualify-engine.sh", query);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(repository.Events);
    }

    /// <summary>Requires policy implementation metadata to name the bounded producer that the stage dispatches.</summary>
    /// <param name="mutation">The invalid implementation field to introduce.</param>
    /// <param name="query">The engine-only or complete stage inventory request.</param>
    [Theory]
    [InlineData("missing", "--list")]
    [InlineData("missing", "--all-stages")]
    [InlineData("null", "--list")]
    [InlineData("null", "--all-stages")]
    [InlineData("non-string", "--list")]
    [InlineData("non-string", "--all-stages")]
    [InlineData("nul-suffix", "--list")]
    [InlineData("nul-suffix", "--all-stages")]
    [InlineData("wrong-existing-producer", "--list")]
    [InlineData("wrong-existing-producer", "--all-stages")]
    [InlineData("missing-path", "--list")]
    [InlineData("missing-path", "--all-stages")]
    [InlineData("traversal", "--list")]
    [InlineData("traversal", "--all-stages")]
    [InlineData("absolute", "--list")]
    [InlineData("absolute", "--all-stages")]
    [InlineData("symlink", "--list")]
    [InlineData("symlink", "--all-stages")]
    public void Engine_inventory_rejects_mismatched_declared_implementations(
        string mutation,
        string query
    )
    {
        // Arrange
        using var repository = new StageRepository();
        using var policy = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    repository.Root,
                    "eng/release/evidence-policy.json")));

        var gates = policy
            .RootElement
            .GetProperty("gates")
            .EnumerateArray()
            .Select(static gate => gate
                .EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => property.Value.Clone()))
            .ToList();

        var runtime = gates.Single(static gate => gate["id"]
                .GetString()
            == "runtime-posture");

        if (mutation == "missing")
        {
            runtime.Remove("implementation");
        }
        else
        {
            object? implementation = mutation switch
            {
                "null" => null,
                "non-string" => 42,
                "nul-suffix" => "eng/testing/test-runtime-posture.sh\0",
                "wrong-existing-producer" => "eng/testing/test-migration-deployment.sh",
                "missing-path" => "eng/testing/missing-producer.sh",
                "traversal" => "eng/testing/../testing/test-runtime-posture.sh",
                "absolute" => Path.Combine(repository.Root, "eng/testing/test-runtime-posture.sh"),
                "symlink" => "eng/testing/runtime-alias.sh",
                _ => throw new InvalidOperationException($"Unknown implementation mutation '{mutation}'."),
            };

            runtime["implementation"] = JsonSerializer.SerializeToElement(implementation);
            if (mutation == "symlink")
            {
                File.CreateSymbolicLink(
                    Path.Combine(repository.Root, "eng/testing/runtime-alias.sh"),
                    Path.Combine(repository.Root, "eng/testing/test-runtime-posture.sh"));
            }
        }

        repository.Write("eng/release/evidence-policy.json", JsonSerializer.Serialize(new { gates }));

        // Act
        var result = repository.RunStage("qualify-engine.sh", query);

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(repository.Events);
        Assert.Contains("implementation", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the full six-engine configuration matrix and live examples for either caller.</summary>
    /// <param name="failure">The failing child, or empty for successful qualification.</param>
    [Theory]
    [InlineData("")]
    [InlineData("integration")]
    [InlineData("examples")]
    public void Integration_recipe_preserves_full_configuration_and_example_contracts(
        string failure
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Environment["DOKA_STUB_FAIL"] = failure;
        repository.Write(
            "eng/testing/test-integration.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'integration|%s|%s|%s|%s\n' "${DOKA_INTEGRATION_TARGETS}" \
                "${DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX}" "${DOKA_INTEGRATION_RUN_ID}" \
                "${DOKA_COVERAGE_RESULTS_DIR}" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == integration ]]; then exit 31; fi
            """);

        repository.Write(
            "eng/testing/test-examples.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'examples|%s|%s|%s\n' "${DOKA_EXAMPLE_TARGETS}" "${DOKA_EXAMPLE_RUN_ID}" \
                "${DOKA_EXAMPLE_EVIDENCE_DIR}" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == examples ]]; then exit 31; fi
            """);

        // Act
        var result = repository.RunStage(
            "qualify-engine.sh",
            "integration",
            "artifacts/release-candidate/process-1",
            "process-1");

        // Assert
        Assert.Equal(
            failure.Length == 0
                ? 0
                : 31,
            result.ExitCode);

        var integration = Assert.Single(
            repository.Events,
            static entry => entry.StartsWith("integration|", StringComparison.Ordinal));

        Assert.Contains(
            "|mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123|1|process-1|",
            integration,
            StringComparison.Ordinal);

        Assert.EndsWith("/coverage-input/integration", integration, StringComparison.Ordinal);
        if (failure == "integration")
        {
            Assert.DoesNotContain(
                repository.Events,
                static entry => entry.StartsWith("examples|", StringComparison.Ordinal));
        }
        else
        {
            var examples = Assert.Single(
                repository.Events,
                static entry => entry.StartsWith("examples|", StringComparison.Ordinal));

            Assert.Contains(
                "|mysql84,mysql97,mariadb1011,mariadb114,mariadb118,mariadb123|process-1|",
                examples,
                StringComparison.Ordinal);

            Assert.EndsWith("/integration/examples", examples, StringComparison.Ordinal);
        }
    }

    /// <summary>Forwards source identity and isolated evidence paths while preserving engine-process failures.</summary>
    /// <param name="stage">The native engine stage.</param>
    /// <param name="fails">Whether the underlying producer fails.</param>
    [Theory]
    [InlineData("runtime", false)]
    [InlineData("runtime", true)]
    [InlineData("migration-deployment", false)]
    [InlineData("migration-deployment", true)]
    public void Engine_recipe_preserves_runtime_and_migration_evidence_contracts(
        string stage,
        bool fails
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Environment["DOKA_STUB_FAIL"] = fails
            ? stage
            : string.Empty;

        repository.Write(
            "eng/testing/test-runtime-posture.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'runtime|%s|%s|%s|%s|%s\n' "${DOKA_RUNTIME_POSTURE_RUN_ID}" \
                "${DOKA_RUNTIME_REQUIRE_CLEAN_SOURCE}" "${DOKA_RUNTIME_MYSQL_PORT}" \
                "${DOKA_RUNTIME_POSTURE_EVIDENCE_DIR}" "$*" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == runtime ]]; then exit 31; fi
            """);

        repository.Write(
            "eng/testing/test-migration-deployment.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'migration|%s|%s\n' "${DOKA_MIGRATION_DEPLOYMENT_RUN_ID}" \
                "${DOKA_MIGRATION_DEPLOYMENT_EVIDENCE_ROOT}" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == migration-deployment ]]; then exit 31; fi
            """);

        // Act
        var result = repository.RunStage(
            "qualify-engine.sh",
            stage,
            "artifacts/release-candidate/process-1",
            "process-1");

        // Assert
        Assert.Equal(
            fails
                ? 31
                : 0,
            result.ExitCode);

        var events = repository.Events;
        Assert.Equal("verify-dotnet", events[0]);
        var evidence = Assert.Single(events[1..]);
        if (stage == "runtime")
        {
            Assert.StartsWith("runtime|process-1|1|0|", evidence, StringComparison.Ordinal);
            Assert.EndsWith("/runtime|--up-test-down", evidence, StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith("migration|process-1|", evidence, StringComparison.Ordinal);
            Assert.EndsWith("/migration-deployment", evidence, StringComparison.Ordinal);
        }
    }

    /// <summary>Uses reviewed EF patches and native connector legs, then copies each leg into the qualification root.</summary>
    /// <param name="stage">The dependency matrix stage.</param>
    /// <param name="fails">Whether the underlying matrix producer fails.</param>
    [Theory]
    [InlineData("efcore-patch-matrix", false)]
    [InlineData("efcore-patch-matrix", true)]
    [InlineData("mysqlconnector-patch-matrix", false)]
    [InlineData("mysqlconnector-patch-matrix", true)]
    public void Engine_recipe_preserves_reviewed_dependency_matrix_legs(
        string stage,
        bool fails
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Environment["DOKA_STUB_FAIL"] = fails
            ? stage
            : string.Empty;

        repository.Write(
            "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/SpecSuiteBaseline.json",
            "{\"efCoreVersions\":[\"10.0.8\",\"10.0.9\"]}");

        repository.Write(
            "eng/testing/test-efcore-matrix.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'ef|%s|%s|%s|%s\n' "${DokaEfCoreVersion}" "${DOKA_EF_CORE_RESOLVED_PATTERN}" \
                "${DOKA_EF_CORE_ARTIFACT_SUFFIX}" "${DOKA_EF_CORE_VALIDATION_SCOPE}" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == efcore-patch-matrix ]]; then exit 31; fi
            directory="${DOKA_STUB_ROOT}/artifacts/efcore-patch-matrix/${DOKA_EF_CORE_ARTIFACT_SUFFIX}"
            mkdir -p "${directory}"
            printf 'qualified-ef' > "${directory}/evidence.json"
            """);

        repository.Write(
            "eng/testing/test-mysqlconnector-matrix.sh",
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf 'connector|%s|%s|%s\n' "${DOKA_MYSQLCONNECTOR_VERSION}" \
                "${DOKA_MYSQLCONNECTOR_RESOLVED_PATTERN}" "${DOKA_MYSQLCONNECTOR_ARTIFACT_SUFFIX}" >> "${DOKA_STUB_ROOT}/events"
            if [[ "${DOKA_STUB_FAIL:-}" == mysqlconnector-patch-matrix ]]; then exit 31; fi
            directory="${DOKA_STUB_ROOT}/artifacts/mysqlconnector-patch-matrix/${DOKA_MYSQLCONNECTOR_ARTIFACT_SUFFIX}"
            mkdir -p "${directory}"
            printf 'qualified-connector' > "${directory}/evidence.json"
            """);

        // Act
        var result = repository.RunStage(
            "qualify-engine.sh",
            stage,
            "artifacts/release-candidate/process-1",
            "process-1");

        // Assert
        Assert.Equal(
            fails
                ? 31
                : 0,
            result.ExitCode);

        var events = repository.Events;
        Assert.Equal("verify-dotnet", events[0]);
        string[] expectedEvents = stage == "efcore-patch-matrix"
            ? ["ef|10.0.8|^10[.]0[.]8$|minimum-10-0-8|dependency-graph", "ef|10.0.9|^10[.]0[.]9$|latest-10-0|full"]
            : ["connector|2.6.1|^2[.]6[.]1$|minimum-2-6-1", "connector|2.*|^2[.][0-9]+[.][0-9]+$|latest-2-x"];

        Assert.Equal(
            fails
                ? expectedEvents[..1]
                : expectedEvents,
            events[1..]);

        if (!fails)
        {
            string[] legs = stage == "efcore-patch-matrix"
                ? ["minimum-10-0-8", "latest-10-0"]
                : ["minimum-2-6-1", "latest-2-x"];

            Assert.All(
                legs,
                leg => Assert.True(
                    File.Exists(
                        Path.Combine(
                            repository.Root,
                            "artifacts/release-candidate/process-1",
                            stage,
                            leg,
                            "evidence.json"))));
        }
    }

    /// <summary>Rejects invalid stages and paths before executing any branch-native producer.</summary>
    /// <param name="stage">The requested stage.</param>
    /// <param name="output">The requested evidence root.</param>
    /// <param name="runId">The requested execution identity.</param>
    [Theory]
    [InlineData("unknown", "artifacts/release-candidate/process-1", "process-1")]
    [InlineData("runtime", "artifacts/elsewhere", "process-1")]
    [InlineData("runtime", "artifacts/release-candidate/../outside", "process-1")]
    [InlineData("runtime", "artifacts/release-candidate/process-1", "../outside")]
    [InlineData("efcore-patch-matrix", "artifacts/release-candidate/process-1", "process-1")]
    public void Engine_recipe_rejects_invalid_or_unavailable_stage_requests(
        string stage,
        string output,
        string runId
    )
    {
        // Arrange
        using var repository = new StageRepository();
        if (stage == "efcore-patch-matrix")
        {
            File.Delete(Path.Combine(repository.Root, "eng/testing/test-efcore-matrix.sh"));
        }

        // Act
        var result = repository.RunStage("qualify-engine.sh", stage, output, runId);

        // Assert
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(repository.Events);
    }

    /// <summary>Selects the checkout's declared test protocol and reconciles only successful specification runs.</summary>
    /// <param name="runner">The branch's declared test runner.</param>
    /// <param name="target">The specification target, including the first-target admission checks.</param>
    /// <param name="fails">Whether specification execution fails.</param>
    [Theory]
    [InlineData("VSTest", "mariadb118", false)]
    [InlineData("VSTest", "mariadb118", true)]
    [InlineData("VSTest", "mysql84", false)]
    [InlineData("VSTest", "mysql84", true)]
    [InlineData("Microsoft.Testing.Platform", "mariadb118", false)]
    [InlineData("Microsoft.Testing.Platform", "mariadb118", true)]
    [InlineData("Microsoft.Testing.Platform", "mysql84", false)]
    [InlineData("Microsoft.Testing.Platform", "mysql84", true)]
    public void Specification_recipe_preserves_branch_native_runner_and_failure_semantics(
        string runner,
        string target,
        bool fails
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Write("global.json", JsonSerializer.Serialize(new { test = new { runner } }));
        repository.Environment["DOTNET_TEST_RUNNER"] = runner == "VSTest"
            ? "Microsoft.Testing.Platform"
            : "VSTest";

        repository.Environment["DOKA_STUB_FAIL"] = fails
            ? "specification"
            : string.Empty;

        var results = Path.Combine(repository.Root, "artifacts/spec-tests", target);
        var project = Path.Combine(
            repository.Root,
            "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj");

        // Act
        var result = repository.RunStage("test-spec-target.sh", target, $"artifacts/spec-tests/{target}");

        // Assert
        Assert.Equal(
            fails
                ? 31
                : 0,
            result.ExitCode);

        Assert.Equal("verify-dotnet", repository.Events[0]);
        var calls = repository
            .DotnetCalls
            .Where(static arguments => arguments[0] == "test")
            .ToArray();

        Assert.Equal(
            target == "mysql84"
                ? 2
                : 1,
            calls.Length);

        Assert.All(calls, arguments => AssertNativeTestArguments(arguments, runner, project));
        var evidence = repository
            .Events
            .Where(static entry => entry.StartsWith("spec-env|", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(calls.Length, evidence.Length);
        Assert.All(evidence, entry => Assert.Equal($"spec-env|{target}|{results}/test-database-evidence.json", entry));
        var runners = repository
            .Events
            .Where(static entry => entry.StartsWith("test-runner|", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(calls.Length, runners.Length);
        Assert.All(runners, entry => Assert.Equal($"test-runner|{runner}", entry));
        if (target == "mysql84")
        {
            Assert.Equal("spec-contract", repository.Events[1]);
            Assert.Equal("spec-discovery", repository.Events[2]);
            Assert.Equal("FullyQualifiedName~SpecDispositionContractTests", ArgumentAfter(calls[0], "--filter"));
        }
        else
        {
            Assert.DoesNotContain("spec-contract", repository.Events);
            Assert.DoesNotContain("spec-discovery", repository.Events);
        }

        var command = calls[calls.Length - 1];
        Assert.Equal(results, ArgumentAfter(command, "--results-directory"));
        if (runner == "VSTest")
        {
            Assert.Equal("Category=Spec|Category=Live", ArgumentAfter(command, "--filter"));
            Assert.Contains("--collect:XPlat Code Coverage", command);
            Assert.Equal("trx;LogFileName=spec-tests.trx", ArgumentAfter(command, "--logger"));
        }
        else
        {
            Assert.Equal(
                "FullyQualifiedName~Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification."
                + "|Category=Spec|Category=Live",
                ArgumentAfter(command, "--filter"));

            Assert.Contains("--coverlet", command);
            Assert.Equal("cobertura", ArgumentAfter(command, "--coverlet-output-format"));
            Assert.Equal($"spec-{target}", ArgumentAfter(command, "--coverlet-file-prefix"));
            Assert.Contains("--report-xunit-trx", command);
            Assert.Equal("spec-tests.trx", ArgumentAfter(command, "--report-xunit-trx-filename"));
        }

        Assert.Equal(!fails, repository.Events.Contains($"reconcile|{target}|{results}", StringComparer.Ordinal));
    }

    /// <summary>Stops first-target qualification when admission, disposition, or reconciliation fails.</summary>
    /// <param name="runner">The branch's declared test runner.</param>
    /// <param name="failure">The first-target phase whose failure must propagate.</param>
    [Theory]
    [InlineData("VSTest", "contract")]
    [InlineData("VSTest", "discovery")]
    [InlineData("VSTest", "disposition")]
    [InlineData("VSTest", "reconciliation")]
    [InlineData("Microsoft.Testing.Platform", "contract")]
    [InlineData("Microsoft.Testing.Platform", "discovery")]
    [InlineData("Microsoft.Testing.Platform", "disposition")]
    [InlineData("Microsoft.Testing.Platform", "reconciliation")]
    public void Specification_recipe_preserves_first_target_gate_failures(
        string runner,
        string failure
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Write("global.json", JsonSerializer.Serialize(new { test = new { runner } }));
        repository.Environment["DOKA_STUB_FAIL"] = failure;
        var results = Path.Combine(repository.Root, "artifacts/spec-tests/mysql84");
        var expectedTestCount = failure switch
        {
            "contract" or "discovery" => 0,
            "disposition" => 1,
            "reconciliation" => 2,
            _ => throw new InvalidOperationException($"Unknown failure '{failure}'."),
        };

        // Act
        var result = repository.RunStage("test-spec-target.sh", "mysql84", "artifacts/spec-tests/mysql84");

        // Assert
        Assert.Equal(31, result.ExitCode);
        Assert.Equal("verify-dotnet", repository.Events[0]);
        Assert.Equal("spec-contract", repository.Events[1]);
        Assert.Equal(failure != "contract", repository.Events.Contains("spec-discovery", StringComparer.Ordinal));
        var calls = repository
            .DotnetCalls
            .Where(static arguments => arguments[0] == "test")
            .ToArray();

        Assert.Equal(expectedTestCount, calls.Length);
        if (calls.Length > 0)
        {
            Assert.Equal("FullyQualifiedName~SpecDispositionContractTests", ArgumentAfter(calls[0], "--filter"));
        }

        var reconciliation = $"reconcile|mysql84|{results}";
        Assert.Equal(failure == "reconciliation", repository.Events.Contains(reconciliation, StringComparer.Ordinal));
        if (failure == "reconciliation")
        {
            Assert.Equal(reconciliation, repository.Events.Last());
        }
    }

    /// <summary>Rejects unsupported runners before SDK verification, guards, tests or output creation.</summary>
    /// <param name="target">The first or another specification partition.</param>
    [Theory]
    [InlineData("mysql84")]
    [InlineData("mariadb118")]
    public void Specification_recipe_rejects_unknown_branch_runner(
        string target
    )
    {
        // Arrange
        using var repository = new StageRepository();
        repository.Write("global.json", "{\"test\":{\"runner\":\"unknown\"}}");

        // Act
        var result = repository.RunStage("test-spec-target.sh", target, $"artifacts/spec-tests/{target}");

        // Assert
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(repository.Events);
        Assert.Empty(repository.DotnetCalls);
        Assert.False(Directory.Exists(Path.Combine(repository.Root, "artifacts/spec-tests", target)));
        Assert.Contains("Unsupported branch test runner", result.StandardError, StringComparison.Ordinal);
    }
}
