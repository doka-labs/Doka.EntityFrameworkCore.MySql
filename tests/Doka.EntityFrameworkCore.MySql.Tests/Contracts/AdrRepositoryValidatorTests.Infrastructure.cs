namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    private static string FormatErrors(
        AdrValidationReport report
    ) => string.Join(Environment.NewLine, report.Errors);

    private static string ExtractQuotedAssignment(
        string line
    )
    {
        var valueStart = line.IndexOf("=\"", StringComparison.Ordinal);

        Assert.True(valueStart >= 0 && line.EndsWith('"'), $"Invalid quoted assignment: {line}");

        return line[(valueStart + 2)..^1];
    }

    private static string NormalizeBenchmarkPathTemplate(
        string path
    ) => path
        .Replace("${repo_root}/", string.Empty, StringComparison.Ordinal)
        .Replace("${target}", "{target}", StringComparison.Ordinal)
        .Replace("${run_id}", "{run}", StringComparison.Ordinal)
        .Replace("${benchmark_artifacts_dir}", "artifacts/benchmarks/{target}", StringComparison.Ordinal)
        .Replace("${benchmark_run_id}", "{run}", StringComparison.Ordinal)
        .Replace("${WORKFLOW_TARGET}", "{target}", StringComparison.Ordinal)
        .Replace("${WORKFLOW_RUN_ID}", "{run}", StringComparison.Ordinal)
        .Replace("${DOKA_BENCHMARK_TARGET}", "{target}", StringComparison.Ordinal)
        .Replace("${DOKA_BENCHMARK_RUN_ID}", "{run}", StringComparison.Ordinal);

    private static bool IsPathOwnedBy(
        string ownerDirectory,
        string candidatePath
    ) => candidatePath.Equals(ownerDirectory, StringComparison.Ordinal)
        || candidatePath.StartsWith(ownerDirectory + "/", StringComparison.Ordinal);

    private static string GetWorkflowJob(
        string workflow,
        string jobName
    )
    {
        var jobStart = workflow.IndexOf($"  {jobName}:\n", StringComparison.Ordinal);

        Assert.True(jobStart >= 0, $"Workflow job '{jobName}' is missing.");

        var nextJob = workflow.IndexOf("\n  ", jobStart + 1, StringComparison.Ordinal);

        while (nextJob >= 0
               && (nextJob + 3 >= workflow.Length
                   || char.IsWhiteSpace(workflow[nextJob + 3])))
        {
            nextJob = workflow.IndexOf("\n  ", nextJob + 1, StringComparison.Ordinal);
        }

        return nextJob < 0 ? workflow[jobStart..] : workflow[jobStart..nextJob];
    }

    private static string GetWorkflowTriggers(
        string workflow
    )
    {
        var triggerStart = workflow.IndexOf("\non:\n", StringComparison.Ordinal);
        var permissionsStart = workflow.IndexOf("\npermissions:\n", StringComparison.Ordinal);

        Assert.True(triggerStart >= 0, "Workflow trigger block is missing.");
        Assert.True(permissionsStart > triggerStart, "Workflow permissions must follow its triggers.");

        return workflow[(triggerStart + 1)..permissionsStart];
    }

    private static void AssertFastLaneJob(
        string workflow,
        string jobName
    )
    {
        var jobStart = workflow.IndexOf($"  {jobName}:", StringComparison.Ordinal);

        Assert.True(jobStart >= 0, $"Workflow job '{jobName}' is missing.");

        var runsOn = workflow.IndexOf("    runs-on:", jobStart, StringComparison.Ordinal);

        Assert.True(runsOn > jobStart, $"Workflow job '{jobName}' has no runs-on declaration.");

        var jobConditions = workflow[jobStart..runsOn]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("    if:", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            jobConditions.Length <= 1,
            $"Workflow job '{jobName}' must not have multiple job conditions.");
        Assert.All(
            jobConditions,
            condition => Assert.Equal("    if: github.event_name != 'schedule'", condition));
    }

    private static void AssertShellGate(
        string script,
        string firstFollowingCommand,
        string path
    )
    {
        var failFast = script.IndexOf("set -euo pipefail", StringComparison.Ordinal);
        var validator = script.IndexOf(
            "\"${repo_root}/eng/quality/validate-adrs.sh\"",
            StringComparison.Ordinal);

        var followingCommand = validator < 0
            ? -1
            : script.IndexOf(firstFollowingCommand, validator + 1, StringComparison.Ordinal);

        Assert.True(failFast >= 0, $"{path} must enable fail-fast shell behavior.");
        Assert.True(validator > failFast, $"{path} must invoke the ADR validator.");
        Assert.True(followingCommand > validator, $"{path} must validate ADRs before '{firstFollowingCommand}'.");
    }

    /// <summary>
    /// Removes shell continuations and incidental whitespace so repository
    /// contracts validate command semantics instead of source formatting.
    /// </summary>
    private static string NormalizeShellLayout(
        string content
    )
    {
        var withoutContinuations = content
            .Replace("\\\r\n", " ", StringComparison.Ordinal)
            .Replace("\\\n", " ", StringComparison.Ordinal);

        return string.Join(
            " ",
            withoutContinuations.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
