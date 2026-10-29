namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Prevents the shipped dependency graph from restoring the vulnerable driver floor.
/// </summary>
public sealed class MySqlDriverDependencyContractTests
{
    /// <summary>Verifies ordinary consumers require a patched driver without admitting a new major.</summary>
    [Fact]
    public void Central_driver_range_excludes_affected_versions()
    {
        // Arrange
        var root = FindRepositoryRoot();
        var packages = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Directory.Packages.props"));

        // Act
        var range = packages.Descendants("DokaMySqlConnectorVersion").Single().Value.Trim();

        // Assert
        // WHY: A successful audit of a warm graph does not protect a new
        // consumer if the published dependency still permits affected versions.
        Assert.Equal("[2.6.1, 3.0.0)", range);
    }

    /// <summary>Verifies every shipped package lock selects a patched driver.</summary>
    /// <param name="project">The production project whose independent lock is checked.</param>
    [Theory]
    [InlineData("Doka.EntityFrameworkCore.MySql")]
    [InlineData("Doka.EntityFrameworkCore.MySql.NetTopologySuite")]
    [InlineData("Doka.Caching.MySql")]
    public void Shipped_package_lock_excludes_affected_driver_versions(
        string project
    )
    {
        // Arrange
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", project, "packages.lock.json")));

        // Act
        var driver = document.RootElement.GetProperty("dependencies").GetProperty("net10.0").GetProperty("MySqlConnector");
        var resolved = Version.Parse(driver.GetProperty("resolved").GetString()!);

        // Assert
        // WHY: Safe matrix overrides can request an exact or floating version.
        // The resolved lock must be patched; the default range is checked separately.
        Assert.True(resolved >= new Version(2, 6, 1), $"{project} resolves affected MySqlConnector {resolved}.");
        Assert.Equal(2, resolved.Major);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
