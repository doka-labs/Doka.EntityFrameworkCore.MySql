namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Exposes the database endpoint owned by the functional-test collection fixture.
/// </summary>
public static class MySqlTestEnvironment
{
    private static TestDatabaseEndpoint? s_endpoint;

    public static string ConnectionString => GetEndpoint().ConnectionString;

    public static MySqlServerVersion ServerVersion { get; private set; } = null!;

    public static bool IsCi { get; } =
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    internal static void Initialize(
        TestDatabaseEndpoint endpoint
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var serverVersion = ParseServerVersion(endpoint.ServerVersionToken);

        if (Interlocked.CompareExchange(ref s_endpoint, endpoint, null) is not null)
        {
            throw new InvalidOperationException("The functional-test database environment is already initialized.");
        }

        ServerVersion = serverVersion;
    }

    internal static void Reset(
        TestDatabaseEndpoint endpoint
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!ReferenceEquals(Interlocked.CompareExchange(ref s_endpoint, null, endpoint), endpoint))
        {
            throw new InvalidOperationException("The functional-test database endpoint is not active.");
        }

        ServerVersion = null!;
    }

    private static TestDatabaseEndpoint GetEndpoint()
    {
        return Volatile.Read(ref s_endpoint)
            ?? throw new InvalidOperationException(
                "The functional-test database fixture has not initialized. "
                + $"Live functional test classes must use collection '{FunctionalDatabaseTestGroup.Name}'.");
    }

    private static MySqlServerVersion ParseServerVersion(
        string token
    )
    {
        var separatorIndex = token.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw new InvalidOperationException(
                $"The server-version token must use the form '<engine>:<version>'; got '{token}'.");
        }

        var engine = token[..separatorIndex].Trim().ToLowerInvariant();
        var versionText = token[(separatorIndex + 1)..].Trim();
        var version = Version.Parse(versionText);

        return engine switch
        {
            "mysql" => MySqlServerVersion.MySql(version),
            "mariadb" => MySqlServerVersion.MariaDb(version),
            _ => throw new InvalidOperationException(
                $"The server-version token engine must be 'mysql' or 'mariadb'; got '{engine}'."),
        };
    }
}
