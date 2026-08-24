namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class ImagePinContract
{
    private const string ComposePath = "docker/compose.yml";
    private const string CSharpPath =
        "tests/Doka.EntityFrameworkCore.MySql.TestUtilities/TestDatabaseImages.cs";
    private const string PerformanceContractPath = "benchmarks/performance-contract.json";

    private static readonly Dictionary<string, string> s_services =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mysql84"] = "mysql84",
            ["mysql97"] = "mysql97",
            ["mariadb1011"] = "mariadb1011",
            ["mariadb114"] = "mariadb114",
            ["mariadb118"] = "mariadb118",
            ["mariadb123"] = "mariadb123",
        };

    private static readonly Dictionary<string, string> s_supportedLines =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mysql84"] = "mysql:8.4",
            ["mysql97"] = "mysql:9.7",
            ["mariadb1011"] = "mariadb:10.11",
            ["mariadb114"] = "mariadb:11.4",
            ["mariadb118"] = "mariadb:11.8",
            ["mariadb123"] = "mariadb:12.3",
        };

    private static readonly Dictionary<string, string> s_csharpConstants =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mysql84"] = "MySql84",
            ["mysql97"] = "MySql97",
            ["mariadb1011"] = "MariaDb1011",
            ["mariadb114"] = "MariaDb114",
            ["mariadb118"] = "MariaDb118",
            ["mariadb123"] = "MariaDb123",
        };

    private static readonly Dictionary<string, Dictionary<string, int>> s_mirrors =
        new(StringComparer.Ordinal)
        {
            [".github/workflows/ci.yml"] = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["mysql84"] = 2,
            },
            [PerformanceContractPath] = SixTargets(1),
            [CSharpPath] = SixTargets(1),
        };

    public static IReadOnlyList<ContractError> Validate(
        string repositoryRoot
    )
    {
        var errors = new List<ContractError>();
        var expected = ReadComposePins(repositoryRoot, errors);
        if (errors.Count > 0)
        {
            return errors;
        }

        var byLine = expected.Values.ToDictionary(ReleaseLine, StringComparer.Ordinal);
        foreach (var (relativePath, requiredCounts) in s_mirrors)
        {
            var references = ReferencesIn(repositoryRoot, relativePath, errors);
            var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var reference in references)
            {
                if (!DigestPinPattern().IsMatch(reference))
                {
                    errors.Add(new ContractError(
                        relativePath,
                        null,
                        $"References {reference} without a digest."));
                    continue;
                }

                var line = ReleaseLine(reference);
                if (!byLine.TryGetValue(line, out var expectedReference))
                {
                    errors.Add(new ContractError(
                        relativePath,
                        null,
                        $"References {reference}, which {ComposePath} does not pin."));
                    continue;
                }

                seenCounts[line] = seenCounts.GetValueOrDefault(line) + 1;
                if (reference != expectedReference)
                {
                    errors.Add(new ContractError(
                        relativePath,
                        null,
                        $"References {reference}, but {ComposePath} pins {expectedReference}."));
                }
            }

            foreach (var (target, count) in requiredCounts)
            {
                var line = ReleaseLine(expected[target]);
                var seen = seenCounts.GetValueOrDefault(line);
                if (seen != count)
                {
                    errors.Add(new ContractError(
                        relativePath,
                        null,
                        $"References {line} {seen} time(s), expected {count}."));
                }
            }
        }

        ValidateCSharpConstants(repositoryRoot, expected, errors);
        ValidatePerformanceTargets(repositoryRoot, expected, errors);
        return errors;
    }

    private static Dictionary<string, string> ReadComposePins(
        string root,
        List<ContractError> errors
    )
    {
        var path = Path.Combine(root, ComposePath);
        if (!File.Exists(path))
        {
            errors.Add(new ContractError(ComposePath, null, "File is missing."));
            return [];
        }

        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        string? service = null;
        foreach (var line in File.ReadLines(path))
        {
            var serviceMatch = ServicePattern().Match(line);
            if (serviceMatch.Success)
            {
                service = serviceMatch.Groups["service"].Value;
                continue;
            }

            var imageMatch = ImagePattern().Match(line);
            if (!imageMatch.Success || service is null || !s_services.TryGetValue(service, out var target))
            {
                continue;
            }

            pins[target] = imageMatch.Groups["image"].Value;
            service = null;
        }

        foreach (var target in s_services.Values.Except(pins.Keys, StringComparer.Ordinal))
        {
            errors.Add(new ContractError(
                ComposePath,
                null,
                $"Declares no image for '{target}'."));
        }

        foreach (var (target, pin) in pins)
        {
            if (!DigestPinPattern().IsMatch(pin))
            {
                errors.Add(new ContractError(
                    ComposePath,
                    null,
                    $"Pins '{target}' as '{pin}', which carries no digest."));
                continue;
            }

            var releaseLine = ReleaseLine(pin);
            if (releaseLine != s_supportedLines[target])
            {
                errors.Add(new ContractError(
                    ComposePath,
                    null,
                    $"Pins '{target}' on {releaseLine}, but the provider supports "
                    + $"{s_supportedLines[target]}."));
            }
        }

        return pins;
    }

    private static string[] ReferencesIn(
        string root,
        string relativePath,
        List<ContractError> errors
    )
    {
        var path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            errors.Add(new ContractError(relativePath, null, "File is missing."));
            return [];
        }

        return AnyReferencePattern()
            .Matches(File.ReadAllText(path))
            .Cast<Match>()
            .Select(static match => match.Value)
            .ToArray();
    }

    private static void ValidateCSharpConstants(
        string root,
        Dictionary<string, string> expected,
        List<ContractError> errors
    )
    {
        var path = Path.Combine(root, CSharpPath);
        if (!File.Exists(path))
        {
            return;
        }

        var text = File.ReadAllText(path);
        foreach (var (target, constant) in s_csharpConstants)
        {
            var match = Regex.Match(
                text,
                $"{Regex.Escape(constant)}\\s*=\\s*\\n?\\s*\"(?<image>[^\"]+)\"",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                errors.Add(new ContractError(
                    CSharpPath,
                    null,
                    $"Declares no {constant} constant."));
            }
            else if (match.Groups["image"].Value != expected[target])
            {
                errors.Add(new ContractError(
                    CSharpPath,
                    null,
                    $"Declares {constant} as {match.Groups["image"].Value}, but "
                    + $"{ComposePath} pins {expected[target]} for '{target}'."));
            }
        }
    }

    private static void ValidatePerformanceTargets(
        string root,
        Dictionary<string, string> expected,
        List<ContractError> errors
    )
    {
        var path = Path.Combine(root, PerformanceContractPath);
        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var targets = document.RootElement.GetProperty("requiredTargets");
        var declared = targets
            .EnumerateObject()
            .Where(static target => target.Value.TryGetProperty("serverImage", out _))
            .ToDictionary(
                static target => target.Name,
                static target => target.Value.GetProperty("serverImage").GetString() ?? string.Empty,
                StringComparer.Ordinal);

        foreach (var target in s_mirrors[PerformanceContractPath].Keys)
        {
            if (!declared.TryGetValue(target, out var image))
            {
                errors.Add(new ContractError(
                    PerformanceContractPath,
                    null,
                    $"Declares no server image for required target '{target}'."));
            }
            else if (image != expected[target])
            {
                errors.Add(new ContractError(
                    PerformanceContractPath,
                    null,
                    $"Declares '{target}' as {image}, but {ComposePath} pins "
                    + $"{expected[target]}."));
            }
        }

        foreach (var target in declared.Keys.Except(expected.Keys, StringComparer.Ordinal))
        {
            errors.Add(new ContractError(
                PerformanceContractPath,
                null,
                $"Requires target '{target}', which {ComposePath} does not declare."));
        }
    }

    private static Dictionary<string, int> SixTargets(
        int count
    ) => s_services.Values.ToDictionary(static target => target, _ => count, StringComparer.Ordinal);

    private static string ReleaseLine(
        string reference
    )
    {
        var match = ReferenceHeadPattern().Match(reference);
        if (!match.Success)
        {
            return reference;
        }

        var version = match.Groups["version"].Value.Split('.');
        return $"{match.Groups["name"].Value}:{string.Join('.', version.Take(2))}";
    }

    [GeneratedRegex(
        "(?<![0-9A-Za-z.-])(?:mysql|mariadb):[0-9][^\\s\"',;}\\]]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex AnyReferencePattern();

    [GeneratedRegex(
        "^(?:mysql|mariadb):[0-9][0-9A-Za-z.-]*@sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DigestPinPattern();

    [GeneratedRegex(
        "^(?<name>mysql|mariadb):(?<version>[0-9][0-9A-Za-z.-]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceHeadPattern();

    [GeneratedRegex("^  (?<service>[a-z0-9-]+):\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServicePattern();

    [GeneratedRegex("^\\s+image:\\s*(?<image>\\S+)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();
}
