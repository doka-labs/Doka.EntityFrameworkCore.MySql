using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Creates short-lived certificates for one isolated database container and
/// removes all client-side private-key material when the container is disposed.
/// </summary>
internal sealed class TestDatabaseTlsMaterial : IDisposable
{
    private const string ServerName = "localhost";
    private readonly string _directory;
    private bool _disposed;

    private TestDatabaseTlsMaterial(
        string directory,
        byte[] caCertificate,
        byte[] serverCertificate,
        byte[] serverKey,
        TestDatabaseTlsOptions options
    )
    {
        _directory = directory;
        CaCertificate = caCertificate;
        ServerCertificate = serverCertificate;
        ServerKey = serverKey;
        Options = options;
    }

    /// <summary>
    /// Gets the certificate-authority certificate mapped into the container.
    /// </summary>
    public byte[] CaCertificate { get; }

    /// <summary>
    /// Gets the server certificate mapped into the database container.
    /// </summary>
    public byte[] ServerCertificate { get; }

    /// <summary>
    /// Gets the server private key mapped into the database container.
    /// </summary>
    public byte[] ServerKey { get; }

    /// <summary>
    /// Gets the client-side certificate files used by connection profiles.
    /// </summary>
    public TestDatabaseTlsOptions Options { get; }

    /// <summary>
    /// Creates an isolated certificate authority plus server, client, and
    /// untrusted certificate material for one test-owned database.
    /// </summary>
    public static TestDatabaseTlsMaterial Create()
    {
        var directory = Directory.CreateTempSubdirectory("doka-mysql-tls-")
            .FullName;

        try
        {
            using var caKey = RSA.Create(3072);
            using var caCertificate = CreateCertificateAuthority(caKey);
            using var serverKey = RSA.Create(3072);
            using var serverCertificate = CreateIssuedCertificate(caCertificate, serverKey, ServerName, isServer: true);
            using var clientKey = RSA.Create(3072);
            using var clientCertificate = CreateIssuedCertificate(
                caCertificate,
                clientKey,
                "doka-integration-client",
                isServer: false);

            using var untrustedCaKey = RSA.Create(3072);
            using var untrustedCaCertificate = CreateCertificateAuthority(untrustedCaKey);

            var caCertificatePem = caCertificate.ExportCertificatePem();
            var serverCertificatePem = serverCertificate.ExportCertificatePem();
            var serverKeyPem = serverKey.ExportPkcs8PrivateKeyPem();
            var clientCertificateFile = Path.Combine(directory, "client-cert.pem");
            var clientKeyFile = Path.Combine(directory, "client-key.pem");
            var caCertificateFile = Path.Combine(directory, "ca.pem");
            var untrustedCaCertificateFile = Path.Combine(directory, "untrusted-ca.pem");

            File.WriteAllText(caCertificateFile, caCertificatePem, Encoding.ASCII);
            File.WriteAllText(
                untrustedCaCertificateFile,
                untrustedCaCertificate.ExportCertificatePem(),
                Encoding.ASCII);
            File.WriteAllText(clientCertificateFile, clientCertificate.ExportCertificatePem(), Encoding.ASCII);
            File.WriteAllText(clientKeyFile, clientKey.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
            RestrictPrivateKey(clientKeyFile);

            return new TestDatabaseTlsMaterial(
                directory,
                Encoding.ASCII.GetBytes(caCertificatePem),
                Encoding.ASCII.GetBytes(serverCertificatePem),
                Encoding.ASCII.GetBytes(serverKeyPem),
                new TestDatabaseTlsOptions(
                    caCertificateFile,
                    untrustedCaCertificateFile,
                    clientCertificateFile,
                    clientKeyFile));
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(ServerKey);
        Directory.Delete(_directory, recursive: true);
    }

    private static X509Certificate2 CreateCertificateAuthority(
        RSA key
    )
    {
        var request = new CertificateRequest(
            "CN=Doka Integration Test CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static X509Certificate2 CreateIssuedCertificate(
        X509Certificate2 issuer,
        RSA key,
        string commonName,
        bool isServer
    )
    {
        var request = new CertificateRequest(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new(isServer ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2"),
                },
                critical: true));

        if (isServer)
        {
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(ServerName);
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        }

        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] &= 0x7F;

        return request.Create(
            issuer,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(2),
            serialNumber);
    }

    private static void RestrictPrivateKey(
        string privateKeyFile
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(privateKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
