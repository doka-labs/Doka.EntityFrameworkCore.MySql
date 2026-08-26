namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class EngineeringToolContractTests
{
    private static readonly string[] s_auditSources = ["https://api.nuget.org/v3/index.json"];

    [Theory]
    [InlineData("smoke", "false")]
    [InlineData("scorecard", "true")]
    [InlineData("stress", "true")]
    public void Benchmark_soak_selection_preserves_false_without_aborting_the_runner(
        string profile,
        string expected
    )
    {
        var repositoryRoot = FindRepositoryRoot();
        var assignment = File
            .ReadLines(Path.Combine(repositoryRoot, "eng/performance/benchmark.sh"))
            .Single(static line => line.StartsWith("soak_required=", StringComparison.Ordinal));

        var result = Run(
            repositoryRoot,
            "-c",
            default,
            "set -euo pipefail\ncontract=\"$1\"\nprofile=\"$2\"\n" + assignment + "\nprintf '%s\\n' \"$soak_required\"",
            "benchmark-soak-selection",
            Path.Combine(repositoryRoot, "benchmarks/performance-contract.json"),
            profile);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput.Trim());
    }

    [Fact]
    public void Dotnet_guard_accepts_only_the_exact_repository_SDK()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        var requiredVersion = globalJson
            .RootElement
            .GetProperty("sdk")
            .GetProperty("version")
            .GetString();

        var fakeDotnet = Path.Combine(directory.Path, "dotnet");
        WriteExecutable(fakeDotnet, $"#!/usr/bin/env bash\nprintf '%s\\n' '{requiredVersion}'\n");
        var accepted = Run(
            repositoryRoot,
            Path.Combine(repositoryRoot, "eng/common/verify-dotnet.sh"),
            ("PATH", directory.Path + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")));

        WriteExecutable(fakeDotnet, "#!/usr/bin/env bash\nprintf '%s\\n' '10.0.999'\n");
        var rejected = Run(
            repositoryRoot,
            Path.Combine(repositoryRoot, "eng/common/verify-dotnet.sh"),
            ("PATH", directory.Path + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")));

        Assert.Equal(0, accepted.ExitCode);
        Assert.Contains($"Using .NET SDK {requiredVersion}", accepted.StandardOutput, StringComparison.Ordinal);
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains(
            $"requires the exact .NET SDK {requiredVersion}",
            rejected.StandardError,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Vulnerability_gate_accepts_a_complete_clean_audit()
    {
        var result = RunVulnerabilityGate(
            new
            {
                version = 1,
                parameters = "--vulnerable --include-transitive",
                sources = s_auditSources,
                projects = new[] { new { path = "/repo/src/Provider/Provider.csproj" } },
            });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.StandardOutput.Trim());
    }

    [Fact]
    public void Vulnerability_gate_rejects_findings_and_incomplete_audits()
    {
        var vulnerable = RunVulnerabilityGate(
            new
            {
                version = 1,
                parameters = "--vulnerable --include-transitive",
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
            });
        var incomplete = RunVulnerabilityGate(
            new
            {
                version = 1,
                parameters = "--vulnerable",
                projects = new[] { new { path = "/repo/src/Provider/Provider.csproj" } },
            });

        Assert.NotEqual(0, vulnerable.ExitCode);
        Assert.Contains("vulnerable package entries", vulnerable.StandardError, StringComparison.Ordinal);
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
