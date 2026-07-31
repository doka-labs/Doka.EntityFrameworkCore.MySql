namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Serializes process-wide ActivitySource and MeterListener tests so one test's
/// listener cannot change another test's no-listener or measurement contract.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlDiagnosticsTestGroup
{
    public const string Name = "MySQL diagnostics contract";
}
