namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal sealed class BenchmarkDatabaseTarget
{
    private const string BenchmarkTargetVariable = "DOKA_BENCHMARK_TARGET";
    private const string BenchmarkPortVariable = "DOKA_BENCHMARK_DATABASE_PORT";
    private const string MySql84TargetId = "mysql84";

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
        var configuredPort = Environment.GetEnvironmentVariable(BenchmarkPortVariable);
        var contract = PerformanceContract.Load();

        return Resolve(configuredTarget, configuredPort, contract);
    }

    internal static BenchmarkDatabaseTarget Resolve(
        string? configuredTarget,
        string? configuredPort,
        PerformanceContract contract
    )
    {
        ArgumentNullException.ThrowIfNull(contract);

        var targetId = string.IsNullOrWhiteSpace(configuredTarget)
            ? MySql84TargetId
            : configuredTarget.ToLowerInvariant();

        if (!contract.RequiredTargets.TryGetValue(targetId, out var target))
        {
            throw new InvalidOperationException(
                $"Unsupported benchmark target '{configuredTarget}'. "
                + $"Set {BenchmarkTargetVariable} to one of: "
                + $"{string.Join(", ", contract.RequiredTargets.Keys.Order(StringComparer.Ordinal))}.");
        }

        if (!Version.TryParse(target.ServerVersion, out var serverVersion))
        {
            throw new InvalidDataException(
                $"Benchmark target '{targetId}' declares invalid server version " + $"'{target.ServerVersion}'.");
        }

        var isMariaDb = target.EngineFamily switch
        {
            "MySQL" => false,
            "MariaDB" => true,
            _ => throw new InvalidDataException(
                $"Benchmark target '{targetId}' declares unsupported engine family " + $"'{target.EngineFamily}'."),
        };

        return new BenchmarkDatabaseTarget(
            targetId,
            target.DisplayName,
            target.EngineFamily,
            serverVersion,
            host: "127.0.0.1",
            port: ResolvePort(configuredPort, target.HostPort),
            isMariaDb: isMariaDb);
    }

    private static int ResolvePort(
        string? configuredPort,
        int defaultPort
    )
    {
        if (string.IsNullOrWhiteSpace(configuredPort))
        {
            return defaultPort;
        }

        if (int.TryParse(configuredPort, out var port)
            && port is > 0 and <= 65535)
        {
            return port;
        }

        throw new InvalidOperationException($"{BenchmarkPortVariable} must be a TCP port between 1 and 65535.");
    }
}
