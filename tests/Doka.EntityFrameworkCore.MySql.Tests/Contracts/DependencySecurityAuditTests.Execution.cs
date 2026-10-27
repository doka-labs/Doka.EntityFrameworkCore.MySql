namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class DependencySecurityAuditTests
{
    /// <summary>Checks every source project and preserves an independent explicit audit source.</summary>
    [Fact]
    public void Runner_covers_each_source_group_with_fresh_direct_and_transitive_audits()
    {
        // Arrange
        using var fixture = new AuditFixture();
        fixture.AddProject("examples/Second/Second.csproj");
        fixture.AddProject("src/Alpha/obj/generated/Excluded.csproj");
        fixture.AddTemplate("examples/PublicRcConsumer/Consumer.csproj.template");

        // Act
        var result = fixture.Execute();

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(6, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.Equal("passed", fixture.Summary.GetProperty("result").GetString());
        var commands = fixture.Commands;
        Assert.Equal(6, commands.Count(command => Argument(command, 0) == "restore"));
        Assert.Equal(6, commands.Count(command => Argument(command, 0) == "package"));
        Assert.All(commands.Where(command => Argument(command, 0) == "restore"), command =>
        {
            var arguments = Arguments(command);
            Assert.Contains("--force", arguments);
            Assert.Contains("--no-http-cache", arguments);
            Assert.Contains("--configfile", arguments);
            Assert.Contains("-p:NuGetAudit=true", arguments);
            Assert.Contains("-p:NuGetAuditMode=all", arguments);
            Assert.Contains("-p:NuGetAuditLevel=low", arguments);
            Assert.Contains("-p:TreatWarningsAsErrors=true", arguments);
            Assert.Contains("-p:NoWarn=", arguments);
            Assert.Contains("-p:WarningsNotAsErrors=", arguments);
            Assert.DoesNotContain("--force-evaluate", arguments);
        });

        Assert.All(commands.Where(command => Argument(command, 0) == "package"), command =>
        {
            var arguments = Arguments(command);
            Assert.Contains("--no-restore", arguments);
            Assert.Contains("--config", arguments);
            Assert.Contains("--vulnerable", arguments);
            Assert.Contains("--include-transitive", arguments);
            Assert.Contains("--output-version", arguments);
            Assert.DoesNotContain("tool", arguments);
        });

        Assert.DoesNotContain("Excluded.csproj", File.ReadAllText(Path.Combine(fixture.RunPath, "inventory.txt")),
            StringComparison.Ordinal);
        Assert.Contains("<auditSources>", fixture.Configuration, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Configuration.Split("<clear", StringSplitOptions.None).Length - 1);
        Assert.Contains(AuditSource, fixture.Configuration, StringComparison.Ordinal);
    }

    /// <summary>Existing package content can be cached, but advisory HTTP data must be run-owned.</summary>
    [Fact]
    public void Runner_isolates_advisory_cache_without_deleting_the_callers_cache()
    {
        // Arrange
        using var fixture = new AuditFixture();
        var originalCache = Path.Combine(fixture.Root, "existing-cache");
        Directory.CreateDirectory(originalCache);
        var marker = Path.Combine(originalCache, "keep.txt");
        File.WriteAllText(marker, "keep");

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["NUGET_HTTP_CACHE_PATH"] = originalCache });

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("keep", File.ReadAllText(marker));
        var cachePath = Assert.Single(fixture.Commands
            .Select(command => command.GetProperty("httpCache").GetString()!)
            .Distinct(StringComparer.Ordinal));

        Assert.StartsWith(fixture.TemporaryPath + Path.DirectorySeparatorChar, cachePath, StringComparison.Ordinal);
        Assert.False(Directory.Exists(cachePath));
    }

    /// <summary>Preserves reviewed locks without applying locked mode to projects which do not own one.</summary>
    [Fact]
    public void Runner_retains_locks_and_selects_locked_mode_per_project()
    {
        // Arrange
        using var fixture = new AuditFixture();
        var lockPath = Path.Combine(fixture.Root, "src/Alpha/packages.lock.json");
        var projectPath = Path.Combine(fixture.Root, "src/Alpha/Alpha.csproj");
        const string lockBytes = "{\"version\":1}\n";
        File.WriteAllText(lockPath, lockBytes);

        // Act
        var result = fixture.Execute();

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(lockBytes, File.ReadAllText(lockPath));
        var restoreCommands = fixture.Commands.Where(command => Argument(command, 0) == "restore").ToArray();
        Assert.Single(restoreCommands, command => Arguments(command).Contains("--locked-mode", StringComparer.Ordinal));
        Assert.Contains(restoreCommands, command =>
            Arguments(command).Contains(projectPath, StringComparer.Ordinal)
            && Arguments(command).Contains("--locked-mode", StringComparer.Ordinal));
    }

    /// <summary>Retains a visible failing result and continues collecting the other project reports.</summary>
    /// <param name="failure">The independent failing boundary injected into the fake CLI.</param>
    [Theory]
    [InlineData("restore")]
    [InlineData("command")]
    [InlineData("warning")]
    [InlineData("wrong-project")]
    [InlineData("malformed")]
    [InlineData("vulnerable")]
    public void Runner_collects_later_projects_after_an_audit_failure(
        string failure
    )
    {
        // Arrange
        using var fixture = new AuditFixture();

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["DOKA_FIXTURE_FAILURE"] = failure });

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Equal(1, fixture.Summary.GetProperty("failedCount").GetInt32());
        Assert.Equal(5, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.Equal(4, fixture.Summary.GetProperty("passedCount").GetInt32());
        Assert.Equal(5, fixture.Commands.Count(command => Argument(command, 0) == "restore"));
        Assert.Contains("failed", fixture.StepSummary, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(fixture.RunPath, "*.log", SearchOption.AllDirectories));
    }

    /// <summary>Fails instead of silently auditing a reduced set when one source group disappears.</summary>
    [Fact]
    public void Runner_rejects_a_missing_source_group()
    {
        // Arrange
        using var fixture = new AuditFixture();
        Directory.Delete(Path.Combine(fixture.Root, "tests"), recursive: true);

        // Act
        var result = fixture.Execute();

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Contains("tests", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>An early SDK guard failure still produces a failing process and retained diagnostics.</summary>
    [Fact]
    public void Runner_preserves_an_early_infrastructure_failure()
    {
        // Arrange
        using var fixture = new AuditFixture();

        // Act
        var result = fixture.Execute(new Dictionary<string, string> { ["DOKA_FIXTURE_FAILURE"] = "sdk" });

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Equal(0, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.Contains("exact .NET SDK", File.ReadAllText(Path.Combine(fixture.RunPath, "sdk.log")),
            StringComparison.Ordinal);
    }

    /// <summary>A successfully audited prefix cannot satisfy the complete frozen inventory.</summary>
    [Fact]
    public void Runner_rejects_a_successful_but_incomplete_loop()
    {
        // Arrange
        using var fixture = new AuditFixture();
        var scriptPath = Path.Combine(fixture.Root, "eng/quality/audit-dependencies.sh");
        var script = File.ReadAllText(scriptPath);
        // WHY: Simulate a host interruption with a zero compound-command status,
        // without sending signals to the test runner or depending on shell bugs.
        File.WriteAllText(scriptPath, script.Replace(
            "done < \"${run_root}/inventory.txt\"",
            "    break\ndone < \"${run_root}/inventory.txt\"",
            StringComparison.Ordinal));

        // Act
        var result = fixture.Execute();

        // Assert
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("failed", fixture.Summary.GetProperty("result").GetString());
        Assert.Equal(5, fixture.Summary.GetProperty("projectCount").GetInt32());
        Assert.Equal(1, fixture.Summary.GetProperty("passedCount").GetInt32());
    }

    private static string? Argument(
        JsonElement command,
        int index
    ) => command.GetProperty("arguments")[index].GetString();

    private static string[] Arguments(
        JsonElement command
    ) => command.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private sealed class AuditFixture : IDisposable
    {
        private readonly TestDirectory _directory = new();

        /// <summary>Copies only the audit boundary and supplies an offline deterministic CLI.</summary>
        public AuditFixture()
        {
            Directory.CreateDirectory(TemporaryPath);
            var repository = FindRepositoryRoot();
            foreach (var relative in new[]
                     {
                         "eng/common/verify-dotnet.sh", "eng/quality/audit-dependencies.sh",
                         "eng/quality/check-vulnerability-audit.sh", "global.json",
                     })
            {
                var destination = Path.Combine(Root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(repository, relative), destination);
            }

            foreach (var relative in new[]
                     {
                         "src/Alpha/Alpha.csproj", "tests/Test/Test.csproj", "benchmarks/Bench/Bench.csproj",
                         "eng/tools/Tool/Tool.csproj", "examples/Example/Example.csproj",
                     })
            {
                AddProject(relative);
            }

            WriteExecutable("dotnet", FakeDotnet);
        }

        /// <summary>Gets the fixture's source root.</summary>
        public string Root => _directory.Path;

        /// <summary>Gets disposable process state outside the retained evidence directory.</summary>
        // WHY: A shared prefix proves the overlap guard accepts siblings, not only unrelated paths.
        public string TemporaryPath => Path.Combine(Root, "evidence-temporary");

        /// <summary>Gets the runner-owned evidence directory.</summary>
        public string RunPath => Directory.GetDirectories(Path.Combine(Root, "evidence")).Single();

        /// <summary>Gets the final machine-readable result.</summary>
        public JsonElement Summary => JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(Path.Combine(RunPath, "summary.json")));

        /// <summary>Gets the exact commands the audit runner requested.</summary>
        public JsonElement[] Commands => File.ReadAllLines(Path.Combine(Root, "commands.jsonl"))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();

        /// <summary>Gets the explicit isolated package and advisory configuration.</summary>
        public string Configuration => File.ReadAllText(Path.Combine(RunPath, "NuGet.Config"));

        /// <summary>Gets the operator-facing GitHub job summary.</summary>
        public string StepSummary => File.ReadAllText(Path.Combine(Root, "step-summary.md"));

        /// <summary>Simulates an owned-cache cleanup error without changing fixture disposal.</summary>
        public void RejectCacheCleanup() => WriteExecutable("rm", """
            #!/usr/bin/env bash
            printf '%s\n' 'Simulated cache cleanup failure.' >&2
            exit 74
            """);

        /// <summary>Adds a source project with its parent directories.</summary>
        public void AddProject(
            string relative
        ) => AddTemplate(relative);

        /// <summary>Adds source input without building or restoring any real package.</summary>
        public void AddTemplate(
            string relative
        )
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        }

        /// <summary>Executes the real shell boundary with the fake package CLI.</summary>
        public ProcessResult Execute(
            IReadOnlyDictionary<string, string>? additionalEnvironment = null
        )
        {
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = Path.Combine(Root, "fake-bin") + Path.PathSeparator
                    + Environment.GetEnvironmentVariable("PATH"),
                ["DOKA_FIXTURE_ROOT"] = Root,
                ["DOKA_FIXTURE_FAILURE"] = string.Empty,
                ["GITHUB_STEP_SUMMARY"] = Path.Combine(Root, "step-summary.md"),
                ["TMPDIR"] = TemporaryPath,
            };

            foreach (var (name, value) in additionalEnvironment ?? new Dictionary<string, string>())
            {
                environment[name] = value;
            }

            return Run(Root, Path.Combine(Root, "eng/quality/audit-dependencies.sh"), environment,
                "--output-dir", Path.Combine(Root, "evidence"));
        }

        /// <inheritdoc />
        public void Dispose() => _directory.Dispose();

        /// <summary>Creates a fixture-owned CLI entry point used only by the child audit process.</summary>
        private void WriteExecutable(
            string name,
            string script
        )
        {
            var executable = Path.Combine(Root, "fake-bin", name);
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, script);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    private const string FakeDotnet = """
        #!/usr/bin/env bash
        set -euo pipefail
        if [[ "$1" == "--version" ]]; then
            if [[ "${DOKA_FIXTURE_FAILURE}" == "sdk" ]]; then
                echo "0.0.0"
                exit 0
            fi

            jq -r '.sdk.version' "${DOKA_FIXTURE_ROOT}/global.json"
            exit 0
        fi

        # WHY: Real NuGet HTTP-cache paths include a colon that artifact upload rejects.
        cache_entry='670c1461c29885f9aa22c281d8b7da90845b38e4$ps:_api.nuget.org_v3_index.json'
        mkdir -p "${NUGET_HTTP_CACHE_PATH}/${cache_entry}"
        printf '%s\n' 'Cached repository signatures.' \
            > "${NUGET_HTTP_CACHE_PATH}/${cache_entry}/repository_signatures_5.0.0.dat"

        jq -cn --arg httpCache "${NUGET_HTTP_CACHE_PATH:-}" --args \
            '{arguments:$ARGS.positional,httpCache:$httpCache}' -- "$@" \
            >> "${DOKA_FIXTURE_ROOT}/commands.jsonl"
        project=""
        if [[ "$1" == "restore" ]]; then
            project="$2"
            if [[ "${DOKA_FIXTURE_FAILURE}" == "restore" && "${project}" == */src/Alpha/Alpha.csproj ]]; then
                echo "NU1900: advisory source unavailable" >&2
                exit 42
            fi
            exit 0
        fi

        if [[ "$1" != "package" || "$2" != "list" ]]; then
            echo "Unexpected dotnet command" >&2
            exit 40
        fi

        while (( $# > 0 )); do
            if [[ "$1" == "--project" ]]; then
                project="$2"
                break
            fi
            shift
        done

        failure=""
        if [[ "${project}" == */src/Alpha/Alpha.csproj ]]; then
            failure="${DOKA_FIXTURE_FAILURE}"
        fi

        case "${failure}" in
            command) echo "package audit command failed" >&2; exit 43 ;;
            malformed) echo '{'; exit 0 ;;
            wrong-project) project="${project}.other" ;;
        esac

        jq -n --arg project "${project}" --arg failure "${failure}" \
            '{version:1,parameters:"--vulnerable --include-transitive",sources:["https://api.nuget.org/v3/index.json"],
              projects:[{path:$project}]}
             | if $failure == "warning" then .problems=[{level:"warning",text:"No vulnerability database"}]
               elif $failure == "vulnerable" then
                 .projects[0].frameworks=[{framework:"net10.0",transitivePackages:[
                   {id:"Example",resolvedVersion:"1.0.0",
                   vulnerabilities:[{severity:"High",advisoryurl:"https://example.invalid/a"}]}]}]
               else . end'
        """;
}
