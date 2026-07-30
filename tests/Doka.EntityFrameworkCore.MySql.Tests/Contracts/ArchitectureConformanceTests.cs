namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Enforces the provider architecture boundaries that analyzers cannot express.
/// </summary>
public sealed class ArchitectureConformanceTests
{
    /// <summary>
    /// Ensures the reserved SQL sentinel namespace has one owner.
    /// </summary>
    [Fact]
    public void Raw_query_sentinels_exist_only_in_the_exhaustive_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(
            repositoryRoot,
            "src",
            "Doka.EntityFrameworkCore.MySql",
            "Internal",
            "Query",
            "Expressions",
            "MySqlSentinelContract.cs");
        var violations = EnumerateProductSources(repositoryRoot)
            .Where(path => !string.Equals(path, contractPath, StringComparison.Ordinal))
            .Where(path => File
                .ReadAllText(path)
                .Contains("__mysql_", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Ensures production behavior branches on named capability contracts instead
    /// of engine-family identity.
    /// </summary>
    [Fact]
    public void Runtime_behavior_does_not_branch_on_engine_family_identity()
    {
        var repositoryRoot = FindRepositoryRoot();
        var behaviorRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql", "Internal", "Infrastructure"),
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql", "Internal", "Migrations"),
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql", "Internal", "Query"),
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql", "Internal", "Scaffolding"),
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql", "Internal", "Update"),
            Path.Combine(repositoryRoot, "src", "Doka.EntityFrameworkCore.MySql.NetTopologySuite", "Internal"),
        };
        var forbiddenFragments = new[]
        {
            ".IsMariaDb",
            "EngineFamily.",
        };

        var violations = behaviorRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path => File
                .ReadLines(path)
                .Select((
                    line,
                    index
                ) => (Path: path, Line: line, Number: index + 1)))
            .Where(candidate => forbiddenFragments.Any(fragment =>
                candidate.Line.Contains(fragment, StringComparison.Ordinal)))
            .Select(candidate => $"{Path.GetRelativePath(repositoryRoot, candidate.Path)}:{candidate.Number}")
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Ensures every engine fact has a production consumer or provider-support
    /// mapping beyond profile construction.
    /// </summary>
    [Fact]
    public void Every_engine_capability_has_an_active_consumer_or_provider_mapping()
    {
        var repositoryRoot = FindRepositoryRoot();
        var excludedFiles = new[]
        {
            "EngineCapability.cs",
            "EngineProfileTable.cs",
        };
        var sources = EnumerateProductSources(repositoryRoot)
            .Where(path => !excludedFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => (Path: path, Content: File.ReadAllText(path)))
            .ToArray();

        var violations = Enum
            .GetValues<EngineCapability>()
            .Where(capability => !sources.Any(source => source.Content.Contains(
                $"EngineCapability.{capability}",
                StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Ensures every provider capability controls a production behavior beyond
    /// its declaration, mapping, and diagnostic projection.
    /// </summary>
    [Fact]
    public void Every_provider_capability_has_an_active_behavior_consumer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var excludedFiles = new[]
        {
            "MySqlLoggerMessages.cs",
            "ProviderCapability.cs",
            "ProviderProfile.cs",
        };
        var sources = EnumerateProductSources(repositoryRoot)
            .Where(path => !excludedFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => (Path: path, Content: File.ReadAllText(path)))
            .ToArray();

        var violations = Enum
            .GetValues<ProviderCapability>()
            .Where(capability => !sources.Any(source => source.Content.Contains(
                $"ProviderCapability.{capability}",
                StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Ensures provider support cannot silently acquire a provider-limitation
    /// state.
    /// </summary>
    [Fact]
    public void Provider_support_statuses_never_blame_the_provider()
    {
        Assert.Equal(
            [
                ProviderSupportStatus.Native,
                ProviderSupportStatus.Emulated,
                ProviderSupportStatus.UnsupportedByEngine,
            ],
            Enum.GetValues<ProviderSupportStatus>());
    }

    private static IEnumerable<string> EnumerateProductSources(
        string repositoryRoot
    ) => Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories);

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
}
