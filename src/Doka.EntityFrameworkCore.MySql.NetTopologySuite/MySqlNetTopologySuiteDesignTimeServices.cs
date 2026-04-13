using Microsoft.EntityFrameworkCore.Design;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Registers optional NetTopologySuite design-time services for reverse engineering and scaffolding.
/// </summary>
public sealed class MySqlNetTopologySuiteDesignTimeServices : IDesignTimeServices
{
    /// <summary>
    /// Configures the optional NetTopologySuite design-time services.
    /// </summary>
    /// <param name="serviceCollection">The design-time service collection.</param>
    public void ConfigureDesignTimeServices(
        IServiceCollection serviceCollection
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddEntityFrameworkDokaMySqlNetTopologySuite();
    }
}
