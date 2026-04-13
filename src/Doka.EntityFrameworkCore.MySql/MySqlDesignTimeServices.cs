using Microsoft.EntityFrameworkCore.Design;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Registers provider-specific design-time services for tooling integration.
/// </summary>
public sealed class MySqlDesignTimeServices : IDesignTimeServices
{
    /// <summary>
    /// Configures design-time service registrations.
    /// </summary>
    /// <param name="serviceCollection">The design-time service collection.</param>
    public void ConfigureDesignTimeServices(
        IServiceCollection serviceCollection
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddEntityFrameworkDokaMySqlDesignTime();
    }
}
