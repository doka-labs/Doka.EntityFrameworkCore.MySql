namespace Doka.EntityFrameworkCore.MySql.CoverageGate;

internal static class CoverageContract
{
    public static IReadOnlyList<string> EvaluateFreshness(
        IEnumerable<string> reportPaths,
        string policyPath,
        long? nowTimestamp = null
    )
    {
        var policy = ReadPolicy(policyPath);
        var now = nowTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var errors = new List<string>();
        foreach (var reportPath in reportPaths)
        {
            var root = XDocument.Load(reportPath)
                    .Root
                ?? throw new InvalidDataException($"Coverage report '{reportPath}' has no root element.");
            errors.AddRange(
                FreshnessErrors(root, policy.EvidenceMaxAgeSeconds, now)
                    .Select(error => $"{reportPath}: {error}"));
        }

        return errors;
    }

    public static CoverageEvaluation Evaluate(
        string reportPath,
        string policyPath,
        long? nowTimestamp = null
    )
    {
        var policy = ReadPolicy(policyPath);
        var root = XDocument.Load(reportPath).Root
            ?? throw new InvalidDataException($"Coverage report '{reportPath}' has no root element.");

        var now = nowTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var freshnessErrors = FreshnessErrors(root, policy.EvidenceMaxAgeSeconds, now);
        if (freshnessErrors.Count > 0)
        {
            return new CoverageEvaluation([], freshnessErrors);
        }

        var errors = new List<string>();
        var packages = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var package in Descendants(root, "package"))
        {
            var name = Attribute(package, "name");
            if (!packages.TryAdd(name, package))
            {
                errors.Add($"Coverage report contains duplicate assembly '{name}'.");
            }
        }

