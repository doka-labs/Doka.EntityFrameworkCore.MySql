namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the binding between active-LTS target profiles, supported lines, and
/// digest-pinned database image tags.
/// </summary>
public sealed class IntegrationDatabaseTargetContractTests
{
    /// <summary>Returns every supported profile independently of the selected live-engine subset.</summary>
    public static IEnumerable<object[]> SupportedTargets => IntegrationTestEnvironment
        .GetSupportedTargets()
        .Select(target => new object[] { target.ToString() });

    /// <summary>Verifies stable schema identity and separation across owners and runs.</summary>
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

    /// <summary>Verifies schema names reject an incomplete run or owner identity.</summary>
    /// <param name="runId">The supplied run identity.</param>
    /// <param name="ownerFilePath">The supplied test-class owner identity.</param>
    [Theory]
    [InlineData("", "/tests/FirstIntegrationTests.cs")]
    [InlineData("0123456789ab", "")]
    public void Class_database_name_rejects_missing_identity(
        string runId,
        string ownerFilePath
    )
    {
        // Arrange
        Action createName = () => IntegrationTestEnvironment.CreateScopedDatabaseName(runId, ownerFilePath);

        // Act
        var exception = Record.Exception(createName);

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    /// Guards capability selection against drifting away from a target's
    /// independently declared supported line.
    /// </summary>
    /// <param name="targetName">The independently reported supported engine profile.</param>
    [Theory]
    [MemberData(nameof(SupportedTargets))]
    public void Active_lts_server_profiles_match_supported_lines(
        string targetName
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var request = IntegrationTestEnvironment.CreateRequest(target);
        var separator = request.ServerVersionToken.IndexOf(':');

        // WHY: The declared request token supplies the independent expected line;
        // validate its shape before parsing that fixture value.
        Assert.True(
            separator > 0,
            $"Target '{target}' has an invalid server-version token: {request.ServerVersionToken}");

        var supportedLine = Version.Parse(request.ServerVersionToken[(separator + 1)..]);

        // Act
        var profileVersion = IntegrationTestEnvironment.GetServerVersion(target).Version;

        // Assert
        Assert.Equal(supportedLine.Major, profileVersion.Major);
        Assert.Equal(supportedLine.Minor, profileVersion.Minor);
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
    )
    {
        // Arrange
        var expected = new Version(major, minor, build);

        // Act
        var actual = IntegrationTestEnvironment.ParsePinnedImageVersion(image);

        // Assert
        Assert.Equal(expected, actual);
    }

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
    )
    {
        // Arrange
        Action parse = () => IntegrationTestEnvironment.ParsePinnedImageVersion(image);

        // Act
        var exception = Record.Exception(parse);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
