namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies ADR validation and the repository contracts that consume it.</summary>
public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Keeps the public migration-fragment contract and its runtime behavior
    /// visible in its release notes instead of only in the API baseline.
    /// </summary>
    [Fact]
    public void Rc11_changelog_describes_the_migration_fragment_contract()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var changelog = File.ReadAllText(Path.Combine(repositoryRoot, "CHANGELOG.md"));
        var shippedPublicApi = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "Doka.EntityFrameworkCore.MySql",
                "PublicAPI.Shipped.txt"));

        var releaseStart = changelog.IndexOf("## [10.0.0-rc.11]", StringComparison.Ordinal);
        var nextReleaseStart = changelog.IndexOf(
            "\n## [",
            releaseStart + "## [10.0.0-rc.11]".Length,
            StringComparison.Ordinal);

        var release = changelog[releaseStart..nextReleaseStart];

        // Assert
        Assert.Contains("MySqlMigrationCommandSpec.Fragments.get", shippedPublicApi, StringComparison.Ordinal);
        Assert.Contains("### Added", release, StringComparison.Ordinal);
        Assert.Contains("`MySqlMigrationCommandSpec.Fragments`", release, StringComparison.Ordinal);
        Assert.Contains("setup, body, and cleanup fragments", release, StringComparison.Ordinal);
        Assert.Contains("### Fixed", release, StringComparison.Ordinal);
        Assert.Contains("`NO_BACKSLASH_ESCAPES`", release, StringComparison.Ordinal);
        Assert.Contains("`TIME` range", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies functional tests default to MariaDB 12.3 without overriding explicit targets.
    /// </summary>
    [Fact]
    public void Functional_tests_define_a_portable_local_database_target()
    {
        // Arrange
        var projectDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests");

        // Act
        var project = File.ReadAllText(
            Path.Combine(projectDirectory, "Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj"));

        var runSettings = File.ReadAllText(Path.Combine(projectDirectory, "LocalMariaDb123.runsettings"));

        // Assert
        Assert.Contains(
            "<RunSettingsFilePath Condition=\"'$(DOKA_SPEC_TEST_TARGET)' == ''\">"
            + "$(MSBuildProjectDirectory)/LocalMariaDb123.runsettings</RunSettingsFilePath>",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "<DOKA_SPEC_TEST_TARGET>mariadb123</DOKA_SPEC_TEST_TARGET>",
            runSettings,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CONNECTION_STRING", runSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", runSettings, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies repository decision corpus passes.
    /// </summary>
    [Fact]
    public void Repository_decision_corpus_passes()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        // Act
        var report = AdrRepositoryValidator.Validate(repositoryRoot);

        // Assert
        Assert.True(report.IsValid, FormatErrors(report));
        Assert.Equal(30, report.Documents.Count);
    }
}
