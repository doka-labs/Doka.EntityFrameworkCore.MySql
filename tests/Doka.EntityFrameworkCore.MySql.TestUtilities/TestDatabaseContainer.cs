using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.MariaDb;
using Testcontainers.MySql;

namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

internal sealed class TestDatabaseContainer : IAsyncDisposable
{
    private const string DatabaseName = "doka_provider";
    private const string RootPassword = "root_password";
    private const string ContainerCaCertificateFile = "/tmp/doka-test-ca.pem";
    private const string ContainerServerCertificateFile = "/tmp/doka-test-server-cert.pem";
    private const string ContainerServerKeyFile = "/tmp/doka-test-server-key.pem";
    private const string DatabasePort = "3306/tcp";
    private const string LoopbackAddress = "127.0.0.1";
    private const uint DatabaseUserId = 999;
    private const uint DatabaseGroupId = 999;

    private readonly IAsyncDisposable _container;
    private readonly TestDatabaseTlsMaterial? _tlsMaterial;

    private TestDatabaseContainer(
        IAsyncDisposable container,
        string connectionString,
        string containerId,
        TestDatabaseTlsMaterial? tlsMaterial
    )
    {
        _container = container;
        _tlsMaterial = tlsMaterial;
        ConnectionString = connectionString;
        ContainerId = containerId;
    }

    public string ConnectionString { get; }

    public string ContainerId { get; }

    public TestDatabaseTlsOptions? TlsOptions => _tlsMaterial?.Options;

    public static async Task<TestDatabaseContainer> StartAsync(
        TestDatabaseRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Image);

        var tlsMaterial = request.SecurityProfile == TestDatabaseSecurityProfile.TlsRequired
            ? TestDatabaseTlsMaterial.Create()
            : null;

