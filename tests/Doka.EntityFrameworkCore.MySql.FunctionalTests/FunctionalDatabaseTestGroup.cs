using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FunctionalDatabaseTestGroup : ICollectionFixture<FunctionalDatabaseFixture>
{
    public const string Name = "functional-database";
}

public sealed class FunctionalDatabaseFixture : IAsyncLifetime
{
    private const string TargetEnvironmentVariable = "DOKA_SPEC_TEST_TARGET";
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

    private static TestDatabaseRequest CreateRequest()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var externalServerVersion = Environment.GetEnvironmentVariable(ServerVersionEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(externalConnectionString)
            || !string.IsNullOrWhiteSpace(externalServerVersion))
        {
            if (string.IsNullOrWhiteSpace(externalConnectionString)
                || string.IsNullOrWhiteSpace(externalServerVersion))
            {
                throw new InvalidOperationException(
                    $"{ConnectionStringEnvironmentVariable} and {ServerVersionEnvironmentVariable} must be configured together.");
            }

            var engine = ParseEngine(externalServerVersion);
            return new TestDatabaseRequest(
                "spec",
                engine,
                externalServerVersion,
                null,
                ConnectionStringEnvironmentVariable);
        }

        var targetId = Environment.GetEnvironmentVariable(TargetEnvironmentVariable) ?? "mysql84";

        return targetId.ToLowerInvariant() switch
        {
            "mysql84" => new TestDatabaseRequest(
                "mysql84",
                TestDatabaseEngine.MySql,
                "mysql:8.4",
                TestDatabaseImages.MySql84,
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
            _ => throw new InvalidOperationException(
                $"Unsupported functional-test target '{targetId}' in {TargetEnvironmentVariable}. "
                + "Supported values are: mysql84, mariadb114, mariadb118."),
        };
    }

    private static TestDatabaseEngine ParseEngine(
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

        return serverVersionToken[..separatorIndex].Trim().ToLowerInvariant() switch
        {
            "mysql" => TestDatabaseEngine.MySql,
            "mariadb" => TestDatabaseEngine.MariaDb,
            var engine => throw new InvalidOperationException(
                $"{ServerVersionEnvironmentVariable} engine must be 'mysql' or 'mariadb'; got '{engine}'."),
        };
    }
}
