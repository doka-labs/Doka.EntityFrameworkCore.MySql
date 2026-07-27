using Testcontainers.MariaDb;
using Testcontainers.MySql;

namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

internal sealed class TestDatabaseContainer : IAsyncDisposable
{
    private const string DatabaseName = "doka_provider";
    private const string RootPassword = "root_password";

    private readonly IAsyncDisposable _container;

    private TestDatabaseContainer(
        IAsyncDisposable container,
        string connectionString,
        string containerId
    )
    {
        _container = container;
        ConnectionString = connectionString;
        ContainerId = containerId;
    }

    public string ConnectionString { get; }

    public string ContainerId { get; }

    public static async Task<TestDatabaseContainer> StartAsync(
        TestDatabaseRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Image);

        return request.Engine switch
        {
            TestDatabaseEngine.MySql => await StartMySqlAsync(request.Image, cancellationToken)
                .ConfigureAwait(false),
            TestDatabaseEngine.MariaDb => await StartMariaDbAsync(request.Image, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Engine,
                $"Unsupported test database engine: {request.Engine}"),
        };
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static async Task<TestDatabaseContainer> StartMySqlAsync(
        string image,
        CancellationToken cancellationToken
    )
    {
        var container = new MySqlBuilder(image)
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(RootPassword)
            .WithCommand(
                "--character-set-server=utf8mb4",
                "--collation-server=utf8mb4_unicode_ci")
            .Build();

        try
        {
            await container
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TestDatabaseContainer(
                container,
                AddProviderTestSettings(container.GetConnectionString()),
                container.Id);
        }
        catch
        {
            await container
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<TestDatabaseContainer> StartMariaDbAsync(
        string image,
        CancellationToken cancellationToken
    )
    {
        var container = new MariaDbBuilder(image)
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(RootPassword)
            .WithCommand(
                "--character-set-server=utf8mb4",
                "--collation-server=utf8mb4_unicode_ci")
            .Build();

        try
        {
            await container
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TestDatabaseContainer(
                container,
                AddProviderTestSettings(container.GetConnectionString()),
                container.Id);
        }
        catch
        {
            await container
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    private static string AddProviderTestSettings(
        string connectionString
    )
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString)
        {
            PersistSecurityInfo = true,
        };

        return builder.ConnectionString;
    }
}
