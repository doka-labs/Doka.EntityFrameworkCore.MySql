using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FunctionalDatabaseTestGroup : ICollectionFixture<FunctionalDatabaseFixture>
{
    public const string Name = "functional-database";
}

public sealed class FunctionalDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "DOKA_SPEC_TEST_CONNECTION_STRING";
    private const string ServerVersionEnvironmentVariable = "DOKA_SPEC_TEST_SERVER_VERSION";

    private TestDatabaseEndpoint? _endpoint;
    private TestDatabaseSession? _session;

    public async Task InitializeAsync()
    {
        var request = CreateRequest();
        var session = await TestDatabaseSession
            .StartAsync([request])
            .ConfigureAwait(false);

        var endpoint = session.GetEndpoint(request.TargetId);

        try
        {
            MySqlTestEnvironment.Initialize(endpoint);
            _session = session;
            _endpoint = endpoint;
        }
        catch
        {
            await session
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_session is null
            || _endpoint is null)
        {
            return;
        }

        var session = _session;
        var endpoint = _endpoint;
        _session = null;
        _endpoint = null;

        try
        {
            MySqlTestEnvironment.Reset(endpoint);
        }
        finally
        {
            await session
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    private static TestDatabaseRequest CreateRequest() => CreateRequest(
        Environment.GetEnvironmentVariable(SpecTestTarget.EnvironmentVariableName),
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable),
        Environment.GetEnvironmentVariable(ServerVersionEnvironmentVariable));

    internal static TestDatabaseRequest CreateRequest(
        string? configuredTarget,
        string? externalConnectionString,
        string? externalServerVersion
    )
    {
        var targetId = SpecTestTarget.ResolveForExecution(configuredTarget);
        var targetRequest = CreateTargetRequest(targetId);

        if (!string.IsNullOrWhiteSpace(externalConnectionString)
            || !string.IsNullOrWhiteSpace(externalServerVersion))
        {
            if (string.IsNullOrWhiteSpace(externalConnectionString)
                || string.IsNullOrWhiteSpace(externalServerVersion))
            {
                throw new InvalidOperationException(
                    $"{ConnectionStringEnvironmentVariable} and "
                    + $"{ServerVersionEnvironmentVariable} must be configured together.");
            }

            var (externalEngine, externalVersion) = ParseServerVersionToken(externalServerVersion);
            var (_, targetVersion) = ParseServerVersionToken(targetRequest.ServerVersionToken);

            if (externalEngine != targetRequest.Engine
                || externalVersion.Major != targetVersion.Major
                || externalVersion.Minor != targetVersion.Minor)
            {
                throw new InvalidOperationException(
                    $"{ServerVersionEnvironmentVariable} value '{externalServerVersion}' does not match "
                    + $"target '{targetId}', which requires {targetRequest.ServerVersionToken}.");
            }

            return targetRequest with
            {
                ServerVersionToken = externalServerVersion,
                Image = null,
            };
        }

        return targetRequest;
    }

    private static TestDatabaseRequest CreateTargetRequest(
        string targetId
    ) => targetId.ToLowerInvariant() switch
    {
        "mysql84" => new TestDatabaseRequest(
            "mysql84",
            TestDatabaseEngine.MySql,
            "mysql:8.4",
            TestDatabaseImages.MySql84,
            ConnectionStringEnvironmentVariable),
        "mysql97" => new TestDatabaseRequest(
            "mysql97",
            TestDatabaseEngine.MySql,
            "mysql:9.7",
            TestDatabaseImages.MySql97,
            ConnectionStringEnvironmentVariable),
        "mariadb1011" => new TestDatabaseRequest(
            "mariadb1011",
            TestDatabaseEngine.MariaDb,
            "mariadb:10.11",
            TestDatabaseImages.MariaDb1011,
            ConnectionStringEnvironmentVariable),
        "mariadb114" => new TestDatabaseRequest(
            "mariadb114",
            TestDatabaseEngine.MariaDb,
            "mariadb:11.4",
            TestDatabaseImages.MariaDb114,
            ConnectionStringEnvironmentVariable),
        "mariadb118" => new TestDatabaseRequest(
            "mariadb118",
            TestDatabaseEngine.MariaDb,
            "mariadb:11.8",
            TestDatabaseImages.MariaDb118,
            ConnectionStringEnvironmentVariable),
        "mariadb123" => new TestDatabaseRequest(
            "mariadb123",
            TestDatabaseEngine.MariaDb,
            "mariadb:12.3",
            TestDatabaseImages.MariaDb123,
            ConnectionStringEnvironmentVariable),
        _ => throw new InvalidOperationException(
            $"Unsupported functional-test target '{targetId}' in {SpecTestTarget.EnvironmentVariableName}. "
            + "Supported values are: mysql84, mysql97, mariadb1011, mariadb114, mariadb118, mariadb123."),
    };

    private static (TestDatabaseEngine Engine, Version Version) ParseServerVersionToken(
        string serverVersionToken
    )
    {
        var separatorIndex = serverVersionToken.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException(
                $"{ServerVersionEnvironmentVariable} must use the form '<engine>:<version>'; "
                + $"got '{serverVersionToken}'.");
        }

        var engine = serverVersionToken[..separatorIndex]
                .Trim()
                .ToLowerInvariant() switch
        {
            "mysql" => TestDatabaseEngine.MySql,
            "mariadb" => TestDatabaseEngine.MariaDb,
            var unknownEngine => throw new InvalidOperationException(
                $"{ServerVersionEnvironmentVariable} engine must be 'mysql' or 'mariadb'; "
                + $"got '{unknownEngine}'."),
        };

        if (!Version.TryParse(
                serverVersionToken[(separatorIndex + 1)..]
                    .Trim(),
                out var version)
            || version.Major < 0
            || version.Minor < 0)
        {
            throw new InvalidOperationException(
                $"{ServerVersionEnvironmentVariable} version must contain a numeric major and minor line; "
                + $"got '{serverVersionToken}'.");
        }

        return (engine, version);
    }
}
