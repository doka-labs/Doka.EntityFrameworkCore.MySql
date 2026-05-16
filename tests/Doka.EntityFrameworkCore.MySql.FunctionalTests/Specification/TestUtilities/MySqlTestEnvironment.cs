namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Resolves the connection-string + server-version pair the specification suite runs against.
/// Two configuration channels:
/// <list type="bullet">
///   <item><c>DOKA_SPEC_TEST_CONNECTION_STRING</c> + <c>DOKA_SPEC_TEST_SERVER_VERSION</c> environment variables.</item>
///   <item>Documented compose-default fallback when neither environment variable is set.</item>
/// </list>
/// The fallback assumes the local docker-compose stack (<c>docker/compose.yml</c>) is running with the
/// repository-default MySQL 8.4 container on port 33068. CI sets the environment variables explicitly.
/// </summary>
public static class MySqlTestEnvironment
{
    private const string DefaultConnectionString =
        "Server=127.0.0.1;Port=33068;User ID=root;Password=root_password;Persist Security Info=True;";

    private const string DefaultServerVersionToken = "mysql:8.4";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOKA_SPEC_TEST_CONNECTION_STRING") ?? DefaultConnectionString;

    public static MySqlServerVersion ServerVersion { get; } = ParseServerVersion(
        Environment.GetEnvironmentVariable("DOKA_SPEC_TEST_SERVER_VERSION") ?? DefaultServerVersionToken);

    public static bool IsCi { get; } =
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private static MySqlServerVersion ParseServerVersion(
        string token
    )
    {
        var separatorIndex = token.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw new InvalidOperationException(
                $"DOKA_SPEC_TEST_SERVER_VERSION must use the form '<engine>:<version>'; got '{token}'.");
        }

        var engine = token[..separatorIndex].Trim().ToLowerInvariant();
        var versionText = token[(separatorIndex + 1)..].Trim();
        var version = Version.Parse(versionText);

        return engine switch
        {
            "mysql" => MySqlServerVersion.MySql(version),
            "mariadb" => MySqlServerVersion.MariaDb(version),
            _ => throw new InvalidOperationException(
                $"DOKA_SPEC_TEST_SERVER_VERSION engine must be 'mysql' or 'mariadb'; got '{engine}'."),
        };
    }
}
