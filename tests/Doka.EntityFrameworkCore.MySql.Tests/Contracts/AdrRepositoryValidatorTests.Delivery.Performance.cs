namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>Verifies scheduled and manual hosted benchmarks retain the non-timing policy.</summary>
    /// <param name="replacement">The evaluation policy to validate.</param>
    /// <param name="accepted">Whether the policy preserves hosted safety.</param>
    [Theory]
    [InlineData("allocations-only", true)]
    [InlineData("full", false)]
    [InlineData("", false)]
    public void Hosted_benchmark_contract_rejects_hardware_sensitive_enforcement(
        string replacement,
        bool accepted
    )
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "benchmark.yml");
        var workflow = File.ReadAllText(path).Replace(
            "DOKA_BENCHMARK_EVALUATION: allocations-only",
            $"DOKA_BENCHMARK_EVALUATION: {replacement}", StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateHostedBenchmarkWorkflow(workflow);

        // Assert
        Assert.Equal(accepted, errors.Count == 0);
    }

    /// <summary>Verifies benchmark ratios use a target-specific evidence directory.</summary>
    [Fact]
    public void Benchmark_gate_reads_target_scoped_reports()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var benchmarkGateScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "check-benchmark-ratios.sh"));

        // Assert
        Assert.Contains(
            "reports=\"${root}/${target}/reports/${run_id}\"",
            benchmarkGateScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "arguments=(--evaluate \"${contract}\" \"${reports}\" \"${target}\" \"${profile}\")",
            benchmarkGateScript,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies benchmark setup validates and cleans its isolated Compose resources.</summary>
    [Fact]
    public void Benchmark_lifecycle_owns_compose_resources()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var benchmarkScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "benchmark.sh"));

        // Assert
        Assert.Contains(
            "\"${compose[@]}\" ps -q \"${target}\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "compose=(docker compose -p \"${compose_project}\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains("DOKA_BENCHMARK_DATABASE_PORT", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains("down --volumes --remove-orphans", benchmarkScript, StringComparison.Ordinal);
        Assert.Contains(
            "docker inspect --format '{{.Config.Image}}'",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "actual_image=\"$(docker inspect --format '{{.Config.Image}}' \"${container}\")\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "source \"${repo_root}/eng/performance/host-preflight.sh\"",
            benchmarkScript,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies benchmark headroom is checked before measurements start.</summary>
    [Fact]
    public void Benchmark_host_preflight_checks_cpu_headroom()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var hostPreflightScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "host-preflight.sh"));

        // Assert
        Assert.Contains(
            "require_benchmark_host_headroom()",
            hostPreflightScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "maximumCpuUtilization",
            hostPreflightScript,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies one benchmark assembly executes both workload and evidence validation.</summary>
    [Fact]
    public void Benchmark_lifecycle_executes_workload_and_ratio_checks()
    {
        // Arrange
        var scriptPath = Path.Combine(FindRepositoryRoot(), "eng", "performance", "benchmark.sh");

        // Act
        var benchmarkScript = File.ReadAllText(scriptPath);

        // Assert
        Assert.Contains(
            "filters=(--filter '*ProviderWorkloadBenchmarks*')",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet \"${benchmark_assembly}\" \"${filters[@]}\" --artifacts \"${report_directory}\"",
            benchmarkScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet \"${benchmark_assembly}\" \"${gate_arguments[@]}\"",
            benchmarkScript,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies retired benchmark orchestration cannot silently return.</summary>
    [Theory]
    [InlineData("python3")]
    [InlineData("attempt-profile")]
    [InlineData("baseline-mode")]
    [InlineData("paired-benchmark")]
    [InlineData("promote-baseline")]
    [InlineData("workflow_state")]
    public void Benchmark_scripts_reject_retired_orchestration(
        string retiredMechanism
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var benchmarkScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "benchmark.sh"));

        var benchmarkGateScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "check-benchmark-ratios.sh"));

        // Assert
        Assert.DoesNotContain(retiredMechanism, benchmarkScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(retiredMechanism, benchmarkGateScript, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies workload cases come from the shared performance catalog.</summary>
    [Fact]
    public void Benchmark_workloads_use_the_shared_catalog()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workloadBenchmarks = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Doka.EntityFrameworkCore.MySql.Benchmarks",
                "ProviderWorkloadBenchmarks.cs"));

        // Assert
        Assert.Contains(
            "[ParamsSource(nameof(WorkloadIds))]",
            workloadBenchmarks,
            StringComparison.Ordinal);
        Assert.Contains(
            "PerformanceWorkloadCatalog.Create()",
            workloadBenchmarks,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies benchmark evaluation preserves original measurements and stays offline.</summary>
    [Fact]
    public void Performance_gate_uses_original_values_without_network_io()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var performanceGate = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Doka.EntityFrameworkCore.MySql.Benchmarks",
                "PerformanceGate.cs"));

        // Assert
        Assert.Contains("OriginalValues", performanceGate, StringComparison.Ordinal);
        Assert.Contains("InvalidEvidenceExitCode = 78", performanceGate, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", performanceGate, StringComparison.Ordinal);
    }

    /// <summary>Verifies benchmark database targets honor their isolated endpoint.</summary>
    [Fact]
    public void Benchmark_target_uses_the_isolated_database_port()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var benchmarkTarget = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Doka.EntityFrameworkCore.MySql.Benchmarks",
                "BenchmarkDatabaseTarget.cs"));

        // Assert
        Assert.Contains("DOKA_BENCHMARK_DATABASE_PORT", benchmarkTarget, StringComparison.Ordinal);
    }

    /// <summary>
    /// Prevents benchmark diagnostics from populating the run directory before
    /// the benchmark runner applies its freshness guard.
    /// </summary>
    [Fact]
    public void Benchmark_diagnostics_do_not_claim_the_report_directory()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "benchmark.yml"));

        var benchmarkScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "performance", "benchmark.sh"));

        var reportDirectory = NormalizeBenchmarkPathTemplate(
            ExtractQuotedAssignment(
                benchmarkScript.Split('\n').Single(static line =>
                    line.StartsWith("report_directory=", StringComparison.Ordinal))));

        var diagnosticPaths = workflow
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("diagnostic=", StringComparison.Ordinal))
            .Select(ExtractQuotedAssignment)
            .Select(NormalizeBenchmarkPathTemplate)
            .ToArray();

        // Assert
        Assert.Single(diagnosticPaths);
        foreach (var diagnosticPath in diagnosticPaths)
        {
            Assert.False(
                IsPathOwnedBy(reportDirectory, diagnosticPath),
                $"Diagnostic path '{diagnosticPath}' must be outside report directory '{reportDirectory}'.");
        }
    }
}
