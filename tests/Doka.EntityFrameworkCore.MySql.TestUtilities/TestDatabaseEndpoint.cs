namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Represents a ready database endpoint without exposing its credentials in evidence.
/// </summary>
public sealed record TestDatabaseEndpoint(
    string TargetId,
    TestDatabaseEngine Engine,
    string ServerVersionToken,
    string ConnectionString,
    string Source,
    string? Image,
    string? ContainerId
);
