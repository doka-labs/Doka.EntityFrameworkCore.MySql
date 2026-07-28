using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Creates EF Core specification-test services with the real Doka MySQL
/// provider registered against a deterministic, non-live MySQL 8.4 profile.
/// </summary>
public sealed class MySqlTestHelpers : RelationalTestHelpers
{
    private const string DummyConnectionString =
        "Server=localhost;Database=DokaSpecificationContract;User ID=root;";

    private static readonly MySqlServerVersion s_serverVersion =
        MySqlServerVersion.MySql(new Version(8, 4, 0));

    private MySqlTestHelpers()
    {
    }

    /// <summary>
    /// Gets the shared stateless helper instance.
    /// </summary>
    public static MySqlTestHelpers Instance { get; } = new();

    /// <inheritdoc />
    public override IServiceCollection AddProviderServices(
        IServiceCollection services
    ) => services
        .AddEntityFrameworkDokaMySql()
        .AddEntityFrameworkDokaMySqlNetTopologySuite();

    /// <inheritdoc />
    public override DbContextOptionsBuilder UseProviderOptions(
        DbContextOptionsBuilder optionsBuilder
    ) => optionsBuilder.UseMySql(
        DummyConnectionString,
        s_serverVersion,
        provider => provider.UseNetTopologySuite());

    /// <inheritdoc />
    public override LoggingDefinitions LoggingDefinitions { get; } =
        new MySqlLoggingDefinitions();
}