        try
        {
            return request.Engine switch
            {
                TestDatabaseEngine.MySql => await StartMySqlAsync(request.Image, tlsMaterial, cancellationToken)
                    .ConfigureAwait(false),
                TestDatabaseEngine.MariaDb => await StartMariaDbAsync(request.Image, tlsMaterial, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Engine,
                    $"Unsupported test database engine: {request.Engine}"),
            };
        }
        catch
        {
            tlsMaterial?.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _container
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            _tlsMaterial?.Dispose();
        }
    }

    private static async Task<TestDatabaseContainer> StartMySqlAsync(
        string image,
        TestDatabaseTlsMaterial? tlsMaterial,
        CancellationToken cancellationToken
    )
    {
        var builder = new MySqlBuilder(image)
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(RootPassword)
            .WithCreateParameterModifier(BindDatabasePortToLoopback);

        builder = ConfigureTls(builder, tlsMaterial);

        var container = builder
            .WithCommand(BuildServerCommand(tlsMaterial))
            .Build();

        try
        {
            await container
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);
            await VerifyLoopbackBindingAsync(container, cancellationToken)
                .ConfigureAwait(false);

            var connectionString = AddProviderTestSettings(
                container.GetConnectionString(),
                tlsMaterial);

            VerifyLoopbackConnectionString(
                connectionString,
                container.GetMappedPublicPort(3306));

            return new TestDatabaseContainer(
                container,
                connectionString,
                container.Id,
                tlsMaterial);
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
        TestDatabaseTlsMaterial? tlsMaterial,
        CancellationToken cancellationToken
    )
    {
        var builder = new MariaDbBuilder(image)
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(RootPassword)
            .WithCreateParameterModifier(BindDatabasePortToLoopback);

        builder = ConfigureTls(builder, tlsMaterial);

        var container = builder
            .WithCommand(BuildServerCommand(tlsMaterial))
            .Build();

        try
        {
            await container
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);
            await VerifyLoopbackBindingAsync(container, cancellationToken)
                .ConfigureAwait(false);

            var connectionString = AddProviderTestSettings(
                container.GetConnectionString(),
                tlsMaterial);

            VerifyLoopbackConnectionString(
                connectionString,
                container.GetMappedPublicPort(3306));

            return new TestDatabaseContainer(
                container,
                connectionString,
                container.Id,
                tlsMaterial);
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
        string connectionString,
        TestDatabaseTlsMaterial? tlsMaterial
    )
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString)
        {
            PersistSecurityInfo = true,
        };

        if (tlsMaterial is not null)
        {
            builder.Server = "localhost";
            builder.SslMode = MySqlSslMode.VerifyFull;
            builder.SslCa = tlsMaterial.Options.CaCertificateFile;
        }

        return builder.ConnectionString;
    }

    private static void BindDatabasePortToLoopback(
        CreateContainerParameters parameters
    )
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var portBindings = parameters.HostConfig?.PortBindings
            ?? throw new InvalidOperationException("Testcontainers did not configure Docker host port bindings.");

        if (!portBindings.TryGetValue(DatabasePort, out var bindings)
            || bindings.Count == 0)
        {
            throw new InvalidOperationException($"Testcontainers did not publish the database port {DatabasePort}.");
        }

        // A random host port avoids collisions but does not constrain the
        // listening interface. Set HostIP explicitly so repository-known test
        // credentials are never reachable from another network principal.
        foreach (var binding in bindings)
        {
            binding.HostIP = LoopbackAddress;
        }
    }

    private static async Task VerifyLoopbackBindingAsync(
        IContainer container,
        CancellationToken cancellationToken
    )
    {
        using var dockerClient = TestcontainersSettings
            .OS.DockerEndpointAuthConfig.GetDockerClientBuilder()
            .Build();

        var inspection = await dockerClient
            .Containers.InspectContainerAsync(container.Id, cancellationToken)
            .ConfigureAwait(false);

        var portBindings = inspection.NetworkSettings?.Ports
            ?? throw new InvalidOperationException(
                $"Docker did not report network settings for container '{container.Id}'.");

        if (!portBindings.TryGetValue(DatabasePort, out var bindings)
            || bindings.Count == 0
            || bindings.Any(binding => !string.Equals(binding.HostIP, LoopbackAddress, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Container '{container.Id}' must bind {DatabasePort} exclusively to {LoopbackAddress}.");
        }
    }

    private static void VerifyLoopbackConnectionString(
        string connectionString,
        ushort mappedPort
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var isLoopbackHost = string.Equals(builder.Server, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(builder.Server, out var address) && IPAddress.IsLoopback(address));

        if (!isLoopbackHost
            || builder.Port != mappedPort)
        {
            throw new InvalidOperationException(
                $"The test connection must use the mapped loopback endpoint 127.0.0.1:{mappedPort}.");
        }
    }

    private static string[] BuildServerCommand(
        TestDatabaseTlsMaterial? tlsMaterial
    )
    {
        var command = new List<string>
        {
            "--character-set-server=utf8mb4",
            "--collation-server=utf8mb4_unicode_ci",
        };

        if (tlsMaterial is not null)
        {
            command.Add($"--ssl-ca={ContainerCaCertificateFile}");
            command.Add($"--ssl-cert={ContainerServerCertificateFile}");
            command.Add($"--ssl-key={ContainerServerKeyFile}");
            command.Add("--require-secure-transport=ON");
        }

        return command.ToArray();
    }

    private static MySqlBuilder ConfigureTls(
        MySqlBuilder builder,
        TestDatabaseTlsMaterial? tlsMaterial
    )
    {
        if (tlsMaterial is null)
        {
            return builder;
        }

        return builder
            .WithResourceMapping(
                tlsMaterial.CaCertificate,
                ContainerCaCertificateFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                tlsMaterial.ServerCertificate,
                ContainerServerCertificateFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                tlsMaterial.ServerKey,
                ContainerServerKeyFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead);
    }

    private static MariaDbBuilder ConfigureTls(
        MariaDbBuilder builder,
        TestDatabaseTlsMaterial? tlsMaterial
    )
    {
        if (tlsMaterial is null)
        {
            return builder;
        }

        return builder
            .WithResourceMapping(
                tlsMaterial.CaCertificate,
                ContainerCaCertificateFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                tlsMaterial.ServerCertificate,
                ContainerServerCertificateFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(
                tlsMaterial.ServerKey,
                ContainerServerKeyFile,
                DatabaseUserId,
                DatabaseGroupId,
                UnixFileModes.UserRead);
    }
}
