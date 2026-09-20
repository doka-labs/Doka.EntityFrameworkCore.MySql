namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the binding between active-LTS target profiles, supported lines, and
/// digest-pinned database image tags.
/// </summary>
public sealed class IntegrationDatabaseTargetContractTests
{
    [Fact]
    public void Class_database_names_are_stable_for_one_owner_and_distinct_for_another()
    {
        // Arrange
        const string runId = "0123456789abcdef0123456789abcdef";
        const string firstOwner = "/tests/FirstIntegrationTests.cs";
        const string secondOwner = "/tests/SecondIntegrationTests.cs";

        // Act
        var firstName = IntegrationTestEnvironment.CreateScopedDatabaseName(runId, firstOwner);
        var repeatedName = IntegrationTestEnvironment.CreateScopedDatabaseName(runId, firstOwner);
        var secondName = IntegrationTestEnvironment.CreateScopedDatabaseName(runId, secondOwner);
        var nextRunName = IntegrationTestEnvironment.CreateScopedDatabaseName(
            "fedcba9876543210fedcba9876543210",
            firstOwner);

        // Assert
        Assert.Equal(firstName, repeatedName);
        Assert.NotEqual(firstName, secondName);
        Assert.NotEqual(firstName, nextRunName);
        Assert.StartsWith($"doka_it_{runId}_", firstName, StringComparison.Ordinal);
        Assert.True(firstName.Length <= 64);
    }

    [Theory]
    [InlineData("", "/tests/FirstIntegrationTests.cs")]
    [InlineData("0123456789ab", "")]
    public void Class_database_name_rejects_missing_identity(
        string runId,
        string ownerFilePath
    )
    {
        // Act
        void CreateDatabaseName() => IntegrationTestEnvironment.CreateScopedDatabaseName(runId, ownerFilePath);

        // Assert
        Assert.Throws<ArgumentException>(CreateDatabaseName);
    }

    /// <summary>
    /// Guards capability selection against drifting away from a target's
    /// independently declared supported line.
    /// </summary>
    [Fact]
    public void Active_lts_server_profiles_match_supported_lines()
    {
        foreach (var target in IntegrationTestEnvironment.GetSupportedTargets())
        {
            var request = IntegrationTestEnvironment.CreateRequest(target);
            var separator = request.ServerVersionToken.IndexOf(':');

            Assert.True(
                separator > 0,
                $"Target '{target}' has an invalid server-version token: {request.ServerVersionToken}");

            var supportedLine = Version.Parse(request.ServerVersionToken[(separator + 1)..]);
            var profileVersion = IntegrationTestEnvironment.GetServerVersion(target)
                .Version;

            Assert.Equal(supportedLine.Major, profileVersion.Major);
            Assert.Equal(supportedLine.Minor, profileVersion.Minor);
        }
    }

    /// <summary>
    /// Verifies that registry ports and digest algorithm separators cannot hide
    /// or truncate the image tag's patch component.
    /// </summary>
    [Theory]
    [InlineData("mysql:8.4.11@sha256:abcdef", 8, 4, 11)]
    [InlineData("registry.example:5000/mariadb:12.3.2@sha256:abcdef", 12, 3, 2)]
    public void Pinned_image_version_parser_preserves_the_complete_numeric_tag(
        string image,
        int major,
        int minor,
        int build
    ) => Assert.Equal(new Version(major, minor, build), IntegrationTestEnvironment.ParsePinnedImageVersion(image));

    /// <summary>
    /// Verifies that unpinned and floating image references cannot enter a
    /// target-specific provider profile.
    /// </summary>
    [Theory]
    [InlineData("mysql:8.4.11")]
    [InlineData("mysql:8.4@sha256:abcdef")]
    [InlineData("mysql:latest@sha256:abcdef")]
    public void Pinned_image_version_parser_rejects_invalid_references(
        string image
    ) => Assert.Throws<InvalidOperationException>(() => IntegrationTestEnvironment.ParsePinnedImageVersion(image));
}
