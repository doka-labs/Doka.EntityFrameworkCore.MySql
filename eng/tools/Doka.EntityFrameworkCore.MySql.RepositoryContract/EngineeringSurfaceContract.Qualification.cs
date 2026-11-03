namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class EngineeringSurfaceContract
{
    private static readonly string[] s_qualificationInputs = ["authorization_artifact", "version"];

    /// <summary>
    /// Checks shared qualification ownership, complete aggregation, and the authority available to PR code.
    /// </summary>
    /// <param name="workflow">The reusable qualification workflow source.</param>
    /// <returns>Contract violations, or an empty list when the workflow satisfies every boundary.</returns>
    public static IReadOnlyList<string> ValidateQualificationWorkflow(
        string workflow
    )
    {
        const string name = ".github/workflows/quality-gates.yml";
        // WHY: Explanatory full-line comments cannot grant authority; executable and inline text stays visible.
        var source = string.Join("\n", workflow.Split('\n')
            .Where(static line => !line.TrimStart().StartsWith('#')));

        string[] producers =
        [
            "quality-gates", "repo-tests", "spec-test-suite", "integration", "foundation",
            "engine-contracts", "sbom", "coverage-gate"
        ];

        var errors = new List<string>();
        var jobsStart = source.IndexOf("\njobs:\n", StringComparison.Ordinal);
        var jobs = jobsStart < 0 ? string.Empty : source[jobsStart..];
        var declared = Regex.Matches(jobs, "^  ([a-z][a-z0-9-]*):$", RegexOptions.Multiline)
            .Select(static match => match.Groups[1].Value)
            .ToArray();

        if (!declared.Order(StringComparer.Ordinal).SequenceEqual(
                producers.Append("required").Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            errors.Add($"{name}: shared qualification must own exactly the complete required job set.");
        }

        var triggers = TriggerBlock(source);
        var inputNames = Regex.Matches(triggers, "^      ([a-z][a-z0-9_]*):$", RegexOptions.Multiline)
            .Select(static match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal);

        if (!inputNames.SequenceEqual(s_qualificationInputs, StringComparer.Ordinal))
        {
            errors.Add($"{name}: shared qualification must expose exactly its version and authorization artifact inputs.");
        }

        foreach (var input in s_qualificationInputs)
        {
            var declaration = Regex.Match(triggers,
                $"^      {input}:\\n(?:(?:        [^\\n]*|)\\n)*",
                RegexOptions.Multiline | RegexOptions.CultureInvariant).Value;

            if (!declaration.Contains("        type: string\n", StringComparison.Ordinal)
                || !declaration.Contains("        required: false\n", StringComparison.Ordinal))
            {
                errors.Add($"{name}: {input} must be an explicit optional typed workflow-call input.");
            }
        }

        if (!triggers.Contains("  workflow_call:\n", StringComparison.Ordinal)
            || triggers.Contains("  pull_request:", StringComparison.Ordinal)
            || triggers.Contains("  push:", StringComparison.Ordinal)
            || triggers.Contains("secrets:", StringComparison.Ordinal))
        {
            errors.Add($"{name}: qualification must expose only its reusable read-only input boundary.");
        }

        foreach (Match permission in Regex.Matches(source,
                     "^ +(?<scope>[a-z-]+): (?<level>read|write)$", RegexOptions.Multiline))
        {
            if (permission.Groups["level"].Value != "read"
                || permission.Groups["scope"].Value is not ("actions" or "contents"))
            {
                errors.Add($"{name}: shared qualification permits only contents: read and actions: read.");
            }
        }

        if (!source.Contains("\npermissions:\n  contents: read\n", StringComparison.Ordinal)
            || Regex.IsMatch(source, "^ *permissions:[ \\t]*[^ \\t\\r\\n]", RegexOptions.Multiline)
            || Regex.IsMatch(source, "^ +(?:environment|secrets):", RegexOptions.Multiline)
            || source.Contains("secrets.", StringComparison.Ordinal)
            || source.Contains("eng/performance/", StringComparison.Ordinal)
            || source.Contains("eng/benchmark.sh", StringComparison.Ordinal)
            || source.Contains("NuGet/login@", StringComparison.Ordinal)
            || source.Contains("eng.release.publication", StringComparison.Ordinal)
            || source.Replace("eng.release.nuget validate-version", string.Empty, StringComparison.Ordinal)
                .Contains("eng.release.nuget", StringComparison.Ordinal)
            || source.Contains("uses: ./.github/actions/publish-release", StringComparison.Ordinal)
            || source.Contains("uses: $/.github/actions/publish-release", StringComparison.Ordinal)
            || source.Contains("nuget push", StringComparison.Ordinal)
            || source.Contains("git push", StringComparison.Ordinal))
        {
            errors.Add($"{name}: shared qualification must remain reversible and free of publication or benchmark authority.");
        }

        var required = JobBlock(source, "required");
        var inlineNeeds = Regex.Match(required, "^    needs: \\[(?<jobs>[^\\]\\n]*)\\]$", RegexOptions.Multiline);
        var dependencies = inlineNeeds.Success
            ? inlineNeeds.Groups["jobs"].Value.Split(',', StringSplitOptions.TrimEntries)
            : Regex.Matches(required, "^      - ([a-z][a-z0-9-]*)$", RegexOptions.Multiline)
                .Select(static match => match.Groups[1].Value)
                .ToArray();

        var aggregate = string.Join(" ", required.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!dependencies.Order(StringComparer.Ordinal).SequenceEqual(
                producers.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !required.Contains("if: always()", StringComparison.Ordinal)
            || !required.Contains("RESULTS: ${{ toJSON(needs) }}", StringComparison.Ordinal)
            || !aggregate.Contains(
                "bash eng/quality/require-qualification.sh " + string.Join(" ", producers),
                StringComparison.Ordinal))
        {
            errors.Add($"{name}: required must fail closed on exactly every qualification producer.");
        }

        return errors;
    }
}
