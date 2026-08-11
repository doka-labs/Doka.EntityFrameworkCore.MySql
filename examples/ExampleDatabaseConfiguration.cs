using MySqlConnector;

namespace Doka.EntityFrameworkCore.MySql.Examples;

/// <summary>
/// Resolves the database endpoint shared by the runnable examples.
/// </summary>
/// <remarks>
/// Every example replaces the configured database name with an example-owned
/// name. This prevents <c>EnsureDeleted</c> from touching an application
/// database supplied through the environment.
/// </remarks>
internal sealed class ExampleDatabaseConfiguration
{
    private const string DefaultTarget = "mysql84";

    private ExampleDatabaseConfiguration(
        string connectionString,
        MySqlServerVersion serverVersion,
        string target
    )
    {
        ConnectionString = connectionString;
        ServerVersion = serverVersion;
        Target = target;
    }

    /// <summary>
    /// Gets the connection string with the example-owned database name.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the explicit server version used by provider configuration.
    /// </summary>
    public MySqlServerVersion ServerVersion { get; }

    /// <summary>
    /// Gets the normalized engine target name.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Creates an isolated configuration for one example database.
    /// </summary>
    public static ExampleDatabaseConfiguration Create(
        string databaseName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var target = Environment
                .GetEnvironmentVariable("DOKA_EXAMPLE_DATABASE_TARGET")
                ?.Trim()
                .ToLowerInvariant()
            ?? DefaultTarget;
        var serverVersion = ResolveServerVersion(target);
        var configuredConnectionString = Environment.GetEnvironmentVariable("DOKA_EXAMPLE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("DOKA_MYSQL_CONNECTION_STRING") ?? DefaultConnectionString(target);
        var connectionStringBuilder =
            new MySqlConnectionStringBuilder(configuredConnectionString)
            {
                Database = databaseName,
            };

        return new ExampleDatabaseConfiguration(connectionStringBuilder.ConnectionString, serverVersion, target);
    }

    private static MySqlServerVersion ResolveServerVersion(
        string target
    ) => target switch
    {
        "mysql84" => MySqlServerVersion.MySql(new Version(8, 4, 0)),
        "mysql97" => MySqlServerVersion.MySql(new Version(9, 7, 0)),
        "mariadb1011" => MySqlServerVersion.MariaDb(new Version(10, 11, 0)),
        "mariadb114" => MySqlServerVersion.MariaDb(new Version(11, 4, 0)),
        "mariadb118" => MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
        "mariadb123" => MySqlServerVersion.MariaDb(new Version(12, 3, 0)),
        _ => throw new InvalidOperationException(
            "DOKA_EXAMPLE_DATABASE_TARGET must be mysql84, mysql97, mariadb1011, "
            + "mariadb114, mariadb118, or mariadb123."),
    };

    private static string DefaultConnectionString(
        string target
    ) => target switch
    {
        "mysql84" => "Server=localhost;Port=33068;User ID=root;Password=root_password;",
        "mysql97" => "Server=localhost;Port=33070;User ID=root;Password=root_password;",
        "mariadb1011" => "Server=localhost;Port=33066;User ID=root;Password=root_password;",
        "mariadb114" => "Server=localhost;Port=33067;User ID=root;Password=root_password;",
        "mariadb118" => "Server=localhost;Port=33069;User ID=root;Password=root_password;",
        "mariadb123" => "Server=localhost;Port=33071;User ID=root;Password=root_password;",
        _ => throw new InvalidOperationException(
            "DOKA_EXAMPLE_DATABASE_TARGET must be mysql84, mysql97, mariadb1011, "
            + "mariadb114, mariadb118, or mariadb123."),
    };
}
