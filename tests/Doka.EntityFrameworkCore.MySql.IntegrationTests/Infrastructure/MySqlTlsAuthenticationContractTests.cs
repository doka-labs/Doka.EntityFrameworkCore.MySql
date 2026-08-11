using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies encrypted transport, certificate validation, password authentication,
/// and mutual-TLS authentication against test-owned security profiles.
/// </summary>
[Collection(TlsDatabaseTestGroup.Name)]
[Trait("Category", "SecurityConfigurationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlTlsAuthenticationContractTests
{
    private const string PasswordUser = "doka_password_contract";
    private const string ClientCertificateUser = "doka_x509_contract";
    private const string CertificateValidationUser = "doka_certificate_contract";
    private const string LifecycleCallbackUser = "doka_lifecycle_callbacks";
    private const string InitialLifecyclePassword = "doka_lifecycle_password_1";
    private const string RotatedLifecyclePassword = "doka_lifecycle_password_2";
    private const string TestPassword = "doka_test_password";
    private readonly TlsDatabaseFixture _fixture;

    /// <summary>
    /// Initializes the contract with the test-owned TLS database fixture.
    /// </summary>
    /// <param name="fixture">The initialized TLS database fixture.</param>
    public MySqlTlsAuthenticationContractTests(
        TlsDatabaseFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)),
                "caching_sha2_password")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MySql97,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MySql97),
                "caching_sha2_password")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb1011),
                "mysql_native_password")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)),
                "mysql_native_password")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                "mysql_native_password")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies transport and authentication profiles against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_satisfies_the_tls_and_authentication_contract()
    {
        await AssertSecurityContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb123),
                "mysql_native_password")
            .ConfigureAwait(false);
    }

    private async Task AssertSecurityContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion,
        string expectedPasswordPlugin
    )
    {
        var endpoint = _fixture.GetEndpoint(target);
        var tlsOptions = Assert.IsType<TestDatabaseTlsOptions>(endpoint.TlsOptions);

        await AssertVerifiedTransportAsync(endpoint.ConnectionString, serverVersion)
            .ConfigureAwait(false);
        await AssertInvalidTransportProfilesAreRejectedAsync(endpoint.ConnectionString, tlsOptions)
            .ConfigureAwait(false);
        await AssertAuthenticationProfilesAsync(
                endpoint.ConnectionString,
                tlsOptions,
                serverVersion,
                expectedPasswordPlugin)
            .ConfigureAwait(false);
        await AssertConnectionLifecycleCallbacksAsync(
                endpoint.ConnectionString,
                tlsOptions,
                serverVersion)
            .ConfigureAwait(false);
        await AssertDataSourceLifecycleCallbacksAsync(
                endpoint.ConnectionString,
                tlsOptions,
                serverVersion)
            .ConfigureAwait(false);
    }

    private static async Task AssertConnectionLifecycleCallbacksAsync(
        string administrativeConnectionString,
        TestDatabaseTlsOptions tlsOptions,
        MySqlServerVersion serverVersion
    )
    {
        var databaseName = $"doka_connection_callbacks_{Guid.NewGuid():N}"[..48];
        var builder = CreateLifecycleConnectionString(
            administrativeConnectionString,
            databaseName);
        var password = builder.Password;
        builder.Password = string.Empty;
        using var clientCertificate = X509Certificate2.CreateFromPemFile(
            tlsOptions.ClientCertificateFile,
            tlsOptions.ClientKeyFile);
        using var certificateAuthority = X509CertificateLoader.LoadCertificateFromFile(
            tlsOptions.CaCertificateFile);
        var certificateCallbackCount = 0;
        var clientCertificateCallbackCount = 0;
        var passwordCallbackCount = 0;
        await using var connection = new MySqlConnection(builder.ConnectionString)
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                Interlocked.Increment(ref certificateCallbackCount);
                return ValidateTestServerCertificate(
                    certificate,
                    errors,
                    certificateAuthority);
            },
            ProvideClientCertificatesCallback = certificates =>
            {
                Interlocked.Increment(ref clientCertificateCallbackCount);
                certificates.Add(clientCertificate);
                return ValueTask.CompletedTask;
            },
            ProvidePasswordCallback = _ =>
            {
                Interlocked.Increment(ref passwordCallbackCount);
                return password;
            },
        };
        var options = IntegrationTestDbContextOptions.Create<SecurityContractContext>()
            .UseMySql(connection, serverVersion)
            .Options;

        await AssertLifecycleOperationsAsync(options)
            .ConfigureAwait(false);

        Assert.True(certificateCallbackCount >= 6);
        Assert.True(clientCertificateCallbackCount >= 6);
        Assert.True(passwordCallbackCount >= 6);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static async Task AssertDataSourceLifecycleCallbacksAsync(
        string administrativeConnectionString,
        TestDatabaseTlsOptions tlsOptions,
        MySqlServerVersion serverVersion
    )
    {
        var databaseName = $"doka_data_source_callbacks_{Guid.NewGuid():N}"[..48];
        var builder = CreateLifecycleConnectionString(administrativeConnectionString, databaseName);
        builder.UserID = LifecycleCallbackUser;
        builder.Password = string.Empty;
        using var clientCertificate = X509Certificate2.CreateFromPemFile(
            tlsOptions.ClientCertificateFile,
            tlsOptions.ClientKeyFile);
        using var certificateAuthority = X509CertificateLoader.LoadCertificateFromFile(tlsOptions.CaCertificateFile);
        var certificateCallbackCount = 0;
        var clientCertificateCallbackCount = 0;
        var passwordProviderCount = 0;
        var connectionOpenedCallbackCount = 0;
        var currentPassword = InitialLifecyclePassword;
        var rotatedPasswordPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var administrativeConnection = new MySqlConnection(administrativeConnectionString);
        await administrativeConnection
            .OpenAsync()
            .ConfigureAwait(false);
        await DropUserAsync(administrativeConnection, LifecycleCallbackUser)
            .ConfigureAwait(false);
        await CreateLifecycleCallbackUserAsync(administrativeConnection)
            .ConfigureAwait(false);

        try
        {
            await using var dataSource = new MySqlDataSourceBuilder(builder.ConnectionString)
                .UseRemoteCertificateValidationCallback((
                    _,
                    certificate,
                    _,
                    errors
                ) =>
                {
                    Interlocked.Increment(ref certificateCallbackCount);
                    return ValidateTestServerCertificate(certificate, errors, certificateAuthority);
                })
                .UseClientCertificatesCallback(certificates =>
                {
                    Interlocked.Increment(ref clientCertificateCallbackCount);
                    certificates.Add(clientCertificate);
                    return ValueTask.CompletedTask;
                })
                .UsePeriodicPasswordProvider(
                    (
                        _,
                        _
                    ) =>
                    {
                        Interlocked.Increment(ref passwordProviderCount);
                        var password = Volatile.Read(ref currentPassword);

                        if (password == RotatedLifecyclePassword)
                        {
                            rotatedPasswordPublished.TrySetResult();
                        }

                        return ValueTask.FromResult(password);
                    },
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(50))
                .UseConnectionOpenedCallback((
                    _,
                    _
                ) =>
                {
                    Interlocked.Increment(ref connectionOpenedCallbackCount);
                    return ValueTask.CompletedTask;
                })
                .Build();
            var options = IntegrationTestDbContextOptions.Create<SecurityContractContext>().UseMySql(dataSource, serverVersion)
                .Options;

            await AssertLifecycleOperationsAsync(options)
                .ConfigureAwait(false);
            await RotateLifecycleCallbackPasswordAsync(administrativeConnection)
                .ConfigureAwait(false);
            Volatile.Write(ref currentPassword, RotatedLifecyclePassword);
            await rotatedPasswordPublished
                .Task.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await AssertLifecycleOperationsAsync(options)
                .ConfigureAwait(false);

            Assert.True(certificateCallbackCount >= 12);
            Assert.True(clientCertificateCallbackCount >= 12);
            Assert.True(passwordProviderCount >= 2);
            Assert.True(connectionOpenedCallbackCount >= 12);
        }
        finally
        {
            await DropUserAsync(administrativeConnection, LifecycleCallbackUser)
                .ConfigureAwait(false);
        }
    }

    private static MySqlConnectionStringBuilder CreateLifecycleConnectionString(
        string administrativeConnectionString,
        string databaseName
    ) => new(administrativeConnectionString)
    {
        Database = databaseName,
        Pooling = false,
        SslCa = string.Empty,
        SslMode = MySqlSslMode.Required,
    };

    private static bool ValidateTestServerCertificate(
        X509Certificate? certificate,
        SslPolicyErrors errors,
        X509Certificate2 certificateAuthority
    )
    {
        if (certificate is null
            || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch
                          | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            return false;
        }

        using var serverCertificate = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificateAuthority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return chain.Build(serverCertificate);
    }

    private static async Task AssertLifecycleOperationsAsync(
        DbContextOptions<SecurityContractContext> options
    )
    {
        await using var context = new SecurityContractContext(options);
        var creator = context.GetService<IRelationalDatabaseCreator>();

        try
        {
            Assert.False(
                await creator
                    .ExistsAsync()
                    .ConfigureAwait(false));
            await creator
                .CreateAsync()
                .ConfigureAwait(false);
            Assert.True(
                await creator
                    .ExistsAsync()
                    .ConfigureAwait(false));
            Assert.False(
                await creator
                    .HasTablesAsync()
                    .ConfigureAwait(false));
            await creator
                .DeleteAsync()
                .ConfigureAwait(false);
            Assert.False(
                await creator
                    .ExistsAsync()
                    .ConfigureAwait(false));
        }
        finally
        {
            if (await creator
                    .ExistsAsync()
                    .ConfigureAwait(false))
            {
                await creator
                    .DeleteAsync()
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task AssertVerifiedTransportAsync(
        string connectionString,
        MySqlServerVersion serverVersion
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var cipherCommand = connection.CreateCommand();
        cipherCommand.CommandText = "SHOW STATUS LIKE 'Ssl_cipher';";
        await using var reader = await cipherCommand
            .ExecuteReaderAsync()
            .ConfigureAwait(false);

        Assert.True(
            await reader
                .ReadAsync()
                .ConfigureAwait(false));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(1)));

        await using var context = new SecurityContractContext(
            IntegrationTestDbContextOptions.Create<SecurityContractContext>().UseMySql(connectionString, serverVersion)
                .Options);

        Assert.Equal(
            1,
            await context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync()
                .ConfigureAwait(false));
    }

    private static async Task AssertInvalidTransportProfilesAreRejectedAsync(
        string connectionString,
        TestDatabaseTlsOptions tlsOptions
    )
    {
        var disabledTls = new MySqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            SslMode = MySqlSslMode.Disabled,
        };
        await AssertConnectionRejectedAsync(disabledTls.ConnectionString)
            .ConfigureAwait(false);

        await using var administrativeConnection = new MySqlConnection(connectionString);
        await administrativeConnection
            .OpenAsync()
            .ConfigureAwait(false);
        await DropUserAsync(administrativeConnection, CertificateValidationUser)
            .ConfigureAwait(false);

        try
        {
            // MySqlConnector can authenticate MariaDB's zero-configuration TLS
            // fingerprint with a shared password. This isolated empty-password
            // account removes that fallback so these cases test X.509 alone.
            await using (var createCommand = administrativeConnection.CreateCommand())
            {
                createCommand.CommandText = $"CREATE USER '{CertificateValidationUser}'@'%';";
                _ = await createCommand
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            var certificateValidationConnectionString = CreateUserConnectionString(
                connectionString,
                CertificateValidationUser,
                string.Empty);
            var untrustedAuthority = new MySqlConnectionStringBuilder(certificateValidationConnectionString)
            {
                SslCa = tlsOptions.UntrustedCaCertificateFile,
                SslMode = MySqlSslMode.VerifyCA,
            };
            await AssertConnectionRejectedAsync(untrustedAuthority.ConnectionString)
                .ConfigureAwait(false);

            var hostnameMismatch = new MySqlConnectionStringBuilder(certificateValidationConnectionString)
            {
                Server = IPAddress.Loopback.ToString(),
            };
            await AssertConnectionRejectedAsync(hostnameMismatch.ConnectionString)
                .ConfigureAwait(false);
        }
        finally
        {
            await DropUserAsync(administrativeConnection, CertificateValidationUser)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertAuthenticationProfilesAsync(
        string administrativeConnectionString,
        TestDatabaseTlsOptions tlsOptions,
        MySqlServerVersion serverVersion,
        string expectedPasswordPlugin
    )
    {
        await using var administrativeConnection = new MySqlConnection(administrativeConnectionString);
        await administrativeConnection
            .OpenAsync()
            .ConfigureAwait(false);

        await DropTestUsersAsync(administrativeConnection)
            .ConfigureAwait(false);

        try
        {
            await CreateTestUserAsync(administrativeConnection, PasswordUser, requireClientCertificate: false)
                .ConfigureAwait(false);
            await CreateTestUserAsync(administrativeConnection, ClientCertificateUser, requireClientCertificate: true)
                .ConfigureAwait(false);

            Assert.Equal(
                expectedPasswordPlugin,
                await GetAuthenticationPluginAsync(administrativeConnection, PasswordUser)
                    .ConfigureAwait(false));

            var passwordConnectionString = CreateUserConnectionString(
                administrativeConnectionString,
                PasswordUser,
                TestPassword);
            await AssertProviderQueryAsync(passwordConnectionString, serverVersion)
                .ConfigureAwait(false);

            var wrongPassword = new MySqlConnectionStringBuilder(passwordConnectionString)
            {
                Password = "wrong_password",
            };
            await AssertConnectionRejectedAsync(wrongPassword.ConnectionString)
                .ConfigureAwait(false);

            var clientCertificateConnectionString = new MySqlConnectionStringBuilder(
                CreateUserConnectionString(administrativeConnectionString, ClientCertificateUser, TestPassword))
            {
                SslCert = tlsOptions.ClientCertificateFile,
                SslKey = tlsOptions.ClientKeyFile,
            }.ConnectionString;
            await AssertProviderQueryAsync(clientCertificateConnectionString, serverVersion)
                .ConfigureAwait(false);

            var missingClientCertificate = new MySqlConnectionStringBuilder(clientCertificateConnectionString)
            {
                SslCert = string.Empty,
                SslKey = string.Empty,
            };
            await AssertConnectionRejectedAsync(missingClientCertificate.ConnectionString)
                .ConfigureAwait(false);
        }
        finally
        {
            await DropTestUsersAsync(administrativeConnection)
                .ConfigureAwait(false);
        }
    }

    private static async Task CreateTestUserAsync(
        MySqlConnection connection,
        string user,
        bool requireClientCertificate
    )
    {
        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE USER '{user}'@'%' IDENTIFIED BY '{TestPassword}'"
                + (requireClientCertificate ? " REQUIRE X509;" : ";");
            _ = await createCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }

        await using var grantCommand = connection.CreateCommand();
        grantCommand.CommandText = $"GRANT SELECT ON `doka_provider`.* TO '{user}'@'%';";
        _ = await grantCommand
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task CreateLifecycleCallbackUserAsync(
        MySqlConnection connection
    )
    {
        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE USER '{LifecycleCallbackUser}'@'%' "
                + $"IDENTIFIED BY '{InitialLifecyclePassword}' REQUIRE X509;";
            _ = await createCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);
        }

        await using var grantCommand = connection.CreateCommand();
        grantCommand.CommandText = $"GRANT CREATE, DROP, SELECT ON *.* TO '{LifecycleCallbackUser}'@'%';";
        _ = await grantCommand
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task RotateLifecycleCallbackPasswordAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER USER '{LifecycleCallbackUser}'@'%' "
            + $"IDENTIFIED BY '{RotatedLifecyclePassword}';";
        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task<string> GetAuthenticationPluginAsync(
        MySqlConnection connection,
        string user
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT plugin FROM mysql.user WHERE User = @user AND Host = '%';";
        command.Parameters.AddWithValue("@user", user);

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync()
                .ConfigureAwait(false));
    }

    private static async Task DropTestUsersAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP USER IF EXISTS '{PasswordUser}'@'%', '{ClientCertificateUser}'@'%';";
        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task DropUserAsync(
        MySqlConnection connection,
        string user
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP USER IF EXISTS '{user}'@'%';";
        _ = await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static string CreateUserConnectionString(
        string administrativeConnectionString,
        string user,
        string password
    )
    {
        return new MySqlConnectionStringBuilder(administrativeConnectionString)
        {
            UserID = user,
            Password = password,
            Pooling = false,
        }.ConnectionString;
    }

    private static async Task AssertProviderQueryAsync(
        string connectionString,
        MySqlServerVersion serverVersion
    )
    {
        await using var context = new SecurityContractContext(
            IntegrationTestDbContextOptions.Create<SecurityContractContext>().UseMySql(connectionString, serverVersion)
                .Options);

        Assert.Equal(
            1,
            await context
                .Database.SqlQueryRaw<int>("SELECT 1 AS Value")
                .SingleAsync()
                .ConfigureAwait(false));
    }

    private static async Task AssertConnectionRejectedAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);

        _ = await Assert
            .ThrowsAsync<MySqlException>(() => connection.OpenAsync())
            .ConfigureAwait(false);
    }

    private sealed class SecurityContractContext : DbContext
    {
        public SecurityContractContext(
            DbContextOptions<SecurityContractContext> options
        ) : base(options) { }
    }
}
