namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies engineering scripts preserve their SDK, benchmark selection, and vulnerability-audit contracts.
/// </summary>
public sealed class EngineeringToolContractTests
{
    private static readonly string[] s_auditSources = ["https://api.nuget.org/v3/index.json"];

    /// <summary>Verifies hosted results preserve both benchmark failures and diagnostic-write failures.</summary>
    /// <param name="benchmarkExitCode">The benchmark process result.</param>
    /// <param name="teeExitCode">The diagnostic writer result.</param>
    /// <param name="expectedExitCode">The combined workflow result.</param>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(78, 0, 78)]
    [InlineData(0, 1, 78)]
    public void Benchmark_workflow_rejects_lost_diagnostics_and_preserves_measured_failures(
        int benchmarkExitCode,
        int teeExitCode,
        int expectedExitCode
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github/workflows/benchmark.yml"));
        var start = workflow.IndexOf("          set +e", StringComparison.Ordinal);
        var end = workflow.IndexOf("\n      - name: Upload raw benchmark", start, StringComparison.Ordinal);

        // Act
        var result = Run(repositoryRoot, "-c", default,
            "set -euo pipefail\nbenchmark_status=\"$1\"\ntee_status=\"$2\"\ndiagnostic=unused\n"
            + "bash() { return \"${benchmark_status}\"; }\ntee() { return \"${tee_status}\"; }\n"
            + workflow[start..end], "benchmark-diagnostics",
            benchmarkExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            teeExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(expectedExitCode, result.ExitCode);
    }

    /// <summary>Verifies only local full-mode measurements depend on preflight CPU headroom.</summary>
    /// <param name="evaluation">The resolved enforcement policy.</param>
    /// <param name="expectedExitCode">The expected result on a contended host.</param>
    [Theory]
    [InlineData("full", 78)]
    [InlineData("allocations-only", 0)]
    public void Benchmark_cpu_headroom_admission_is_local_full_mode_only(
        string evaluation,
        int expectedExitCode
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng/performance/benchmark.sh"));
        var start = script.IndexOf("if [[ \"${evaluation}\" == \"full\" ]]", StringComparison.Ordinal);
        var end = script.IndexOf("\necho \"Benchmark evaluation policy:", start, StringComparison.Ordinal);

        // Act
        var result = Run(repositoryRoot, "-c", default,
            "set -euo pipefail\nevaluation=\"$1\"\ncontract=unused\n"
            + "require_benchmark_host_headroom() { echo admission-called; return 1; }\n"
            + script[start..end], "benchmark-admission", evaluation);

        // Assert
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal(evaluation == "full", result.StandardOutput.Contains("admission-called", StringComparison.Ordinal));
    }

    /// <summary>Verifies the shell and executable agree on hosted enforcement, including explicit overrides.</summary>
    /// <param name="requested">The requested evaluation mode.</param>
    /// <param name="actions">The hosted execution marker.</param>
    /// <param name="expected">The resolved policy, or null for a rejected mode.</param>
    [Theory]
    [InlineData("full", "true", "allocations-only")]
    [InlineData("full", "TRUE", "allocations-only")]
    [InlineData("full", "false", "full")]
    [InlineData("allocations-only", "false", "allocations-only")]
    [InlineData("typo", "true", null)]
    public void Benchmark_shell_policy_cannot_enable_hosted_timing_gates(
        string requested,
        string actions,
        string? expected
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng/performance/benchmark.sh"));
        var start = script.IndexOf("case \"${evaluation}\" in", StringComparison.Ordinal);
        var end = script.IndexOf("export DOKA_BENCHMARK_EVALUATION=", start, StringComparison.Ordinal);
        end = script.IndexOf('\n', end);

        // Act
        var result = Run(repositoryRoot, "-c", default,
            "set -euo pipefail\nevaluation=\"$1\"\nGITHUB_ACTIONS=\"$2\"\n"
            + script[start..end] + "\nprintf '%s' \"${DOKA_BENCHMARK_EVALUATION}\"",
            "benchmark-policy", requested, actions);

        // Assert
        Assert.Equal(expected is null ? 78 : 0, result.ExitCode);
        Assert.Equal(expected ?? string.Empty, result.StandardOutput);
    }

    /// <summary>Raw soak budget failures reach the evaluator; failed execution never becomes valid evidence.</summary>
    /// <param name="soakExitCode">The raw soak process result.</param>
    /// <param name="expectedExitCode">The expected orchestration result before evaluation.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 78)]
    [InlineData(78, 78)]
    [InlineData(137, 78)]
    public void Benchmark_shell_defers_only_measured_soak_outcomes_to_the_evaluator(
        int soakExitCode,
        int expectedExitCode
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng/performance/benchmark.sh"));
        var start = script.IndexOf("if [[ \"${soak_required}\" == \"true\" ]]; then", StringComparison.Ordinal);
        var end = script.IndexOf("\nset +e", start, StringComparison.Ordinal);

        // Act
        var result = Run(repositoryRoot, "-c", default,
            "set -euo pipefail\nsoak_required=true\nbenchmark_assembly=unused\nsoak_report=unused\n"
            + "gate_arguments=()\nraw_status=\"$1\"\ndotnet() { return \"${raw_status}\"; }\n"
            + script[start..end] + "\necho evaluator-reached",
            "benchmark-soak-outcome", soakExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal(expectedExitCode == 0, result.StandardOutput.Contains("evaluator-reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies benchmark soak selection preserves an explicit false result without aborting the runner.
    /// </summary>
    /// <param name="profile">The benchmark profile to inspect.</param>
    /// <param name="expected">The expected shell boolean value.</param>
    [Theory]
    [InlineData("smoke", "false")]
    [InlineData("scorecard", "true")]
    [InlineData("stress", "true")]
    public void Benchmark_soak_selection_preserves_false_without_aborting_the_runner(
        string profile,
        string expected
    )
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var assignment = File
            .ReadLines(Path.Combine(repositoryRoot, "eng/performance/benchmark.sh"))
            .Single(static line => line.StartsWith("soak_required=", StringComparison.Ordinal));

        // Act
        var result = Run(
            repositoryRoot,
            "-c",
            default,
            "set -euo pipefail\ncontract=\"$1\"\nprofile=\"$2\"\n" + assignment + "\nprintf '%s\\n' \"$soak_required\"",
            "benchmark-soak-selection",
            Path.Combine(repositoryRoot, "benchmarks/performance-contract.json"),
            profile);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput.Trim());
    }

    /// <summary>
    /// Verifies SDK validation accepts the exact repository version and rejects a different version.
    /// </summary>
    /// <param name="exactVersion">Whether the test executable reports the repository SDK version.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dotnet_guard_accepts_only_the_exact_repository_SDK(
        bool exactVersion
    )
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        var requiredVersion = globalJson
            .RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString();

        var fakeDotnet = Path.Combine(directory.Path, "dotnet");
        var reportedVersion = exactVersion ? requiredVersion : "10.0.999";
        WriteExecutable(fakeDotnet, $"#!/usr/bin/env bash\nprintf '%s\\n' '{reportedVersion}'\n");

        // Act
        var result = Run(
            repositoryRoot,
            Path.Combine(repositoryRoot, "eng/common/verify-dotnet.sh"),
            ("PATH", directory.Path + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")));

        // Assert
        if (exactVersion)
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Contains($"Using .NET SDK {requiredVersion}", result.StandardOutput, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                $"requires the exact .NET SDK {requiredVersion}",
                result.StandardError,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies a complete vulnerability audit without findings passes the gate.
    /// </summary>
    [Fact]
    public void Vulnerability_gate_accepts_a_complete_clean_audit()
    {
        // Arrange
        var document = new
        {
            version = 1,
            parameters = "--vulnerable --include-transitive",
            sources = s_auditSources,
            projects = new[] { new { path = "/repo/src/Provider/Provider.csproj" } },
        };

        // Act
        var result = RunVulnerabilityGate(document);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.StandardOutput.Trim());
    }

    /// <summary>
    /// Verifies transitive package vulnerabilities fail the audit gate.
    /// </summary>
    [Fact]
    public void Vulnerability_gate_rejects_findings()
    {
        // Arrange
        var document = new
        {
            version = 1,
            parameters = "--vulnerable --include-transitive",
            sources = s_auditSources,
            projects = new[]
            {
                new
                {
                    path = "/repo/src/Provider/Provider.csproj",
                    frameworks = new[]
                    {
                        new
                        {
                            framework = "net10.0",
                            transitivePackages = new[]
                            {
                                new
                                {
                                    id = "Vulnerable.Package",
                                    resolvedVersion = "1.0.0",
                                    vulnerabilities = new[]
                                    {
                                        new
                                        {
                                            severity = "High",
                                            advisoryurl = "https://example.invalid/a",
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        // Act
        var vulnerable = RunVulnerabilityGate(document);

        // Assert
        Assert.NotEqual(0, vulnerable.ExitCode);
        Assert.Contains("vulnerable package entries", vulnerable.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an audit that omits transitive dependencies fails the completeness contract.
    /// </summary>
    [Fact]
    public void Vulnerability_gate_rejects_incomplete_audits()
    {
        // Arrange
        var document = new
        {
            version = 1,
            parameters = "--vulnerable",
            projects = new[] { new { path = "/repo/src/Provider/Provider.csproj" } },
        };

        // Act
        var incomplete = RunVulnerabilityGate(document);

        // Assert
        Assert.NotEqual(0, incomplete.ExitCode);
        Assert.Contains("--include-transitive", incomplete.StandardError, StringComparison.Ordinal);
    }

    private static ProcessResult RunVulnerabilityGate<T>(
        T document
    )
    {
        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var audit = Path.Combine(directory.Path, "audit.json");
        File.WriteAllText(audit, JsonSerializer.Serialize(document));

        return Run(
            repositoryRoot,
            Path.Combine(repositoryRoot, "eng/quality/check-vulnerability-audit.sh"),
            default,
            audit,
            "/repo/src/Provider/Provider.csproj");
    }

    private static ProcessResult Run(
        string workingDirectory,
        string script,
        (string Name, string? Value) environment = default,
        params string[] arguments
    )
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("bash")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(environment.Name))
        {
            startInfo.Environment[environment.Name] = environment.Value;
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{script}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static void WriteExecutable(
        string path,
        string content
    )
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"doka-engineering-tool-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
