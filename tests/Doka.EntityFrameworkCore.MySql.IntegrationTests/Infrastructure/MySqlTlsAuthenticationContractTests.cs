using System.Net;

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
            new DbContextOptionsBuilder<SecurityContractContext>().UseMySql(connectionString, serverVersion)
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
            new DbContextOptionsBuilder<SecurityContractContext>().UseMySql(connectionString, serverVersion)
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
