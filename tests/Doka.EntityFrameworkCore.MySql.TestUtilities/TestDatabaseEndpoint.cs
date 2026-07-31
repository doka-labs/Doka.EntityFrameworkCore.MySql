namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Represents a ready database endpoint without exposing its credentials in evidence.
/// </summary>
/// <param name="TargetId">The stable matrix target identifier.</param>
/// <param name="Engine">The database engine family.</param>
/// <param name="ServerVersionToken">The provider server-version token.</param>
/// <param name="ConnectionString">The runtime connection string for the owned endpoint.</param>
/// <param name="Source">The endpoint ownership source.</param>
/// <param name="Image">The optional digest-pinned container image.</param>
/// <param name="ContainerId">The optional ephemeral container identifier.</param>
/// <param name="TlsOptions">The optional test-owned client TLS material.</param>
public sealed record TestDatabaseEndpoint(
    string TargetId,
    TestDatabaseEngine Engine,
    string ServerVersionToken,
    string ConnectionString,
    string Source,
    string? Image,
    string? ContainerId,
    TestDatabaseTlsOptions? TlsOptions = null
);

/// <summary>
/// Exposes only the client-side files required to exercise the test-owned TLS profile.
/// </summary>
/// <param name="CaCertificateFile">The trusted certificate-authority PEM file.</param>
/// <param name="UntrustedCaCertificateFile">A separate CA used by rejection tests.</param>
/// <param name="ClientCertificateFile">The client certificate PEM file.</param>
/// <param name="ClientKeyFile">The access-restricted client private-key PEM file.</param>
public sealed record TestDatabaseTlsOptions(
    string CaCertificateFile,
    string UntrustedCaCertificateFile,
    string ClientCertificateFile,
    string ClientKeyFile
);
