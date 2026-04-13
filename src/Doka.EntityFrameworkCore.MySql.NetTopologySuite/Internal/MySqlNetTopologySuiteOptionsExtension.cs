namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new MySqlNetTopologySuiteOptionsExtensionInfo(this);

    public void ApplyServices(
        IServiceCollection services
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEntityFrameworkDokaMySqlNetTopologySuite();
    }

    public void Validate(
        IDbContextOptions options
    ) => ArgumentNullException.ThrowIfNull(options);
}
