namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Describes one database target required by a live test assembly.
/// </summary>
/// <param name="TargetId">The stable matrix target identifier.</param>
/// <param name="Engine">The requested database engine family.</param>
/// <param name="ServerVersionToken">The provider server-version token.</param>
/// <param name="Image">The optional digest-pinned image used for local provisioning.</param>
/// <param name="ConnectionStringEnvironmentVariable">The external endpoint override variable.</param>
/// <param name="SecurityProfile">The transport-security profile owned by the fixture.</param>
public sealed record TestDatabaseRequest(
    string TargetId,
    TestDatabaseEngine Engine,
    string ServerVersionToken,
    string? Image,
    string ConnectionStringEnvironmentVariable,
    TestDatabaseSecurityProfile SecurityProfile = TestDatabaseSecurityProfile.PlainText
);

/// <summary>
/// Identifies the database image family used for local provisioning.
/// </summary>
public enum TestDatabaseEngine
{
    /// <summary>
    /// Identifies Oracle MySQL Server.
    /// </summary>
    MySql = 0,

    /// <summary>
    /// Identifies MariaDB Server.
    /// </summary>
    MariaDb = 1,
}

/// <summary>
/// Selects the transport-security profile owned by the test infrastructure.
/// </summary>
public enum TestDatabaseSecurityProfile
{
    /// <summary>
    /// Uses the container image's default unencrypted test endpoint.
    /// </summary>
    PlainText = 0,

    /// <summary>
    /// Requires a test-owned certificate authority and encrypted transport.
    /// </summary>
    TlsRequired = 1,
}
