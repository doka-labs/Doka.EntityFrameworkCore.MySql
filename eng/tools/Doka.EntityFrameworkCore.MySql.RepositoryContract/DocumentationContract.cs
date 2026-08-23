namespace Doka.EntityFrameworkCore.MySql.RepositoryContract;

internal static partial class DocumentationContract
{
    private static readonly IReadOnlyDictionary<string, string[]> s_canonicalSections =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GOVERNANCE.md"] =
            [
                "Project Stewardship",
                "Roles and Responsibilities",
                "Continuity and Succession",
                "Primary Sources",
            ],
            ["ROADMAP.md"] =
            [
                "Direction Through July 2027",
                "Explicit Non-Goals Through July 2027",
                "Review and Change Process",
                "Primary Source",
            ],
            ["docs/architecture.md"] =
            [
                "System Context",
                "Runtime Composition",
                "Architectural Invariants",
                "Primary Sources",
            ],
            ["docs/README.md"] =
            [
                "Use the Provider",
                "Operate the Provider",
                "Maintain the Provider",
                "Document Ownership",
                "Documentation Contract",
            ],
            ["docs/complex-types.md"] = ["Support Matrix", "Verification", "Primary Sources"],
            ["docs/ctes.md"] =
            [
                "Support Matrix",
                "Runnable Verification",
                "Related Limitations",
                "Primary Sources",
            ],
            ["docs/host-integration-examples.md"] = ["Local Validation", "Primary Sources"],
            ["docs/ide-integration.md"] = ["Repository Verification", "Primary Sources"],
            ["docs/migration-operation-handlers.md"] =
            [
                "Contract at a Glance",
                "Package Author Verification",
                "Primary Sources",
            ],
            ["docs/openssf-best-practices.md"] =
            [
                "Official Project State",
                "Silver Documentation Evidence",
                "Gold Preparation",
                "Update Procedure",
                "Primary Sources",
            ],
            ["docs/operations/paired-performance-methodology.md"] =
            [
                "What a Paired Run Measures",
                "Registered Sensitivity",
                "What the Contract Controls",
                "Primary Sources",
            ],
            ["docs/operations/performance-baseline-operations.md"] =
            [
                "Accept an Engine Image Update",
                "Seed an Accepted Baseline",
                "Hosted Runner Baseline",
                "Primary Sources",
            ],
            ["docs/operations/performance-evidence-reference.md"] =
            [
                "Profiles",
                "Evidence Layout",
                "Measurement Quality and Termination",
                "Soak Interpretation",
                "Primary Sources",
            ],
            ["docs/operations/performance-evidence.md"] =
            [
                "Choose the Right Document",
                "Run One Target",
                "Failure Triage",
            ],
            ["docs/operations/resilience-and-topology.md"] =
            [
                "Connection Pooler / Load Balancer Compatibility",
                "Primary Sources",
            ],
            ["docs/security/assurance-case.md"] =
            [
                "Scope and Method",
                "Residual Risk and Ownership",
                "Review and Re-evaluation",
                "Primary Source",
            ],
            ["docs/security/release-verification.md"] =
            [
                "Verify the Source Tag",
                "Verify SLSA Provenance",
                "Verify NuGet Repository Signatures",
                "Primary Sources",
            ],
            ["docs/provider-configuration.md"] =
            [
                "Connection and Server Configuration",
                "Context Options",
                "Model Configuration",
                "Runnable Verification",
                "Primary Sources",
            ],
            ["docs/query-functions.md"] =
            [
                "Function Matrix",
                "Runnable Verification",
                "Primary Sources",
            ],
            ["docs/supported-databases.md"] =
            [
                "Active LTS Matrix",
                "Qualification Contract",
                "Primary Sources",
            ],
            ["docs/temporal-tables.md"] =
            [
                "Support Matrix",
                "Runnable Verification",
                "Related Limitations",
                "Primary Sources",
            ],
        };

    private static readonly HashSet<string> s_queryTypes = new(
        ["MySqlDbFunctionsExtensions", "MySqlNetTopologySuiteDbFunctionsExtensions"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> s_configurationTypes = new(
        [
            "MySqlDbContextOptionsBuilderExtensions",
            "MySqlEntityTypeBuilderExtensions",
            "MySqlIndexBuilderExtensions",
            "MySqlModelBuilderExtensions",
            "MySqlNetTopologySuiteDbContextOptionsBuilderExtensions",
            "MySqlNetTopologySuiteIndexBuilderExtensions",
            "MySqlNetTopologySuitePropertyBuilderExtensions",
            "MySqlNetTopologySuiteServiceCollectionExtensions",
            "MySqlPropertyBuilderExtensions",
            "MySqlServiceCollectionExtensions",
            "MySqlDbContextOptionsBuilder",
            "MySqlReverseEngineeringOptionsBuilder",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> s_navigationSupportFiles = new(
        ["docs/decisions/MADR-PROFILE.md", "docs/decisions/adr-template.md"],
        StringComparer.Ordinal);

    public static DocumentationValidationResult Validate(
        string repositoryRoot
    )
    {
        var localLinks = ValidateLocalLinks(repositoryRoot);
        var errors = new List<ContractError>(localLinks.Errors);
        errors.AddRange(ValidatePackageReadme(repositoryRoot));
        errors.AddRange(ValidateCanonicalSections(repositoryRoot));
        errors.AddRange(ValidatePublicApiDocumentation(repositoryRoot));
        errors.AddRange(ValidateNavigation(repositoryRoot));
        return new DocumentationValidationResult(
            localLinks.DocumentCount,
            localLinks.LinkCount,
            errors);
    }

    internal static DocumentationValidationResult ValidateLocalLinks(
        string repositoryRoot
    )
    {
        var documents = DiscoverDocuments(repositoryRoot);
        var errors = new List<ContractError>();
        var anchorCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var linkCount = 0;
        foreach (var document in documents)
        {
            foreach (var (line, target) in DocumentLinks(document))
            {
                if (IsExternal(target))
                {
                    continue;
                }

                linkCount++;
                var error = ValidateTarget(repositoryRoot, document, line, target, anchorCache);
                if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        return new DocumentationValidationResult(documents.Length, linkCount, errors);
    }

    private static string[] DiscoverDocuments(
        string root
    )
    {
        var documents = Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly).ToList();
        foreach (var directory in new[] { ".github", "docs", "examples", "tests" })
        {
            var path = Path.Combine(root, directory);
            if (Directory.Exists(path))
            {
                documents.AddRange(Directory.GetFiles(path, "*.md", SearchOption.AllDirectories));
            }
        }

        return documents.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<ContractError> ValidatePackageReadme(
        string root
    )
    {
        var readme = Path.Combine(root, "README.md");
        foreach (var (line, target) in DocumentLinks(readme))
        {
            if (IsExternal(target))
            {
                continue;
            }

            var (path, fragment) = SplitTarget(target);
            if (path.Length > 0 || fragment.Length == 0)
            {
                yield return Error(
                    root,
                    readme,
                    line,
                    "Packaged README links must be absolute or in-document anchors",
                    target);
            }
        }
    }

    private static IEnumerable<ContractError> ValidateCanonicalSections(
        string root
    )
    {
        foreach (var (relativePath, requiredSections) in s_canonicalSections)
        {
            var path = Path.Combine(root, relativePath);
            if (!File.Exists(path))
            {
                yield return new ContractError(relativePath, 1, "Canonical guide does not exist.");
                continue;
            }

            var headings = AuthoredLines(path)
                .Select(static entry => HeadingPattern().Match(entry.Text))
                .Where(static match => match.Success)
                .Select(static match => match.Groups["title"].Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var section in requiredSections.Where(section => !headings.Contains(section)))
            {
                yield return new ContractError(
                    relativePath,
                    1,
                    $"Canonical guide is missing required section '{section}'.");
            }
        }
    }

    private static IEnumerable<ContractError> ValidatePublicApiDocumentation(
        string root
    )
    {
        var queryMethods = new HashSet<string>(StringComparer.Ordinal);
        var configurationMethods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in new[]
                 {
                     "src/Doka.EntityFrameworkCore.MySql/PublicAPI.Unshipped.txt",
                     "src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/PublicAPI.Unshipped.txt",
                 })
        {
            foreach (var line in File.ReadLines(Path.Combine(root, relativePath)))
            {
                var match = PublicMethodPattern().Match(line);
                if (!match.Success || match.Groups["type"].Value == match.Groups["method"].Value)
                {
                    continue;
                }

                var type = match.Groups["type"].Value;
                var method = match.Groups["method"].Value;
                if (s_queryTypes.Contains(type))
                {
                    queryMethods.Add(method);
                }

                if (s_configurationTypes.Contains(type)
                    || (type == "MySqlServerVersion"
                        && line.StartsWith("static ", StringComparison.Ordinal)))
                {
                    configurationMethods.Add(method);
                }
            }
        }

        return MissingApiMethods(root, "docs/query-functions.md", queryMethods)
            .Concat(MissingApiMethods(root, "docs/provider-configuration.md", configurationMethods));
    }

    private static IEnumerable<ContractError> MissingApiMethods(
        string root,
        string relativePath,
        IEnumerable<string> methods
    )
    {
        var content = File.Exists(Path.Combine(root, relativePath))
            ? File.ReadAllText(Path.Combine(root, relativePath))
            : string.Empty;
        var documented = DocumentedMethodPattern()
            .Matches(content)
            .Cast<Match>()
            .Select(static match => match.Groups["method"].Value)
            .ToHashSet(StringComparer.Ordinal);

        return methods
            .Where(method => !documented.Contains(method))
            .Order(StringComparer.Ordinal)
            .Select(method => new ContractError(
                relativePath,
                1,
                $"Public API method '{method}' is missing from its canonical guide."));
    }

    private static IEnumerable<ContractError> ValidateNavigation(
        string root
    )
    {
        var documentationRoot = Path.GetFullPath(Path.Combine(root, "docs"));
        var pending = new Stack<string>([Path.Combine(documentationRoot, "README.md")]);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var document))
        {
            document = Path.GetFullPath(document);
            if (!File.Exists(document) || !reachable.Add(document))
            {
                continue;
            }

            foreach (var (_, target) in DocumentLinks(document))
            {
                if (IsExternal(target))
                {
                    continue;
                }

                var (relativePath, _) = SplitTarget(target);
                if (relativePath.Length == 0)
                {
                    continue;
                }

                var destination = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(document)!, Uri.UnescapeDataString(relativePath)));
                if (!IsWithin(documentationRoot, destination))
                {
                    continue;
                }

                if (Directory.Exists(destination))
                {
                    destination = Path.Combine(destination, "README.md");
                }

                if (Path.GetExtension(destination).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    pending.Push(destination);
                }
            }
        }

        foreach (var document in Directory.GetFiles(documentationRoot, "*.md", SearchOption.AllDirectories))
        {
            var relativePath = Relative(root, document);
            if (!reachable.Contains(Path.GetFullPath(document))
                && !s_navigationSupportFiles.Contains(relativePath))
            {
                yield return new ContractError(
                    relativePath,
                    1,
                    "Public document is not reachable from docs/README.md.");
            }
        }
    }

    private static ContractError? ValidateTarget(
        string root,
        string source,
        int line,
        string target,
        Dictionary<string, HashSet<string>> anchorCache
    )
    {
        var (relativePath, fragment) = SplitTarget(target);
        var destination = relativePath.Length == 0
            ? source
            : relativePath[0] == '/'
                ? Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(relativePath.TrimStart('/'))))
                : Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(source)!,
                    Uri.UnescapeDataString(relativePath)));
        if (!IsWithin(root, destination))
        {
            return Error(root, source, line, "Target escapes the repository root", target);
        }

        if (Directory.Exists(destination))
        {
            destination = Path.Combine(destination, "README.md");
        }

        if (!File.Exists(destination))
        {
            return Error(root, source, line, "Target file does not exist", target);
        }

        if (fragment.Length == 0)
        {
            return null;
        }

        if (!Path.GetExtension(destination).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                root,
                source,
                line,
                "Fragments can only be verified for Markdown targets",
                target);
        }

        if (!anchorCache.TryGetValue(destination, out var anchors))
        {
            anchors = DocumentAnchors(destination);
            anchorCache[destination] = anchors;
        }

        return anchors.Contains(Uri.UnescapeDataString(fragment))
            ? null
            : Error(root, source, line, $"Anchor '#{fragment}' does not exist", target);
    }

    private static IEnumerable<(int Line, string Target)> DocumentLinks(
        string document
    )
    {
        foreach (var (line, text) in AuthoredLines(document))
        {
            foreach (Match match in InlineLinkPattern().Matches(text))
            {
                var linkTarget = Destination(match.Groups["target"].Value);
                if (linkTarget is not null)
                {
                    yield return (line, linkTarget);
                }
            }

            var definition = ReferenceDefinitionPattern().Match(text);
            if (definition.Success
                && Destination(definition.Groups["target"].Value) is { } referenceTarget)
            {
                yield return (line, referenceTarget);
            }
        }
    }

    private static IEnumerable<(int Line, string Text)> AuthoredLines(
        string document
    )
    {
        char? fenceCharacter = null;
        var fenceLength = 0;
        var lines = File.ReadAllLines(document);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var fence = FencePattern().Match(line);
            if (fence.Success)
            {
                var marker = fence.Groups["marker"].Value;
                if (fenceCharacter is null)
                {
                    fenceCharacter = marker[0];
                    fenceLength = marker.Length;
                }
                else if (marker[0] == fenceCharacter && marker.Length >= fenceLength)
                {
                    fenceCharacter = null;
                    fenceLength = 0;
                }

                continue;
            }

            if (fenceCharacter is null)
            {
                yield return (index + 1, line);
            }
        }
    }

    private static HashSet<string> DocumentAnchors(
        string document
    )
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var headingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, line) in AuthoredLines(document))
        {
            foreach (Match anchor in ExplicitAnchorPattern().Matches(line))
            {
                anchors.Add(anchor.Groups["anchor"].Value);
            }

            var heading = HeadingPattern().Match(line);
            if (!heading.Success)
            {
                continue;
            }

            var baseAnchor = GitHubHeadingAnchor(heading.Groups["title"].Value);
            var duplicateIndex = headingCounts.GetValueOrDefault(baseAnchor);
            headingCounts[baseAnchor] = duplicateIndex + 1;
            anchors.Add(duplicateIndex == 0 ? baseAnchor : $"{baseAnchor}-{duplicateIndex}");
        }

        return anchors;
    }

    private static string GitHubHeadingAnchor(
        string title
    )
    {
        var decoded = WebUtility.HtmlDecode(HtmlTagPattern().Replace(title, string.Empty));
        var normalized = InlineMarkupPattern().Replace(decoded, string.Empty).Trim().ToLowerInvariant();
        var characters = normalized.Where(static character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_');
        return WhitespacePattern().Replace(string.Concat(characters), "-");
    }

    private static string? Destination(
        string rawTarget
    )
    {
        var target = rawTarget.Trim();
        if (target.Length == 0)
        {
            return null;
        }

        if (target.StartsWith('<'))
        {
            var end = target.IndexOf('>');
            return end < 0 ? target : target[1..end];
        }

        var whitespace = target.IndexOfAny([' ', '\t']);
        return whitespace < 0 ? target : target[..whitespace];
    }

    private static bool IsExternal(
        string target
    ) => target.StartsWith("//", StringComparison.Ordinal) || SchemePattern().IsMatch(target);

    private static (string Path, string Fragment) SplitTarget(
        string target
    )
    {
        var hash = target.IndexOf('#');
        var withoutFragment = hash < 0 ? target : target[..hash];
        var query = withoutFragment.IndexOf('?');
        return (
            query < 0 ? withoutFragment : withoutFragment[..query],
            hash < 0 ? string.Empty : target[(hash + 1)..]);
    }

    private static bool IsWithin(
        string root,
        string candidate
    )
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static ContractError Error(
        string root,
        string source,
        int line,
        string reason,
        string target
    ) => new(Relative(root, source), line, $"{reason}: {target}.");

    private static string Relative(
        string root,
        string path
    ) => Path.GetRelativePath(root, path).Replace('\\', '/');

    [GeneratedRegex("^\\s*(?<marker>`{3,}|~{3,})", RegexOptions.CultureInvariant)]
    private static partial Regex FencePattern();

    [GeneratedRegex("^\\s{0,3}#{1,6}\\s+(?<title>.+?)\\s*#*\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(
        "<a\\s+[^>]*?(?:id|name)=[\"'](?<anchor>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitAnchorPattern();

    [GeneratedRegex("!?\\[[^\\]]*\\]\\((?<target>[^)\\n]+)\\)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineLinkPattern();

    [GeneratedRegex("^\\s{0,3}\\[[^\\]]+\\]:\\s*(?<target>\\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceDefinitionPattern();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex("[`*_~]", RegexOptions.CultureInvariant)]
    private static partial Regex InlineMarkupPattern();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

    [GeneratedRegex(
        "^(?:override |static )?Doka\\.EntityFrameworkCore\\.MySql\\."
        + "(?<type>MySql[A-Za-z0-9]+)\\.(?<method>[A-Za-z][A-Za-z0-9]*)"
        + "(?:<[^>]+>)?\\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex PublicMethodPattern();

    [GeneratedRegex(
        "`(?:[A-Za-z][A-Za-z0-9]*\\.)?(?<method>[A-Za-z][A-Za-z0-9]*)"
        + "(?:<[^>]+>)?\\s*\\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex DocumentedMethodPattern();
}

internal sealed record DocumentationValidationResult(
    int DocumentCount,
    int LinkCount,
    IReadOnlyList<ContractError> Errors
);