        var resultLines = new List<string>();
        var declaredAssemblies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in policy.Assemblies)
        {
            if (!declaredAssemblies.Add(assembly.Name))
            {
                errors.Add($"Coverage policy repeats assembly '{assembly.Name}'.");
                continue;
            }

            if (!packages.TryGetValue(assembly.Name, out var package))
            {
                errors.Add($"Coverage report is missing shipped assembly '{assembly.Name}'.");
                continue;
            }

            var threshold = ValidateThreshold(
                $"assembly {assembly.Name}",
                Metrics(Descendants(package, "line")),
                assembly.MinimumLinePercent,
                assembly.MinimumBranchPercent);
            resultLines.Add(threshold.Result);
            errors.AddRange(threshold.Errors);
        }

        var declaredClasses = new HashSet<(string Assembly, string Name)>();
        foreach (var criticalClass in policy.CriticalClasses)
        {
            if (!declaredClasses.Add((criticalClass.Assembly, criticalClass.Name)))
            {
                errors.Add($"Coverage policy repeats critical class '{criticalClass.Name}'.");
                continue;
            }

            if (!packages.TryGetValue(criticalClass.Assembly, out var package))
            {
                continue;
            }

            var matches = Descendants(package, "class")
                .Where(element => Attribute(element, "name") == criticalClass.Name)
                .ToArray();
            if (matches.Length == 0)
            {
                errors.Add($"Coverage report is missing critical class '{criticalClass.Name}'.");
                continue;
            }

            var filenames = matches
                .Select(element => Attribute(element, "filename"))
                .ToArray();
            if (matches.Length > 1
                && (filenames.Any(string.IsNullOrEmpty)
                    || filenames
                        .Distinct(StringComparer.Ordinal)
                        .Count()
                    != filenames.Length))
            {
                errors.Add(
                    $"Coverage report contains ambiguous source fragments for critical class "
                    + $"'{criticalClass.Name}'.");
                continue;
            }

            var threshold = ValidateThreshold(
                $"critical class {criticalClass.Name}",
                Metrics(matches.SelectMany(element => Descendants(element, "line"))),
                criticalClass.MinimumLinePercent,
                criticalClass.MinimumBranchPercent);
            resultLines.Add(threshold.Result);
            errors.AddRange(threshold.Errors);
        }

        return new CoverageEvaluation(resultLines, errors);
    }

    private static CoveragePolicy ReadPolicy(
        string policyPath
    )
    {
        using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
        var root = document.RootElement;
        if (root
                .GetProperty("schemaVersion")
                .GetInt32()
            != 1)
        {
            throw new InvalidDataException("Coverage policy schemaVersion must be 1.");
        }

        var maximumAge = root
            .GetProperty("evidenceMaxAgeSeconds")
            .GetInt64();
        var assemblies = root.TryGetProperty("assemblies", out var assemblyArray)
            ? assemblyArray
                .EnumerateArray()
                .Select(ReadAssembly)
                .ToArray()
            : [];
        var criticalClasses = root.TryGetProperty("criticalClasses", out var classArray)
            ? classArray
                .EnumerateArray()
                .Select(ReadCriticalClass)
                .ToArray()
            : [];
        return new CoveragePolicy(maximumAge, assemblies, criticalClasses);
    }

    private static CoverageAssembly ReadAssembly(
        JsonElement element
    ) => new(
        element
            .GetProperty("name")
            .GetString()
        ?? throw new InvalidDataException("Coverage assembly name is null."),
        element
            .GetProperty("minimumLinePercent")
            .GetDouble(),
        element
            .GetProperty("minimumBranchPercent")
            .GetDouble());

    private static CriticalClass ReadCriticalClass(
        JsonElement element
    )
    {
        var branchFloor = element.GetProperty("minimumBranchPercent");
        return new CriticalClass(
            element
                .GetProperty("assembly")
                .GetString()
            ?? throw new InvalidDataException("Critical class assembly is null."),
            element
                .GetProperty("name")
                .GetString()
            ?? throw new InvalidDataException("Critical class name is null."),
            element
                .GetProperty("minimumLinePercent")
                .GetDouble(),
            branchFloor.ValueKind == JsonValueKind.Null ? null : branchFloor.GetDouble());
    }

    private static List<string> FreshnessErrors(
        XElement root,
        long maximumAge,
        long now
    )
    {
        var timestamp = long.Parse(Attribute(root, "timestamp", "0"), CultureInfo.InvariantCulture);
        var age = now - timestamp;
        if (timestamp <= 0)
        {
            return ["Coverage report has no positive timestamp."];
        }

        if (age < -300)
        {
            return ["Coverage report timestamp is more than five minutes in the future."];
        }

        return age > maximumAge ? [$"Coverage report is {age} seconds old; maximum age is {maximumAge} seconds."] : [];
    }

    private static CoverageMetrics Metrics(
        IEnumerable<XElement> lines
    )
    {
        var lineElements = lines.ToArray();
        var linesCovered = lineElements.Count(line => int.Parse(
                Attribute(line, "hits", "0"),
                CultureInfo.InvariantCulture)
            > 0);
        var branchesCovered = 0;
        var branchesValid = 0;
        foreach (var line in lineElements)
        {
            var conditionCoverage = Attribute(line, "condition-coverage");
            var openingParenthesis = conditionCoverage.IndexOf('(', StringComparison.Ordinal);
            var slash = conditionCoverage.IndexOf('/', StringComparison.Ordinal);
            if (openingParenthesis < 0
                || slash < 0)
            {
                continue;
            }

            var fraction = conditionCoverage[(openingParenthesis + 1)..]
                .TrimEnd(')');
            var parts = fraction.Split('/', 2, StringSplitOptions.None);
            branchesCovered += int.Parse(parts[0], CultureInfo.InvariantCulture);
            branchesValid += int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return new CoverageMetrics(linesCovered, lineElements.Length, branchesCovered, branchesValid);
    }

    private static ThresholdEvaluation ValidateThreshold(
        string label,
        CoverageMetrics metrics,
        double minimumLinePercent,
        double? minimumBranchPercent
    )
    {
        var errors = new List<string>();
        if (metrics.LinesValid == 0)
        {
            errors.Add($"{label} has no instrumented lines.");
        }
        else if (metrics.LinePercent < minimumLinePercent)
        {
            errors.Add($"{label} line coverage {metrics.LinePercent:F2}% is below " + $"{minimumLinePercent:F2}%.");
        }

        string branchFloor;
        if (minimumBranchPercent is null)
        {
            branchFloor = "N/A";
            if (metrics.BranchesValid > 0)
            {
                errors.Add($"{label} has instrumented branches but declares no branch floor.");
            }
        }
        else
        {
            branchFloor = $"{minimumBranchPercent:F2}%";
            if (minimumBranchPercent <= 0)
            {
                errors.Add($"{label} branch floor must be greater than zero or null for " + "a branch-free surface.");
            }
            else if (metrics.BranchesValid == 0)
            {
                errors.Add($"{label} has no instrumented branches.");
            }
            else if (metrics.BranchPercent < minimumBranchPercent)
            {
                errors.Add(
                    $"{label} branch coverage {metrics.BranchPercent:F2}% is below " + $"{minimumBranchPercent:F2}%.");
            }
        }

        var result = $"{label}: lines {metrics.LinesCovered}/{metrics.LinesValid} "
            + $"({metrics.LinePercent:F2}%, minimum {minimumLinePercent:F2}%); "
            + $"branches {metrics.BranchesCovered}/{metrics.BranchesValid} "
            + $"({metrics.BranchPercent:F2}%, minimum {branchFloor})";
        return new ThresholdEvaluation(result, errors);
    }

    private static IEnumerable<XElement> Descendants(
        XContainer element,
        string localName
    ) => element
        .Descendants()
        .Where(candidate => candidate.Name.LocalName == localName);

    private static string Attribute(
        XElement element,
        string name,
        string defaultValue = ""
    ) => element.Attribute(name)
            ?.Value
        ?? defaultValue;

    private sealed record CoveragePolicy(
        long EvidenceMaxAgeSeconds,
        IReadOnlyList<CoverageAssembly> Assemblies,
        IReadOnlyList<CriticalClass> CriticalClasses
    );

    private sealed record CoverageAssembly(
        string Name,
        double MinimumLinePercent,
        double MinimumBranchPercent
    );

    private sealed record CriticalClass(
        string Assembly,
        string Name,
        double MinimumLinePercent,
        double? MinimumBranchPercent
    );

    private sealed record CoverageMetrics(
        int LinesCovered,
        int LinesValid,
        int BranchesCovered,
        int BranchesValid
    )
    {
        public double LinePercent => LinesValid == 0 ? 0 : 100d * LinesCovered / LinesValid;

        public double BranchPercent => BranchesValid == 0 ? 0 : 100d * BranchesCovered / BranchesValid;
    }

    private sealed record ThresholdEvaluation(
        string Result,
        IReadOnlyList<string> Errors
    );
}

internal sealed record CoverageEvaluation(
    IReadOnlyList<string> Results,
    IReadOnlyList<string> Errors
);
