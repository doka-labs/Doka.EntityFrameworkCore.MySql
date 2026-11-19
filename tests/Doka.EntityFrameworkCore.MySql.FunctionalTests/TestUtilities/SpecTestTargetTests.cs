namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies local and explicit test-target selection independently of the runner.</summary>
public sealed class SpecTestTargetTests
{
    /// <summary>Uses the same local MariaDB profile for discovery and database execution.</summary>
    [Fact]
    public void Missing_local_target_selects_mariadb123()
    {
        // Arrange
        const string? configuredTarget = null;

        // Act
        var request = FunctionalDatabaseFixture.CreateRequest(configuredTarget, null, null, isCi: false);

        // Assert
        Assert.Equal("mariadb123", request.TargetId);
        Assert.Equal("mariadb:12.3", request.ServerVersionToken);
        Assert.Equal(TestDatabaseEngine.MariaDb, request.Engine);
        Assert.Equal(TestDatabaseImages.MariaDb123, request.Image);
        Assert.Equal(SpecTestTarget.Resolve(configuredTarget), request.TargetId);
    }

    /// <summary>Preserves every explicitly selected engine in local and CI runs.</summary>
    /// <param name="target">The explicit supported profile.</param>
    /// <param name="isCi">Whether execution belongs to CI.</param>
    [Theory]
    [InlineData("mysql84", false)]
    [InlineData("mysql84", true)]
    [InlineData("mysql97", false)]
    [InlineData("mysql97", true)]
    [InlineData("mariadb1011", false)]
    [InlineData("mariadb1011", true)]
    [InlineData("mariadb114", false)]
    [InlineData("mariadb114", true)]
    [InlineData("mariadb118", false)]
    [InlineData("mariadb118", true)]
    [InlineData("mariadb123", false)]
    [InlineData("mariadb123", true)]
    public void Explicit_targets_override_the_local_default(
        string target,
        bool isCi
    )
    {
        // Arrange
        var expected = SpecTestTarget.Resolve(target);

        // Act
        var request = FunctionalDatabaseFixture.CreateRequest(target, null, null, isCi);

        // Assert
        Assert.Equal(expected, request.TargetId);
    }

