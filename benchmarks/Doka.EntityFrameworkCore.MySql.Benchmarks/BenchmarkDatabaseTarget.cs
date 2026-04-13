namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal sealed class BenchmarkDatabaseTarget
{
    private const string BenchmarkTargetVariable = "DOKA_BENCHMARK_TARGET";
    private const string MySql84TargetId = "mysql84";
    private const string MariaDb118TargetId = "mariadb118";

    private BenchmarkDatabaseTarget(
        string targetId,
        string displayName,
        string engineFamily,
        Version serverVersion,
        string host,
        int port,
        bool isMariaDb
    )
    {
        TargetId = targetId;
        DisplayName = displayName;
        EngineFamily = engineFamily;
        ServerVersion = serverVersion;
        Host = host;
        Port = port;
        IsMariaDb = isMariaDb;
    }

    public static BenchmarkDatabaseTarget Current { get; } = ResolveFromEnvironment();

    public string TargetId { get; }

    public string DisplayName { get; }

    public string EngineFamily { get; }

    public Version ServerVersion { get; }

    public string Host { get; }

    public int Port { get; }

    public bool IsMariaDb { get; }

    public string CreateConnectionString(
        string databaseName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            Database = databaseName,
            UserID = "root",
            Password = "root_password",
        };

        return builder.ConnectionString;
    }

    public MySqlServerVersion CreateServerVersion() => IsMariaDb
        ? MySqlServerVersion.MariaDb(ServerVersion)
        : MySqlServerVersion.MySql(ServerVersion);

    private static BenchmarkDatabaseTarget ResolveFromEnvironment()
    {
        var configuredTarget = Environment.GetEnvironmentVariable(BenchmarkTargetVariable);

        if (string.IsNullOrWhiteSpace(configuredTarget)
            || string.Equals(configuredTarget, MySql84TargetId, StringComparison.OrdinalIgnoreCase))
        {
            return new BenchmarkDatabaseTarget(
                MySql84TargetId,
                "MySQL 8.4",
                "MySQL",
                new Version(8, 4, 0),
                host: "127.0.0.1",
                port: 33068,
                isMariaDb: false);
        }

        if (string.Equals(configuredTarget, MariaDb118TargetId, StringComparison.OrdinalIgnoreCase))
        {
            return new BenchmarkDatabaseTarget(
                MariaDb118TargetId,
                "MariaDB 11.8",
                "MariaDB",
                new Version(11, 8, 0),
                host: "127.0.0.1",
                port: 33069,
                isMariaDb: true);
        }

        throw new InvalidOperationException(
            $"Unsupported benchmark target '{configuredTarget}'. "
            + $"Set {BenchmarkTargetVariable} to '{MySql84TargetId}' or '{MariaDb118TargetId}'.");
    }
}
