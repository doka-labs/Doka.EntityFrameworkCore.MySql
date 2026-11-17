using System.Xml.Linq;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects the native collector's explicit shipping-code measurement boundary.</summary>
public sealed class NativeCoverageConfigurationTests
{
    /// <summary>Measures every shipped assembly without selecting framework or test binaries.</summary>
    [Fact]
    public void Native_coverage_includes_exactly_the_shipping_assemblies()
    {
        // Arrange
        var root = FindRepositoryRoot();
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng/coverage-policy.json")));
        using var configuration = ReadConfiguration(root);
        var expected = policy
            .RootElement
            .GetProperty("assemblies")
            .EnumerateArray()
            .Select(assembly => $"[{assembly.GetProperty("name").GetString()}]*")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Act
        var actual = (Settings(configuration)
                    .GetProperty("include")
                    .GetString()
                ?? throw new InvalidDataException("Native coverage includes are null."))
            .Split(',')
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Retains the previous CLI attribute exclusions and complete source instrumentation.</summary>
    [Fact]
    public void Native_coverage_preserves_the_existing_source_exclusions()
    {
        // Arrange
        using var configuration = ReadConfiguration(FindRepositoryRoot());

        // Act
        var settings = Settings(configuration);

        // Assert
        Assert.Equal(
            "ExcludeFromCodeCoverage,ExcludeFromCodeCoverageAttribute,GeneratedCodeAttribute,CompilerGeneratedAttribute",
            settings
                .GetProperty("excludeByAttribute")
                .GetString());

        Assert.False(settings
            .GetProperty("skipAutoProps")
            .GetBoolean());

        Assert.False(settings
            .GetProperty("singleHit")
            .GetBoolean());

        Assert.False(settings
            .GetProperty("includeTestAssembly")
            .GetBoolean());

        Assert.False(settings.TryGetProperty("excludeByFile", out _));
    }

    /// <summary>Supplies the same native configuration to CLI and IDE-built test modules.</summary>
    /// <param name="project">The test module that must receive the shared configuration.</param>
    [Theory]
    [InlineData("Doka.EntityFrameworkCore.MySql.Tests")]
    [InlineData("Doka.EntityFrameworkCore.MySql.FunctionalTests")]
    [InlineData("Doka.EntityFrameworkCore.MySql.IntegrationTests")]
    public void Every_native_test_project_copies_the_shared_configuration(
        string project
    )
    {
        // Arrange
        var path = Path.Combine(FindRepositoryRoot(), "tests", project, project + ".csproj");
        var document = XDocument.Load(path);

        // Act
        var item = document
            .Descendants("None")
            .SingleOrDefault(element =>
                (string?)element.Attribute("Include") == "../testconfig.json");

        // Assert
        Assert.NotNull(item);
        Assert.Equal("testconfig.json", (string?)item.Attribute("Link"));
        Assert.Equal("PreserveNewest", (string?)item.Attribute("CopyToOutputDirectory"));
    }

    private static JsonDocument ReadConfiguration(
        string root
    ) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/testconfig.json")));

    private static JsonElement Settings(
        JsonDocument configuration
    ) => configuration
        .RootElement
        .GetProperty("platformOptions")
        .GetProperty("Coverlet");

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

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
