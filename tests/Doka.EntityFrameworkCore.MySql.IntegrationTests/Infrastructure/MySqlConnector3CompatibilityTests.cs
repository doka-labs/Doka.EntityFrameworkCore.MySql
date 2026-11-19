namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies the driver behaviors that differ between supported Connector 2.x and 3.x.</summary>
[IntegrationDatabaseTest]
// WHY: Other driver contracts clear all pools; they must not race with this family's reuse assertions.
[Collection(ProcessWideIntegrationTestGroup.Name)]
[Trait("Category", "DriverContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlConnector3CompatibilityTests
{
    private static readonly Version s_driverVersion = ReadDriverVersion();

    /// <summary>Reports each selected engine as an independent driver contract.</summary>
    public static TheoryData<string> Targets
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString());
            }

            return cases;
        }
    }

    private static bool UsesConnector3 => s_driverVersion.Major == 3;

    /// <summary>Limits shared datasets to qualified targets selected for this run.</summary>
    private static IEnumerable<IntegrationDatabaseTarget> GetSelectedSupportedTargets()
        // WHY: A mixed selection can include diagnostic MySQL 8.0; every data
        // case must independently stay inside this family's qualified matrix.
        => IntegrationTestEnvironment.GetSupportedTargets()
            .Where(IntegrationTestEnvironment.IsTargetSelected);

    /// <summary>Keeps every topic partial in this family's source-owned database.</summary>
    private static string CreateConnectionString(
        string target
    ) => new MySqlConnectionStringBuilder(
        IntegrationTestEnvironment.GetConnectionString(Enum.Parse<IntegrationDatabaseTarget>(target)))
    {
        GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        Pooling = false,
        AutoEnlist = false,
        AllowUserVariables = true,
    }.ConnectionString;

    /// <summary>Reads the actual package identity rather than assuming assembly-version policy.</summary>
    private static Version ReadDriverVersion()
    {
        // WHY: AssemblyVersion can be held stable for binary compatibility; the
        // informational version identifies the package whose behavior is being tested.
        var identity = typeof(MySqlConnection).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? throw new InvalidOperationException("The MySqlConnector package identity is unavailable.");

        var suffix = identity.IndexOfAny(['+', '-']);
        var version = Version.Parse(suffix < 0 ? identity : identity[..suffix]);
        if (version.Major is not (2 or 3))
        {
            throw new InvalidOperationException($"Driver compatibility cases do not qualify MySqlConnector {identity}.");
        }

        return version;
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task EnableNoBackslashEscapesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await ExecuteAsync(
            connection,
            "SET SESSION sql_mode = 'STRICT_TRANS_TABLES,NO_BACKSLASH_ESCAPES';",
            cancellationToken);
        var mode = Assert.IsType<string>(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode;", cancellationToken));

        // WHY: Aligned escaping reproduces the bug; strict mode makes the
        // legacy overlong GUID fail with 1406 instead of being silently truncated.
        Assert.Contains("NO_BACKSLASH_ESCAPES", mode.Split(','), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("STRICT_TRANS_TABLES", mode.Split(','), StringComparer.OrdinalIgnoreCase);
        Assert.True(new MySqlConnectionStringBuilder(connection.ConnectionString).NoBackslashEscapes);
    }

    private static async Task<(long Count, string? Hex)> ReadGuidStoreAsync(
        MySqlConnection connection,
        string table,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*), MAX(HEX(`Token`)) FROM `{table}`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
