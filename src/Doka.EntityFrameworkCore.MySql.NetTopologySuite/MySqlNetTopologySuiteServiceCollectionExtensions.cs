namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Registers the optional NetTopologySuite integration services for the provider.
/// </summary>
public static class MySqlNetTopologySuiteServiceCollectionExtensions
{
    /// <summary>
    /// Adds the optional NetTopologySuite runtime services to the supplied collection.
    /// </summary>
    /// <param name="serviceCollection">The target service collection.</param>
    /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
    public static IServiceCollection AddEntityFrameworkDokaMySqlNetTopologySuite(
        this IServiceCollection serviceCollection
    )
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.TryAddSingleton<MySqlNetTopologySuiteSpatialTypeProvider>();
        serviceCollection.TryAddSingleton<IMySqlNetTopologySuiteMarker>(serviceProvider =>
            serviceProvider.GetRequiredService<MySqlNetTopologySuiteSpatialTypeProvider>());
        serviceCollection.TryAddSingleton<IMySqlSpatialTypeProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<MySqlNetTopologySuiteSpatialTypeProvider>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor
                .Singleton<IRelationalTypeMappingSourcePlugin, MySqlNetTopologySuiteTypeMappingSourcePlugin>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMethodCallTranslatorPlugin, MySqlNetTopologySuiteMethodCallTranslatorPlugin>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Scoped<IMemberTranslatorPlugin, MySqlNetTopologySuiteMemberTranslatorPlugin>());

        return serviceCollection;
    }
}
