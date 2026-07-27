namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Describes one database target required by a live test assembly.
/// </summary>
public sealed record TestDatabaseRequest(
    string TargetId,
    TestDatabaseEngine Engine,
    string ServerVersionToken,
    string? Image,
    string ConnectionStringEnvironmentVariable
);

/// <summary>
/// Identifies the database image family used for local provisioning.
/// </summary>
public enum TestDatabaseEngine
{
    MySql = 0,
    MariaDb = 1,
}
