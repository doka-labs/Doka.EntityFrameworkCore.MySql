namespace Doka.EntityFrameworkCore.MySql.AdrValidator;

internal static partial class AdrParser
{
    /// <summary>Checks the fixed notification audience without claiming that notification occurred.</summary>
    private static void ValidateInformed(
        string value,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var audience = ParseList(value, relativePath, errors);

        if (audience.Length != 1
            || Unquote(audience[0]) != "@doka-labs/core-maintainers")
        {
            errors.Add(new AdrValidationError(
                relativePath,
                7,
                "informed must contain exactly '@doka-labs/core-maintainers'."));
        }
    }

    /// <summary>Keeps the current update date bound to an ordered, unchanged recording history.</summary>
    private static void ValidateHistoryMetadata(
        string[] lines,
        DateOnly metadataDate,
        string relativePath,
        List<AdrValidationError> errors
    )
    {
        var historyIndex = Array.FindIndex(lines, static line => line == "### Decision History");
        var implementationIndex = Array.FindIndex(lines, static line => line == "### Implementation References");

        if (historyIndex < 0
            || implementationIndex <= historyIndex)
        {
            return;
        }

        DateOnly? previousDate = null;

        // WHY: Only the actual history section binds metadata; dates in source references
        // or examples must not silently become the current decision date.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = historyIndex + 1; index < implementationIndex; index++)
        {
            var match = HistoryEntryRegex()
                .Match(lines[index]);

            if (!match.Success
                || !DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var historyDate))
            {
                // WHY: The existing history validator reports malformed entries and dates.
                continue;
            }

            if (previousDate is { } earlierDate
                && historyDate < earlierDate)
            {
                errors.Add(new AdrValidationError(
                    relativePath,
                    index + 1,
                    "Decision History dates must be chronological."));
            }

            previousDate = historyDate;
        }

        if (previousDate is { } lastDate
            && metadataDate != lastDate)
        {
            errors.Add(new AdrValidationError(
                relativePath,
                4,
                "Metadata date must match the last Decision History entry."));
        }
    }
}
