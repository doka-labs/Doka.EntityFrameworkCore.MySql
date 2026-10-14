namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Serializes tests that observe or mutate process-wide diagnostics and
/// MySqlConnector pools rather than only their own database state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideIntegrationTestGroup
{
    /// <summary>Gets the collection name for process-wide integration tests.</summary>
    public const string Name = "process-wide-integration";
}
