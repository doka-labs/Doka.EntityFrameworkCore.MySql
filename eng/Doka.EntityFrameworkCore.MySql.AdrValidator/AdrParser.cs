namespace Doka.EntityFrameworkCore.MySql.AdrValidator;

internal static partial class AdrParser
{
    private const string RepositoryEvidenceSource = "- No external sources; repository evidence only.";

    private static readonly string[] s_metadataKeys =
    [
        "id",
        "status",
        "date",
        "decision-makers",
        "consulted",
        "informed",
        "scope",
        "supersedes",
        "superseded-by",
        "amends",
        "amended-by",
        "madr-version",
        "doka-profile-version",
    ];

    private static readonly HashSet<string> s_statuses = new(
        [
            "proposed",
            "accepted",
            "implemented",
            "rejected",
            "deprecated",
            "superseded",
        ],
        StringComparer.Ordinal);

    private static readonly (string Label, string Key)[] s_bodyMetadataKeys =
    [
        ("Status", "status"),
        ("Date", "date"),
        ("Scope", "scope")
    ];

    private static readonly HashSet<(string From, string To)> s_statusTransitions =
    [
        ("proposed", "accepted"),
        ("proposed", "rejected"),
        ("accepted", "implemented"),
        ("accepted", "deprecated"),
        ("accepted", "superseded"),
        ("implemented", "deprecated"),
        ("implemented", "superseded"),
    ];

    private static readonly string[] s_requiredHeadings =
    [
        "## Context and Problem Statement",
        "## Decision Drivers",
        "## Considered Options",
        "## Decision Outcome",
        "### Consequences",
        "### Confirmation",
        "## Pros and Cons of the Options",
        "## More Information",
        "### Re-evaluation Triggers",
        "### Decision History",
        "### Implementation References",
        "### Sources",
    ];

