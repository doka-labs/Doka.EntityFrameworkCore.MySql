using System.Xml.Linq;

namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class EngineeringSurfaceContract
{
    private static readonly string[] s_retiredFiles =
    [
        ".github/workflows/benchmark-scorecard.yml",
        ".github/workflows/benchmark-smoke.yml",
        ".github/workflows/benchmark-target.yml",
        ".github/workflows/main-admission.yml",
        "eng/performance/paired-benchmark.sh",
        "eng/tests/test_release_workflow_policy.py",
        "eng/tests/test_workflow_orchestration.py"
    ];

    private static readonly string[] s_retiredPythonPolicyTests =
    [
        "test_baseline_mode_resolution.py",
        "test_benchmark_profile_resolution.py",
        "test_benchmark_ratio_gate.py",
        "test_benchmark_workflow_state.py",
        "test_commit_message.py",
        "test_compose_security.py",
        "test_coverage_policy.py",
        "test_dependency_contract.py",
        "test_dependency_snapshot_readiness.py",
        "test_documentation_contract.py",
        "test_dotnet_contract.py",
        "test_engineering_structure.py",
        "test_example_contract.py",
        "test_executable_chain.py",
        "test_image_pins.py",
        "test_lint_tool_resolution.py",
        "test_measurement_quality_policy.py",
        "test_paired_attempt_handoff.py",
        "test_paired_runtime_guards.py",
        "test_performance_attempts.py",
        "test_performance_baseline.py",
        "test_performance_confirmation.py",
        "test_performance_contract.py",
        "test_performance_host.py",
        "test_performance_paired.py",
        "test_performance_reports.py",
        "test_performance_sensitivity.py",
        "test_test_runner.py",
        "test_vulnerability_audit_gate.py"
    ];

    public static IReadOnlyList<string> Validate(
        string repositoryRoot
    )
    {
        var errors = new List<string>();
        AddPythonBoundaryErrors(repositoryRoot, errors);
        ValidateRetiredSurface(repositoryRoot, errors);
        ValidateWorkflowBoundary(repositoryRoot, errors);
        errors.AddRange(ValidateWorkflowConcurrencyFiles(repositoryRoot));
        ValidateArchitectureManifest(repositoryRoot, errors);
        ValidateShellPortability(repositoryRoot, errors);
        ValidateComposeBoundary(repositoryRoot, errors);
        ValidateDependencyBoundary(repositoryRoot, errors);
        ValidateTestRunnerBoundary(repositoryRoot, errors);
        ValidateQualityToolBoundary(repositoryRoot, errors);
        ValidateReferencedScripts(repositoryRoot, errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidatePythonBoundary(
        string repositoryRoot
    )
    {
        var errors = new List<string>();
        AddPythonBoundaryErrors(repositoryRoot, errors);
        return errors;
    }

    /// <summary>
    /// Validates the workflow queue fields not yet understood by the pinned actionlint.
    /// </summary>
    public static IReadOnlyList<string> ValidateWorkflowConcurrencyFiles(
        string repositoryRoot
    )
    {
        var workflows = Path.Combine(repositoryRoot, ".github", "workflows");
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(workflows))
        {
            if (Path.GetExtension(path) is not (".yml" or ".yaml"))
            {
                continue;
            }

            errors.AddRange(ValidateWorkflowConcurrency(File.ReadAllText(path), Relative(repositoryRoot, path)));
        }

        return errors;
    }

    /// <summary>
    /// Validates literal queue choices and their cancellation contract at both supported scopes.
    /// </summary>
    /// <remarks>
    /// This is a narrow extension, not another YAML parser. Actionlint remains
    /// responsible for YAML syntax, placement, expressions and all other keys.
    /// </remarks>
    public static IReadOnlyList<string> ValidateWorkflowConcurrency(
        string workflow,
        string name
    )
    {
        var errors = new List<string>();
        var lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var scalarIndent = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var indent = line.Length - line.TrimStart(' ').Length;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (scalarIndent >= 0 && indent > scalarIndent)
            {
                continue;
            }

            scalarIndent = BlockScalarHeader().IsMatch(line) ? indent : -1;
            var mapping = WorkflowScalarField().Match(line);
            if (line.TrimStart().StartsWith('?') || line.TrimStart().StartsWith('!')
                || mapping.Success && mapping.Groups["key"].Value.Contains('\\')
                || indent is 0 or 4 && line.TrimStart().StartsWith('"') && !mapping.Success)
            {
                // WHY: Tagged/explicit keys can change the YAML name seen by
                // actionlint without appearing as a literal concurrency key.
                errors.Add($"{name}: workflow mappings must use explicit untagged and unescaped scalar keys.");
                continue;
            }

            var header = ConcurrencyHeader().Match(line);
            if (!header.Success)
            {
                continue;
            }

            if (indent is not (0 or 4))
            {
                errors.Add($"{name}: concurrency requires workflow or job scope with canonical two-space indentation.");
                continue;
            }

            var value = TrimYamlComment(header.Groups["value"].Value);
            if (value.StartsWith('{') || value.StartsWith('&') || value.StartsWith('*'))
            {
                // WHY: A flow map or YAML alias could hide a queue from this
                // narrow check while actionlint reports only its unknown key.
                errors.Add($"{name}: concurrency queue validation requires an explicit block-style mapping.");
                continue;
            }

            if (value.Length != 0)
            {
                continue;
            }

            ValidateConcurrencyMapping(lines, index + 1, indent, name, errors);
        }

        return errors;
    }

    /// <summary>
    /// Checks the direct children of one concurrency mapping without interpreting unrelated YAML.
    /// </summary>
    private static void ValidateConcurrencyMapping(
        string[] lines,
        int start,
        int parentIndent,
        string name,
        List<string> errors
    )
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var childIndent = -1;
        for (var index = start; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart(' ').Length;
            if (indent <= parentIndent)
            {
                break;
            }

            if (childIndent < 0)
            {
                childIndent = indent;
            }

            var field = WorkflowScalarField().Match(line);
            if (indent != childIndent)
            {
                continue;
            }

            if (!field.Success)
            {
                errors.Add($"{name}: concurrency requires explicit untagged scalar keys.");
                continue;
            }

            var key = UnquoteYamlScalar(field.Groups["key"].Value);
            if (key is not ("group" or "queue" or "cancel-in-progress"))
            {
                errors.Add($"{name}: concurrency contains an unsupported or escaped key '{key}'.");
                continue;
            }

            var value = TrimYamlComment(field.Groups["value"].Value);
            if (!fields.TryAdd(key, value))
            {
                errors.Add($"{name}: concurrency contains duplicate '{key}' fields.");
            }
        }

        if (!fields.TryGetValue("queue", out var queue))
        {
            return;
        }

        var normalized = UnquoteYamlScalar(queue);
        if (normalized is not ("single" or "max"))
        {
            errors.Add($"{name}: concurrency queue must be the string 'single' or 'max'.");
            return;
        }

        if (!fields.TryGetValue("cancel-in-progress", out var cancellation))
        {
            return;
        }

        var expression = UnquoteYamlScalar(cancellation);
        var isExpression = expression.StartsWith("${{", StringComparison.Ordinal)
            && expression.EndsWith("}}", StringComparison.Ordinal);

        if (cancellation is not ("true" or "false") && !isExpression)
        {
            errors.Add($"{name}: concurrency cancel-in-progress must be a boolean or an expression, not a string.");
        }

        // WHY: GitHub rejects max with cancellation; a dynamic expression cannot
        // prove that the shared publication queue will remain non-cancelling.
        if (normalized == "max" && cancellation != "false")
        {
            errors.Add($"{name}: concurrency queue 'max' requires absent or literal false cancel-in-progress.");
        }
    }

    private static string TrimYamlComment(
        string value
    )
    {
        if (value.TrimStart().StartsWith('#'))
        {
            return string.Empty;
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);

        return (comment < 0 ? value : value[..comment]).Trim();
    }

    private static string UnquoteYamlScalar(
        string value
    ) => value.Length >= 2 && (value[0] == '\'' && value[^1] == '\'' || value[0] == '"' && value[^1] == '"')
        ? value[1..^1]
        : value;

    private static void AddPythonBoundaryErrors(
        string root,
        List<string> errors
    )
    {
        foreach (var domain in new[] { "performance", "quality" })
        {
            var path = Path.Combine(root, "eng", domain);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var python in Directory.EnumerateFiles(path, "*.py", SearchOption.AllDirectories))
            {
                errors.Add($"{Relative(root, python)}: Python is not allowed in eng/{domain}.");
            }
        }

        var testRunner = File.ReadAllText(Path.Combine(root, "eng", "testing", "test.sh"));
        if (testRunner.Contains("python3", StringComparison.Ordinal)
            || testRunner.Contains("unittest", StringComparison.Ordinal))
        {
            errors.Add("eng/testing/test.sh: the general provider test path must not run Python self-tests.");
        }

        var releaseTestPath = Path.Combine(root, "eng", "release", "test-tools.sh");
        var releaseTests = File.Exists(releaseTestPath) ? File.ReadAllText(releaseTestPath) : string.Empty;
        foreach (var python in Directory.EnumerateFiles(Path.Combine(root, "eng"), "*.py", SearchOption.AllDirectories))
        {
            var relative = Relative(root, python);
            if (relative.StartsWith("eng/tests/", StringComparison.Ordinal))
            {
                var module = "eng.tests." + Path.GetFileNameWithoutExtension(python);
                if (!releaseTests.Contains(module, StringComparison.Ordinal))
                {
                    errors.Add($"{relative}: Python test has no retained release owner.");
                }

                continue;
            }

            if (relative.StartsWith("eng/performance/", StringComparison.Ordinal)
                || relative.StartsWith("eng/quality/", StringComparison.Ordinal))
            {
                continue;
            }

            if (relative.StartsWith("eng/release/", StringComparison.Ordinal)
                || relative is "eng/common/__init__.py" or "eng/common/deadline.py" or "eng/testing/spec_matrix.py")
            {
                continue;
            }

            errors.Add($"{relative}: Python is limited to release, deadline, and specification matrix logic.");
        }
    }

    private static void ValidateRetiredSurface(
        string root,
        List<string> errors
    )
    {
        foreach (var relative in s_retiredFiles)
        {
            if (File.Exists(Path.Combine(root, relative)))
            {
                errors.Add($"{relative}: retired engineering-framework surface still exists.");
            }
        }

        var testRoot = Path.Combine(root, "eng", "tests");
        foreach (var name in s_retiredPythonPolicyTests)
        {
            if (File.Exists(Path.Combine(testRoot, name)))
            {
                errors.Add($"eng/tests/{name}: retired Python policy test still exists.");
            }
        }
    }

    private static void ValidateWorkflowBoundary(
        string root,
        List<string> errors
    )
    {
        var workflows = Path.Combine(root, ".github", "workflows");
        var ci = File.ReadAllText(Path.Combine(workflows, "ci.yml"));
        var benchmark = File.ReadAllText(Path.Combine(workflows, "benchmark.yml"));
        var containerMatrix = File.ReadAllText(Path.Combine(workflows, "container-matrix.yml"));
        var scorecard = File.ReadAllText(Path.Combine(workflows, "scorecard.yml"));
        var dependencyReview = File.ReadAllText(Path.Combine(workflows, "dependency-review.yml"));
        var securityAudit = File.ReadAllText(Path.Combine(workflows, "security-audit.yml"));
        errors.AddRange(ValidateHostedBenchmarkWorkflow(benchmark));

        if (!TriggerBlock(ci)
                .Contains("  pull_request:\n", StringComparison.Ordinal)
            || TriggerBlock(ci)
                .Contains("  push:\n", StringComparison.Ordinal))
        {
            errors.Add(".github/workflows/ci.yml: product qualification must run on PRs, not push main.");
        }

        foreach (var (name, workflow) in new[] { ("benchmark.yml", benchmark), ("scorecard.yml", scorecard) })
        {
            var triggers = TriggerBlock(workflow);
            if (!triggers.Contains("  workflow_dispatch:\n", StringComparison.Ordinal)
                || !triggers.Contains("  schedule:\n", StringComparison.Ordinal)
                || triggers.Contains("  push:\n", StringComparison.Ordinal))
            {
                errors.Add($".github/workflows/{name}: must be scheduled/manual and must not run on push.");
            }
        }

        if (ci.Contains("merge_group", StringComparison.Ordinal)
            || ci.Contains("main-admission", StringComparison.Ordinal)
            || ci.Contains("baseline-proposal", StringComparison.Ordinal))
        {
            errors.Add(".github/workflows/ci.yml: obsolete admission or benchmark policy remains.");
        }

        foreach (var job in new[]
                 {
                     "quality-gates", "repo-tests", "spec-test-suite", "coverage-gate",
                     "integration-smoke"
                 })
        {
            if (!JobBlock(ci, job)
                    .Contains("if: github.event_name != 'schedule' && !inputs.maintenance", StringComparison.Ordinal))
            {
                errors.Add(
                    $".github/workflows/ci.yml: fixed qualification job '{job}' "
                    + "must not repeat on the maintenance schedule.");
            }
        }

        var qualification = JobBlock(ci, "repository-qualification");
        if (!qualification.Contains("if: always()", StringComparison.Ordinal)
            || !qualification.Contains("github.event_name != 'schedule' && !inputs.maintenance", StringComparison.Ordinal))
        {
            errors.Add(
                ".github/workflows/ci.yml: repository-qualification must aggregate PR/manual "
                + "results and remain absent from the maintenance schedule.");
        }

        foreach (var (name, workflow) in new[]
                 {
                     ("ci.yml", ci), ("benchmark.yml", benchmark),
                     ("container-matrix.yml", containerMatrix), ("security-audit.yml", securityAudit)
                 })
        {
            ValidateScheduledWorkflow(name, workflow, errors);
        }

        errors.AddRange(ValidateSecurityAuditWorkflow(securityAudit));

        if (!dependencyReview.Contains("actions/dependency-review-action@", StringComparison.Ordinal)
            || !dependencyReview.Contains("retry-on-snapshot-warnings: true", StringComparison.Ordinal)
            || dependencyReview.Contains("dependency_snapshot_readiness", StringComparison.Ordinal))
        {
            errors.Add(".github/workflows/dependency-review.yml: snapshot readiness must remain platform-owned.");
        }
    }

    /// <summary>Prevents a routine hosted benchmark from becoming a hardware-speed gate again.</summary>
    public static IReadOnlyList<string> ValidateHostedBenchmarkWorkflow(
        string workflow
    )
    {
        var benchmark = JobBlock(workflow, "benchmark");
        if (!benchmark.Contains("DOKA_BENCHMARK_EVALUATION: allocations-only", StringComparison.Ordinal)
            || !benchmark.Contains("bash ./eng/benchmark.sh --up-run-down", StringComparison.Ordinal)
            || benchmark.Contains("continue-on-error:", StringComparison.Ordinal))
        {
            return ["benchmark.yml: hosted execution must enforce allocations/resource correctness without suppressing failures."];
        }

        return [];
    }

    /// <summary>
    /// Keeps scheduled cross-line dispatch separate from branch-native work.
    /// </summary>
    private static void ValidateScheduledWorkflow(
        string name,
        string workflow,
        List<string> errors
    )
    {
        var identity = JobBlock(workflow, "schedule-identity");
        var coordinator = JobBlock(workflow, "scheduled-release-lines");
        var ordinaryJobs = coordinator.Length > 0
            ? workflow.Replace(coordinator, string.Empty, StringComparison.Ordinal)
            : workflow;

        var contracts = workflow.Split('\n')
            .Where(line => line.StartsWith("# release-line-schedule: ", StringComparison.Ordinal))
            .ToArray();

        if (contracts.Length != 1
            || !identity.Contains($"validate-child --workflow {name}", StringComparison.Ordinal)
            || !identity.Contains("EXPECTED_COMMIT: ${{ inputs.expected_commit }}", StringComparison.Ordinal)
            || !identity.Contains("SCHEDULE_CORRELATION: ${{ inputs.schedule_correlation }}", StringComparison.Ordinal)
            || !coordinator.Contains("if: github.event_name == 'schedule'", StringComparison.Ordinal)
            || !coordinator.Contains("actions: write", StringComparison.Ordinal)
            || !coordinator.Contains($"schedules.py run --workflow {name}", StringComparison.Ordinal)
            || ordinaryJobs.Contains("actions: write", StringComparison.Ordinal)
            || workflow.Contains("ref: ${{ inputs.", StringComparison.Ordinal))
        {
            errors.Add($".github/workflows/{name}: scheduled lines require exact branch-native producers and isolated dispatch permissions.");
        }

        if (contracts.Length != 1)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(contracts[0]["# release-line-schedule: ".Length..]);
            var producers = document.RootElement.GetProperty("producers").EnumerateArray().ToArray();
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1
                || producers.Length == 0
                || producers.Any(producer =>
                    !JobBlock(workflow, producer.GetString() ?? string.Empty)
                        .Contains("needs: schedule-identity", StringComparison.Ordinal)
                    && !(producer.GetString() == "benchmark"
                         && JobBlock(workflow, "benchmark")
                             .Contains("needs: resolve-targets", StringComparison.Ordinal)
                         && JobBlock(workflow, "resolve-targets")
                             .Contains("needs: schedule-identity", StringComparison.Ordinal))))
            {
                errors.Add($".github/workflows/{name}: every scheduled producer must depend on the identity guard.");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            errors.Add($".github/workflows/{name}: invalid scheduled producer contract.");
        }
    }

    private static void ValidateArchitectureManifest(
        string root,
        List<string> errors
    )
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "architecture.json")));
        var manifest = document.RootElement;
        if (manifest
                .GetProperty("schemaVersion")
                .GetInt32()
            != 1)
        {
            errors.Add("eng/architecture.json: schemaVersion must be 1.");
        }

        var domains = manifest.GetProperty("domains");
        var domainOwners = domains
            .EnumerateObject()
            .ToDictionary(
                static domain => domain.Name,
                static domain => RequiredString(domain.Value, "owner"),
                StringComparer.Ordinal);
        var entrypoints = manifest
            .GetProperty("rootEntrypoints")
            .EnumerateArray()
            .ToArray();
        var declaredPaths = new HashSet<string>(StringComparer.Ordinal);
        var declaredTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrypoint in entrypoints)
        {
            var path = RequiredString(entrypoint, "path");
            var target = RequiredString(entrypoint, "target");
            var owner = RequiredString(entrypoint, "owner");
            if (!declaredPaths.Add(path)
                || !declaredTargets.Add(target))
            {
                errors.Add($"eng/architecture.json: duplicate entrypoint path or target '{path}'.");
            }

            if (!IsSafeRelativePath(path)
                || !IsSafeRelativePath(target))
            {
                errors.Add($"eng/architecture.json: entrypoint '{path}' has an unsafe path.");
                continue;
            }

            var targetParts = target.Split('/');
            if (targetParts.Length < 3
                || !domainOwners.TryGetValue(targetParts[1], out var domainOwner)
                || !string.Equals(owner, domainOwner, StringComparison.Ordinal))
            {
                errors.Add($"eng/architecture.json: entrypoint '{path}' is not owned by its target domain.");
            }

            var rootPath = Path.Combine(root, path);
            var targetPath = Path.Combine(root, target);
            if (!File.Exists(rootPath)
                || !File.Exists(targetPath))
            {
                errors.Add($"eng/architecture.json: entrypoint '{path}' or target '{target}' is missing.");
                continue;
            }

            var body = File.ReadAllText(rootPath);
            if (!body.StartsWith("#!/usr/bin/env bash\n", StringComparison.Ordinal)
                || !body.Contains("set -euo pipefail", StringComparison.Ordinal)
                || body.Split('\n')
                    .Length
                > 13)
            {
                errors.Add($"{path}: root entrypoint must remain a thin fail-fast Bash facade.");
            }
        }

        var observed = Directory
            .EnumerateFiles(Path.Combine(root, "eng"), "*.sh")
            .Select(path => $"eng/{Path.GetFileName(path)}")
            .ToHashSet(StringComparer.Ordinal);
        if (!observed.SetEquals(declaredPaths))
        {
            errors.Add("eng/architecture.json: root Shell entrypoints do not match the manifest.");
        }
    }

    private static void ValidateShellPortability(
        string root,
        List<string> errors
    )
    {
        foreach (var script in Directory.EnumerateFiles(Path.Combine(root, "eng"), "*.sh", SearchOption.AllDirectories))
        {
            var executable = File
                .ReadLines(script)
                .Where(static line => !line
                    .TrimStart()
                    .StartsWith('#'))
                .ToArray();
            if (executable.Any(line => line.Contains("mapfile", StringComparison.Ordinal)
                    || line.Contains("readarray", StringComparison.Ordinal)
                    || line.Contains("declare -A", StringComparison.Ordinal))
                || executable.Any(line => BashCaseConversion()
                    .IsMatch(line)))
            {
                errors.Add($"{Relative(root, script)}: Shell must remain compatible with Bash 3.2.");
            }
        }
    }

    private static void ValidateComposeBoundary(
        string root,
        List<string> errors
    )
    {
        var published = File
            .ReadLines(Path.Combine(root, "docker", "compose.yml"))
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- \"", StringComparison.Ordinal)
                && line.EndsWith(":3306\"", StringComparison.Ordinal))
            .Select(static line => line[3..^1])
            .ToHashSet(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "127.0.0.1:${DOKA_MYSQL84_PORT:-33068}:3306",
            "127.0.0.1:${DOKA_MYSQL97_PORT:-33070}:3306",
            "127.0.0.1:${DOKA_MARIADB1011_PORT:-33066}:3306",
            "127.0.0.1:${DOKA_MARIADB114_PORT:-33067}:3306",
            "127.0.0.1:${DOKA_MARIADB118_PORT:-33069}:3306",
            "127.0.0.1:${DOKA_MARIADB123_PORT:-33071}:3306"
        };
        if (!published.SetEquals(expected))
        {
            errors.Add("docker/compose.yml: database ports must bind only to IPv4 loopback.");
        }
    }

    private static void ValidateDependencyBoundary(
        string root,
        List<string> errors
    )
    {
        const string specification = "Microsoft.EntityFrameworkCore.Relational.Specification.Tests";
        const string runner = "xunit.runner.visualstudio";
        var consumers = new List<string>();
        foreach (var project in Directory
                     .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !path
                         .Split(Path.DirectorySeparatorChar)
                         .Contains("artifacts", StringComparer.Ordinal)))
        {
            var document = XDocument.Load(project);
            var references = document
                .Descendants("PackageReference")
                .ToArray();
            if (!references.Any(reference => (string?)reference.Attribute("Include") == specification)
                || string.Equals(
                    document
                        .Descendants("IsTestProject")
                        .FirstOrDefault()
                        ?.Value
                        .Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            consumers.Add(Relative(root, project));
            var runnerReferences = references
                .Where(reference => (string?)reference.Attribute("Include") == runner)
                .ToArray();
            if (runnerReferences.Length != 1
                || runnerReferences[0]
                    .Attribute("Version") is not null
                || runnerReferences[0]
                    .Attribute("VersionOverride") is not null
                || runnerReferences[0]
                    .Element("PrivateAssets")
                    ?.Value
                != "all"
                || runnerReferences[0]
                    .Element("ExcludeAssets")
                    ?.Value
                != "all")
            {
                errors.Add($"{Relative(root, project)}: specification consumer must exclude runner assets.");
            }
        }

        var expected = new[]
        {
            "eng/tools/Doka.EntityFrameworkCore.MySql.SpecificationContract/Doka.EntityFrameworkCore.MySql.SpecificationContract.csproj",
            "tests/Doka.EntityFrameworkCore.MySql.SpecificationAdapters/SpecificationAdapters.csproj"
        };
        if (!consumers
                .Order(StringComparer.Ordinal)
                .SequenceEqual(expected, StringComparer.Ordinal))
        {
            errors.Add("Specification package consumers do not match the reviewed dependency boundary.");
        }
    }

    private static void ValidateTestRunnerBoundary(
        string root,
        List<string> errors
    )
    {
        var runner = File.ReadAllText(Path.Combine(root, "eng", "testing", "test.sh"));
        foreach (var project in new[]
                 {
                     "Doka.EntityFrameworkCore.MySql.Tests", "Doka.EntityFrameworkCore.MySql.FunctionalTests",
                     "Doka.EntityFrameworkCore.MySql", "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
                     "Doka.EntityFrameworkCore.MySql.AdrValidator",
                     "Doka.EntityFrameworkCore.MySql.SpecificationContract",
                     "Doka.EntityFrameworkCore.MySql.TestUtilities", "SpecificationAdapters"
                 })
        {
            if (!runner.Contains($"\"{project}\"", StringComparison.Ordinal))
            {
                errors.Add($"eng/testing/test.sh: restore completeness omits '{project}'.");
            }
        }

        foreach (var restoreArtifact in new[]
                 {
                     "${project_obj}/project.assets.json", "${project_obj}/${project_name}.csproj.nuget.g.props",
                     "${project_obj}/${project_name}.csproj.nuget.g.targets"
                 })
        {
            if (!runner.Contains(restoreArtifact, StringComparison.Ordinal))
            {
                errors.Add($"eng/testing/test.sh: restore completeness omits '{restoreArtifact}'.");
            }
        }
    }

    private static void ValidateReferencedScripts(
        string root,
        List<string> errors
    )
    {
        var surfaces = Directory
            .EnumerateFiles(Path.Combine(root, "eng"), "*.sh", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml"));
        foreach (var surface in surfaces)
        {
            foreach (Match match in ReferencedShellScript()
                         .Matches(File.ReadAllText(surface)))
            {
                var relative = match.Groups[1].Value;
                if (!File.Exists(Path.Combine(root, relative)))
                {
                    errors.Add($"{Relative(root, surface)}: referenced script '{relative}' is missing.");
                }
            }
        }
    }

    private static void ValidateQualityToolBoundary(
        string root,
        List<string> errors
    )
    {
        var lint = File.ReadAllText(Path.Combine(root, "eng", "quality", "lint-workflows.sh"));
        foreach (var required in new[]
                 {
                     "elif [[ \"${CI:-false}\" == \"true\" ]]", "DOKA_LINT_AUTO_INSTALL must be 0 or 1",
                     "if ! command -v shellcheck", "--require-hashes", "actionlint_version=\"1.7.12\""
                 })
        {
            if (!lint.Contains(required, StringComparison.Ordinal))
            {
                errors.Add($"eng/quality/lint-workflows.sh: missing toolchain invariant '{required}'.");
            }
        }

        var vulnerability = File.ReadAllText(Path.Combine(root, "eng", "quality", "check-vulnerability-audit.sh"));
        foreach (var required in new[]
                 {
                     ".version == 1", "--vulnerable", "--include-transitive", "vulnerability_count",
                     "does not cover"
                 })
        {
            if (!vulnerability.Contains(required, StringComparison.Ordinal))
            {
                errors.Add($"eng/quality/check-vulnerability-audit.sh: missing audit invariant '{required}'.");
            }
        }
    }

    private static string TriggerBlock(
        string workflow
    )
    {
        var start = workflow.IndexOf("\non:\n", StringComparison.Ordinal);
        var end = workflow.IndexOf("\npermissions:\n", StringComparison.Ordinal);
        return start >= 0 && end > start ? workflow[(start + 1)..end] : string.Empty;
    }

    private static string JobBlock(
        string workflow,
        string job
    )
    {
        var start = workflow.IndexOf($"\n  {job}:\n", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start++;
        var end = workflow.IndexOf("\n  ", start + job.Length + 4, StringComparison.Ordinal);
        while (end >= 0
               && end + 3 < workflow.Length
               && workflow[end + 3] == ' ')
        {
            end = workflow.IndexOf("\n  ", end + 3, StringComparison.Ordinal);
        }

        return end < 0 ? workflow[start..] : workflow[start..end];
    }

    private static string RequiredString(
        JsonElement element,
        string property
    ) => element
            .GetProperty(property)
            .GetString()
        ?? throw new InvalidDataException($"Engineering property '{property}' is null.");

    private static bool IsSafeRelativePath(
        string path
    ) => !Path.IsPathRooted(path)
        && !path
            .Split('/')
            .Contains("..", StringComparer.Ordinal)
        && path == path.Replace('\\', '/');

    private static string Relative(
        string root,
        string path
    ) => Path
        .GetRelativePath(root, path)
        .Replace('\\', '/');

    [GeneratedRegex("\\$\\{[^}\\n]*(?:,,|\\^\\^)[^}\\n]*\\}")]
    private static partial Regex BashCaseConversion();

    [GeneratedRegex("(?:bash|exec)\\s+[\"']?(?:\\./)?(eng/[\\w./-]+[.]sh)")]
    private static partial Regex ReferencedShellScript();

    [GeneratedRegex("^(?<indent> *)(?:concurrency|'concurrency'|\"concurrency\"):[ \\t]*(?<value>.*)$")]
    private static partial Regex ConcurrencyHeader();

    [GeneratedRegex("^ *(?<key>[A-Za-z-]+|'[^']+'|\"[^\"]+\"):[ \\t]*(?<value>.*)$")]
    private static partial Regex WorkflowScalarField();

    [GeneratedRegex(":[ \\t]*[|>][-+0-9]*[ \\t]*(?:#.*)?$")]
    private static partial Regex BlockScalarHeader();
}
