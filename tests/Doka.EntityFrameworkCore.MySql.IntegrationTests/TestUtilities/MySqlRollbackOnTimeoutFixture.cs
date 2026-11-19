namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Owns an isolated representative server with startup-only full rollback on lock-wait timeout.</summary>
internal sealed class MySqlRollbackOnTimeoutFixture : IAsyncDisposable
{
    private const string Database = "doka_timeout_policy";
    private const string Password = "rollback_on_timeout_test_password";
    private readonly IContainer _container;

    private MySqlRollbackOnTimeoutFixture(
        IContainer container,
        string connectionString,
        MySqlServerVersion version
    )
    {
        _container = container;
        ConnectionString = connectionString;
        Version = version;
    }

    /// <summary>Gets the private endpoint; callers must own their schemas and connections.</summary>
    internal string ConnectionString { get; }

    /// <summary>Gets the version corresponding to the exact repository-pinned image.</summary>
    internal MySqlServerVersion Version { get; }

    /// <summary>Gets the owned container identity for cleanup and native-policy evidence.</summary>
    internal string ContainerId => _container.Id;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // WHY: The fixture owns this exact container; disposing it after cancellation must release its anonymous volumes too.
        return _container.DisposeAsync();
    }

    /// <summary>Starts one exact pinned representative with no external endpoint or global-setting override.</summary>
    internal static async Task<MySqlRollbackOnTimeoutFixture> StartAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // WHY: This policy is fixed at startup. A private container avoids changing the
        // normal six-engine session or any other project's innodb_rollback_on_timeout value.
        IContainer container = target switch
        {
            IntegrationDatabaseTarget.MySql84 => new MySqlBuilder(TestDatabaseImages.MySql84)
                .WithDatabase(Database)
                .WithUsername("root")
                .WithPassword(Password)
                .WithCreateParameterModifier(BindLoopback)
                .WithCommand("--innodb-rollback-on-timeout=ON", "--character-set-server=utf8mb4")
                .Build(),
            IntegrationDatabaseTarget.MariaDb123 => new MariaDbBuilder(TestDatabaseImages.MariaDb123)
                .WithDatabase(Database)
                .WithUsername("root")
                .WithPassword(Password)
                .WithCreateParameterModifier(BindLoopback)
                .WithCommand("--innodb-rollback-on-timeout=ON", "--character-set-server=utf8mb4")
                .Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(target),
                target,
                "The alternate timeout policy is qualified on the pinned MySQL 8.4 and MariaDB 12.3 representatives."),
        };

        try
        {
            await container
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);

            var builder = new MySqlConnectionStringBuilder
            {
                Server = container.Hostname,
                Port = container.GetMappedPublicPort(3306),
                Database = Database,
                UserID = "root",
                Password = Password,
                Pooling = false,
                AutoEnlist = false,
                AllowUserVariables = true,
                GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            };

            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var command = new MySqlCommand("SELECT @@innodb_rollback_on_timeout;", connection);

            if (Convert.ToInt32(await command
                        .ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture)
                != 1)
            {
                throw new InvalidOperationException(
                    "The owned alternate server did not enable rollback-on-timeout at startup.");
            }

            return new MySqlRollbackOnTimeoutFixture(container,
                builder.ConnectionString,
                IntegrationTestEnvironment.GetServerVersion(target));
        }
        catch (Exception primary)
        {
            // WHY: Failed startup still owns its Docker resources; teardown cannot use the cancelled request token.
            try
            {
                await container
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }

            throw;
        }
    }

    /// <summary>Restricts repository-known test credentials to the local Docker host loopback interface.</summary>
    private static void BindLoopback(
        CreateContainerParameters parameters
    )
    {
        var bindings = parameters.HostConfig?.PortBindings;

        if (bindings is null
            || !bindings.TryGetValue("3306/tcp", out var ports)
            || ports.Count == 0)
        {
            throw new InvalidOperationException(
                "The owned database container must publish its loopback-only database port.");
        }

        foreach (var port in ports)
        {
            port.HostIP = "127.0.0.1";
        }
    }
}