    /// <summary>
    /// Parses and validates one ADR against the complete Doka profile contract.
    /// </summary>
    /// <param name="path">The absolute path of the ADR file.</param>
    /// <param name="relativePath">The repository-relative path used in diagnostics.</param>
    /// <returns>The parsed decision when identity metadata is usable and all diagnostics.</returns>
    public static AdrParseResult Parse(
        string path,
        string relativePath
    )
    {
        var errors = new List<AdrValidationError>();
        var bytes = File.ReadAllBytes(path);

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] <= 0x7F)
            {
                continue;
            }

            errors.Add(
                new AdrValidationError(relativePath, null, $"Non-ASCII byte 0x{bytes[index]:X2} at offset {index}."));
            break;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var metadata = ParseMetadata(lines, relativePath, errors, out var bodyStart);
        var title = ParseTitle(lines, bodyStart, relativePath, errors);

        ValidateBodyStructure(lines, bodyStart, relativePath, errors);
        ValidateRequiredHeadings(lines, relativePath, errors);
        ValidateDecisionContent(lines, relativePath, errors);
        ValidateSources(lines, relativePath, errors);

        if (!TryBuildDocument(relativePath, metadata, title, errors, out var document))
        {
            return new AdrParseResult(null, errors);
        }

        ValidateStatusHistory(lines, document!.Status, relativePath, errors);
        return new AdrParseResult(document, errors);
    }

    private static Dictionary<string, string> ParseMetadata(
        string[] lines,
        string relativePath,
        List<AdrValidationError> errors,
        out int bodyStart
    )
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        bodyStart = 0;

        if (lines.Length == 0
            || lines[0] != "---")
        {
            errors.Add(new AdrValidationError(relativePath, 1, "The document must begin with YAML front matter."));
            return metadata;
        }

        var closingIndex = Array.FindIndex(lines, 1, static line => line == "---");
        if (closingIndex < 0)
        {
            errors.Add(new AdrValidationError(relativePath, 1, "The YAML front matter has no closing delimiter."));
            return metadata;
        }

        bodyStart = closingIndex + 1;
        var observedKeys = new List<string>();

        for (var index = 1; index < closingIndex; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        index + 1,
                        "Metadata must use one flat 'key: value' entry per line."));
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..]
                .Trim();
            observedKeys.Add(key);

            if (!metadata.TryAdd(key, value))
            {
                errors.Add(new AdrValidationError(relativePath, index + 1, $"Duplicate metadata key '{key}'."));
            }
        }

        if (!observedKeys.SequenceEqual(s_metadataKeys, StringComparer.Ordinal))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    1,
                    $"Metadata keys must appear exactly in this order: {string.Join(", ", s_metadataKeys)}."));
        }

        return metadata;
    }

    private static (string Id, string Title)? ParseTitle(
        string[] lines,
        int bodyStart,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var titleIndex = Array.FindIndex(lines, bodyStart, static line => line.Length > 0);
        if (titleIndex < 0)
        {
            errors.Add(new AdrValidationError(relativePath, null, "The ADR has no title."));
            return null;
        }

        var match = TitleRegex().Match(lines[titleIndex]);

        if (!match.Success)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    titleIndex + 1,
                    "The title must use '# D-NNN -- Short decision title'."));
            return null;
        }

        return (match.Groups["id"].Value, match.Groups["title"].Value);
    }

    private static void ValidateBodyStructure(
        string[] lines,
        int bodyStart,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        for (var index = bodyStart; index < lines.Length; index++)
        {
            var line = lines[index];
            if (IsMarkdownHeading(line)
                && index > bodyStart
                && lines[index - 1].Length > 0)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        index + 1,
                        "Markdown headings must be preceded by a blank line."));
            }

            if (line
                .TrimStart('#', ' ')
                .Equals("Original record metadata", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        index + 1,
                        "Legacy 'Original record metadata' blocks are forbidden; "
                        + "front matter is the single metadata source."));
            }

            foreach (var (label, key) in s_bodyMetadataKeys)
            {
                if (!line.StartsWith($"- **{label}:**", StringComparison.Ordinal))
                {
                    continue;
                }

                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        index + 1,
                        $"ADR bodies must not repeat the front matter '{key}' key."));
            }
        }
    }

    private static bool IsMarkdownHeading(
        string line
    )
    {
        var markerLength = 0;
        while (markerLength < line.Length
               && line[markerLength] == '#')
        {
            markerLength++;
        }

        return markerLength is >= 1 and <= 6 && markerLength < line.Length && line[markerLength] == ' ';
    }

    private static void ValidateRequiredHeadings(
        string[] lines,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var previousIndex = -1;
        foreach (var heading in s_requiredHeadings)
        {
            var matchingIndices = Enumerable
                .Range(0, lines.Length)
                .Where(index => lines[index] == heading)
                .ToArray();

            if (matchingIndices.Length == 0)
            {
                errors.Add(new AdrValidationError(relativePath, null, $"Missing required heading '{heading}'."));
                continue;
            }

            if (matchingIndices.Length > 1)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        matchingIndices[1] + 1,
                        $"Heading '{heading}' must appear exactly once."));
            }

            var index = matchingIndices[0];
            if (index <= previousIndex)
            {
                errors.Add(
                    new AdrValidationError(relativePath, index + 1, $"Heading '{heading}' is out of canonical order."));
            }

            previousIndex = index;
        }
    }

    private static void ValidateDecisionContent(
        string[] lines,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var headingPositions = s_requiredHeadings
            .Select(heading => (Heading: heading, Index: Array.FindIndex(lines, line => line == heading)))
            .ToArray();

        if (headingPositions.Any(static position => position.Index < 0))
        {
            return;
        }

        if (headingPositions
            .Zip(
                headingPositions.Skip(1),
                static (current, next) => current.Index >= next.Index)
            .Any(static outOfOrder => outOfOrder))
        {
            return;
        }

        for (var index = 0; index < headingPositions.Length; index++)
        {
            var start = headingPositions[index].Index + 1;
            var end = index + 1 < headingPositions.Length
                ? headingPositions[index + 1].Index
                : lines.Length;

            if (!lines[start..end]
                    .Any(static line => line.Length > 0))
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        headingPositions[index].Index + 1,
                        $"Section '{headingPositions[index].Heading}' must not be empty."));
            }
        }

        var consideredStart = headingPositions[2].Index + 1;
        var consideredEnd = headingPositions[3].Index;
        var options = lines[consideredStart..consideredEnd]
            .Where(static line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(static line => line[2..]
                .Trim())
            .Where(static line => line.Length > 0)
            .ToArray();

        if (options.Length < 2)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingPositions[2].Index + 1,
                    "Considered Options must list at least two options."));
            return;
        }

        if (options.Distinct(StringComparer.Ordinal).Count() != options.Length)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingPositions[2].Index + 1,
                    "Considered option titles must be unique."));
        }

        var outcomeLines = lines[(headingPositions[3].Index + 1)..headingPositions[4].Index];
        var outcome = string.Join(" ", outcomeLines);
        var chosenMatch = ChosenOptionRegex().Match(outcome);

        if (!chosenMatch.Success)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingPositions[3].Index + 1,
                    "Decision Outcome must contain 'Chosen option: \"...\", because ...'."));
        }
        else if (!options.Contains(chosenMatch.Groups["option"].Value, StringComparer.Ordinal))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingPositions[3].Index + 1,
                    "The chosen option must exactly match one Considered Options entry."));
        }

        ValidateGoodBadSection(
            lines,
            headingPositions[4].Index,
            headingPositions[5].Index,
            relativePath,
            "Consequences",
            errors);
        ValidateBulletSection(
            lines,
            headingPositions[5].Index,
            headingPositions[6].Index,
            relativePath,
            "Confirmation",
            errors);
        ValidateOptionTradeoffs(
            lines,
            headingPositions[6].Index,
            headingPositions[7].Index,
            options,
            relativePath,
            errors);
        ValidateHistory(lines, headingPositions[9].Index, headingPositions[10].Index, relativePath, errors);
        ValidateBulletSection(
            lines,
            headingPositions[10].Index,
            headingPositions[11].Index,
            relativePath,
            "Implementation References",
            errors);
    }

    private static void ValidateGoodBadSection(
        string[] lines,
        int headingIndex,
        int end,
        string relativePath,
        string sectionName,
        List<AdrValidationError> errors
    )
    {
        var section = lines[(headingIndex + 1)..end];
        if (!section.Any(static line => line.StartsWith("- Good, because ", StringComparison.Ordinal)))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingIndex + 1,
                    $"{sectionName} must contain a '- Good, because ...' entry."));
        }

        if (!section.Any(static line => line.StartsWith("- Bad, because ", StringComparison.Ordinal)))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingIndex + 1,
                    $"{sectionName} must contain a '- Bad, because ...' entry."));
        }
    }

    private static void ValidateOptionTradeoffs(
        string[] lines,
        int headingIndex,
        int end,
        IReadOnlyList<string> options,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        foreach (var option in options)
        {
            var optionHeading = $"### {option}";
            var optionIndex = Array.FindIndex(
                lines,
                headingIndex + 1,
                end - headingIndex - 1,
                line => line == optionHeading);
            if (optionIndex < 0)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        headingIndex + 1,
                        $"Missing trade-off section '{optionHeading}'."));
                continue;
            }

            var nextHeading = Array.FindIndex(
                lines,
                optionIndex + 1,
                end - optionIndex - 1,
                static line => line.StartsWith("### ", StringComparison.Ordinal));
            var optionEnd = nextHeading < 0 ? end : nextHeading;
            ValidateGoodBadSection(lines, optionIndex, optionEnd, relativePath, $"Option '{option}'", errors);
        }
    }

    private static void ValidateHistory(
        string[] lines,
        int headingIndex,
        int end,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var entries = lines[(headingIndex + 1)..end]
            .Where(static line => line.StartsWith("- ", StringComparison.Ordinal))
            .ToArray();

        if (entries.Length == 0)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    headingIndex + 1,
                    "Decision History must contain at least one dated entry."));
            return;
        }

        foreach (var entry in entries)
        {
            var match = HistoryEntryRegex().Match(entry);

            if (!match.Success)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        headingIndex + 1,
                        $"Invalid Decision History entry '{entry}'."));
                continue;
            }

            if (!DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        headingIndex + 1,
                        $"Decision History entry has an invalid date: '{entry}'."));
            }
        }
    }

    private static void ValidateStatusHistory(
        string[] lines,
        string metadataStatus,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var historyIndex = Array.FindIndex(lines, static line => line == "### Decision History");
        var implementationIndex = Array.FindIndex(lines, static line => line == "### Implementation References");
        if (historyIndex < 0
            || implementationIndex < 0)
        {
            return;
        }

        var entries = lines[(historyIndex + 1)..implementationIndex]
            .Where(static line => line.StartsWith("- ", StringComparison.Ordinal))
            .ToArray();

        if (entries.Length == 0)
        {
            return;
        }

        var recorded = RecordedStatusRegex().Match(entries[0]);

        if (!recorded.Success)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    historyIndex + 1,
                    "Decision History must begin with 'Decision recorded with status <status>.'"));
            return;
        }

        var currentStatus = recorded.Groups["status"].Value;
        if (!s_statuses.Contains(currentStatus))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    historyIndex + 1,
                    $"Decision History starts with unsupported status '{currentStatus}'."));
            return;
        }

        foreach (var entry in entries.Skip(1))
        {
            var transition = StatusTransitionRegex().Match(entry);

            if (!transition.Success)
            {
                if (entry.Contains(": Status changed", StringComparison.Ordinal))
                {
                    errors.Add(
                        new AdrValidationError(
                            relativePath,
                            historyIndex + 1,
                            "Status changes must use 'Status changed from <old> to <new>.'"));
                }

                continue;
            }

            var from = transition.Groups["from"].Value;
            var to = transition.Groups["to"].Value;
            if (from != currentStatus)
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        historyIndex + 1,
                        $"Status transition starts at '{from}', but the active history state is '{currentStatus}'."));
                continue;
            }

            if (!s_statusTransitions.Contains((from, to)))
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        historyIndex + 1,
                        $"Status transition '{from}' -> '{to}' is not allowed."));
                continue;
            }

            currentStatus = to;
        }

        if (currentStatus != metadataStatus)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    historyIndex + 1,
                    $"Decision History ends at '{currentStatus}', but metadata status is '{metadataStatus}'."));
        }
    }

    private static void ValidateBulletSection(
        string[] lines,
        int headingIndex,
        int end,
        string relativePath,
        string sectionName,
        List<AdrValidationError> errors
    )
    {
        if (lines[(headingIndex + 1)..end]
            .Any(static line => line.StartsWith("- ", StringComparison.Ordinal)))
        {
            return;
        }

        errors.Add(
            new AdrValidationError(
                relativePath,
                headingIndex + 1,
                $"{sectionName} must contain at least one bullet entry."));
    }

    private static void ValidateSources(
        string[] lines,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var sourcesIndex = Array.FindIndex(lines, static line => line == "### Sources");
        if (sourcesIndex < 0)
        {
            return;
        }

        for (var index = 0; index < sourcesIndex; index++)
        {
            if (lines[index].Contains("https://", StringComparison.Ordinal)
                || lines[index].Contains("http://", StringComparison.Ordinal))
            {
                errors.Add(
                    new AdrValidationError(relativePath, index + 1, "External URLs must be consolidated in Sources."));
            }
        }

        var sourceLines = lines[(sourcesIndex + 1)..]
            .Where(static line => line.Length > 0)
            .ToArray();

        if (sourceLines.Length == 0)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    sourcesIndex + 1,
                    "Sources must contain repository evidence or a dated primary source."));
            return;
        }

        if (sourceLines.Length > 1
            && sourceLines.Contains(RepositoryEvidenceSource, StringComparer.Ordinal))
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    sourcesIndex + 1,
                    "The repository-only evidence marker cannot be combined with external sources."));
        }

        foreach (var sourceLine in sourceLines)
        {
            if (sourceLine == RepositoryEvidenceSource)
            {
                continue;
            }

            var match = PrimarySourceRegex().Match(sourceLine);

            if (!match.Success)
            {
                errors.Add(
                    new AdrValidationError(relativePath, sourcesIndex + 1, $"Invalid source entry '{sourceLine}'."));
                continue;
            }

            if (!DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                errors.Add(
                    new AdrValidationError(
                        relativePath,
                        sourcesIndex + 1,
                        $"Source entry has an invalid retrieval date: '{sourceLine}'."));
            }
        }
    }

    private static bool TryBuildDocument(
        string relativePath,
        Dictionary<string, string> metadata,
        (string Id, string Title)? parsedTitle,
        List<AdrValidationError> errors,
        out AdrDocument? document
    )
    {
        document = null;
        if (parsedTitle is null
            || s_metadataKeys.Any(key => !metadata.ContainsKey(key)))
        {
            return false;
        }

        var fileName = Path.GetFileName(relativePath);
        var fileMatch = FileNameRegex().Match(fileName);

        if (!fileMatch.Success)
        {
            errors.Add(
                new AdrValidationError(
                    relativePath,
                    null,
                    "ADR filenames must use a lowercase dash or version-dot slug."));
            return false;
        }

        var id = Unquote(metadata["id"]);
        if (id != parsedTitle.Value.Id
            || id != fileMatch.Groups["id"].Value)
        {
            errors.Add(
                new AdrValidationError(relativePath, 2, "Metadata, filename, and title ADR identifiers must match."));
        }

        var status = Unquote(metadata["status"]);
        if (!s_statuses.Contains(status))
        {
            errors.Add(new AdrValidationError(relativePath, 3, $"Unsupported status '{status}'."));
        }

        if (!DateOnly.TryParseExact(
                Unquote(metadata["date"]),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            errors.Add(new AdrValidationError(relativePath, 4, "Date must use YYYY-MM-DD."));
        }

        ValidateNonEmptyList(metadata, "decision-makers", relativePath, errors);
        ValidateList(metadata, "consulted", relativePath, errors);
        ValidateList(metadata, "informed", relativePath, errors);

        if (Unquote(metadata["scope"]).Length == 0)
        {
            errors.Add(new AdrValidationError(relativePath, 8, "Scope must not be empty."));
        }

        if (Unquote(metadata["madr-version"]) != "4.0.0")
        {
            errors.Add(new AdrValidationError(relativePath, 13, "madr-version must be pinned to 4.0.0."));
        }

        if (Unquote(metadata["doka-profile-version"]) != "1.0")
        {
            errors.Add(new AdrValidationError(relativePath, 14, "doka-profile-version must be pinned to 1.0."));
        }

        var supersedes = ParseList(metadata["supersedes"], relativePath, errors);
        var supersededBy = ParseList(metadata["superseded-by"], relativePath, errors);
        var amends = ParseList(metadata["amends"], relativePath, errors);
        var amendedBy = ParseList(metadata["amended-by"], relativePath, errors);

        document = new AdrDocument(
            id,
            parsedTitle.Value.Title,
            status,
            date,
            relativePath,
            supersedes,
            supersededBy,
            amends,
            amendedBy);
        return true;
    }

    private static void ValidateNonEmptyList(
        Dictionary<string, string> metadata,
        string key,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        if (ParseList(metadata[key], relativePath, errors).Length == 0)
        {
            errors.Add(new AdrValidationError(relativePath, null, $"Metadata list '{key}' must not be empty."));
        }
    }

    private static void ValidateList(
        Dictionary<string, string> metadata,
        string key,
        string relativePath,
        List<AdrValidationError> errors
    ) => ParseList(metadata[key], relativePath, errors);

    private static string[] ParseList(
        string value,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        if (value.Length < 2
            || value[0] != '['
            || value[^1] != ']')
        {
            errors.Add(
                new AdrValidationError(relativePath, null, $"List value '{value}' must use inline YAML list syntax."));
            return [];
        }

        var inner = value[1..^1].Trim();
        if (inner.Length == 0)
        {
            return [];
        }

        var entries = inner.Split(',', StringSplitOptions.TrimEntries);
        if (entries.Any(static entry => entry.Length == 0))
        {
            errors.Add(new AdrValidationError(relativePath, null, $"List value '{value}' contains an empty entry."));
        }

        if (entries.Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            errors.Add(new AdrValidationError(relativePath, null, $"List value '{value}' contains duplicate entries."));
        }

        return entries;
    }

    private static string Unquote(
        string value
    )
    {
        if (value is ['"', _, ..] && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    [GeneratedRegex(@"^# (?<id>D-[0-9]{3}) -- (?<title>[^\r\n]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("^(?<id>D-[0-9]{3})-[a-z0-9]+(?:[-.][a-z0-9]+)*[.]md$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex("Chosen option: \"(?<option>[^\"]+)\", because ", RegexOptions.CultureInvariant)]
    private static partial Regex ChosenOptionRegex();

    [GeneratedRegex("^- (?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}): .+$", RegexOptions.CultureInvariant)]
    private static partial Regex HistoryEntryRegex();

    [GeneratedRegex(
        @"^- \[[^\]]+\]\(https://[^)]+\) \(primary source; retrieved (?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrimarySourceRegex();

    [GeneratedRegex(
        "^- [0-9]{4}-[0-9]{2}-[0-9]{2}: Decision recorded with status (?<status>[a-z]+)[.]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RecordedStatusRegex();

    [GeneratedRegex(
        "^- [0-9]{4}-[0-9]{2}-[0-9]{2}: Status changed from (?<from>[a-z]+) to (?<to>[a-z]+)[.]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StatusTransitionRegex();
}