    /// <summary>Rejects implicit CI rows and malformed explicit target values.</summary>
    /// <param name="target">The absent or invalid target value.</param>
    /// <param name="isCi">Whether a missing value belongs to CI.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    public void Missing_ci_or_blank_explicit_targets_are_rejected(
        string? target,
        bool isCi
    )
    {
        // Arrange
        Action resolve = () => FunctionalDatabaseFixture.CreateRequest(target, null, null, isCi);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(resolve);

        // Assert
        Assert.Contains(SpecTestTarget.EnvironmentVariableName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Does not turn an unsupported explicit profile into a successful default run.</summary>
    /// <param name="target">The unsupported explicit profile.</param>
    [Theory]
    [InlineData("unsupported")]
    [InlineData(" mariadb123")]
    [InlineData("mariadb123 ")]
    public void Unknown_explicit_targets_are_rejected(
        string target
    )
    {
        // Arrange
        Action resolve = () => FunctionalDatabaseFixture.CreateRequest(target, null, null, isCi: false);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(resolve);

        // Assert
        Assert.Contains("Unsupported functional-test target", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Accepts an external endpoint only for its explicitly selected engine and version line.</summary>
    /// <param name="target">The supported target profile.</param>
    /// <param name="serverVersion">The matching external engine and version.</param>
    [Theory]
    [InlineData("mysql84", "mysql:8.4.11")]
    [InlineData("mysql97", "mysql:9.7.2")]
    [InlineData("mariadb1011", "mariadb:10.11.18")]
    [InlineData("mariadb114", "mariadb:11.4.12")]
    [InlineData("mariadb118", "mariadb:11.8.8")]
    [InlineData("mariadb123", "mariadb:12.3.2")]
    public void External_endpoints_preserve_explicit_targets(
        string target,
        string serverVersion
    )
    {
        // Arrange
        const string connectionString = "Server=external";

        // Act
        var request = FunctionalDatabaseFixture.CreateRequest(target, connectionString, serverVersion, isCi: true);

        // Assert
        Assert.Equal(target, request.TargetId);
        Assert.Equal(serverVersion, request.ServerVersionToken);
        Assert.Null(request.Image);
    }

    /// <summary>Allows a matching external MariaDB endpoint with the local default.</summary>
    [Fact]
    public void External_endpoint_can_match_the_local_default()
    {
        // Arrange
        const string serverVersion = "mariadb:12.3.3";

        // Act
        var request = FunctionalDatabaseFixture.CreateRequest(null, "Server=external", serverVersion, isCi: false);

        // Assert
        Assert.Equal("mariadb123", request.TargetId);
        Assert.Equal(serverVersion, request.ServerVersionToken);
        Assert.Null(request.Image);
    }

    /// <summary>Rejects external endpoints from another engine or MariaDB release line.</summary>
    /// <param name="serverVersion">The external version that conflicts with the local default.</param>
    [Theory]
    [InlineData("mysql:8.4.11")]
    [InlineData("mysql:9.7.2")]
    [InlineData("mariadb:11.8.9")]
    public void External_endpoints_cannot_change_the_local_default(
        string serverVersion
    )
    {
        // Arrange
        Action resolve = () => FunctionalDatabaseFixture.CreateRequest(
            null,
            "Server=external",
            serverVersion,
            isCi: false);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(resolve);

        // Assert
        Assert.Contains("does not match target 'mariadb123'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Retains family and version-line rejection for an explicit nondefault target.</summary>
    /// <param name="serverVersion">The external version that conflicts with MySQL 8.4.</param>
    [Theory]
    [InlineData("mariadb:11.4.12")]
    [InlineData("mysql:9.7.2")]
    public void External_endpoints_cannot_change_an_explicit_target(
        string serverVersion
    )
    {
        // Arrange
        Action resolve = () => FunctionalDatabaseFixture.CreateRequest(
            "mysql84",
            "Server=external",
            serverVersion,
            isCi: true);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(resolve);

        // Assert
        Assert.Contains("does not match target 'mysql84'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Requires both parts of an external endpoint without falling back to a container.</summary>
    /// <param name="connectionString">The optional external connection string.</param>
    /// <param name="serverVersion">The optional external version token.</param>
    [Theory]
    [InlineData("Server=external", null)]
    [InlineData(null, "mariadb:12.3.3")]
    public void Partial_external_endpoints_are_rejected(
        string? connectionString,
        string? serverVersion
    )
    {
        // Arrange
        Action resolve = () => FunctionalDatabaseFixture.CreateRequest(
            null,
            connectionString,
            serverVersion,
            isCi: false);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(resolve);

        // Assert
        Assert.Contains("must be configured together", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Exercises the actual test-host environment rather than only explicit helper arguments.</summary>
    [Fact]
    public void Test_host_uses_the_configured_or_local_target()
    {
        // Arrange
        var configuredTarget = Environment.GetEnvironmentVariable(SpecTestTarget.EnvironmentVariableName);
        TestDatabaseRequest? request = null;

        // Act
        var exception = Record.Exception(() => request = FunctionalDatabaseFixture.CreateRequest());

        // Assert
        if (configuredTarget is null
            && MySqlTestEnvironment.IsCi)
        {
            var missingTarget = Assert.IsType<InvalidOperationException>(exception);

            Assert.Contains(SpecTestTarget.EnvironmentVariableName, missingTarget.Message, StringComparison.Ordinal);
            Assert.Null(request);
        }
        else
        {
            Assert.Null(exception);
            Assert.NotNull(request);
            Assert.Equal(configuredTarget?.ToLowerInvariant() ?? "mariadb123", request.TargetId);
            Assert.Equal(SpecTestTarget.Resolve(), request.TargetId);
            Assert.Equal(SpecTestTarget.ResolveForExecution(), request.TargetId);
        }
    }
}
