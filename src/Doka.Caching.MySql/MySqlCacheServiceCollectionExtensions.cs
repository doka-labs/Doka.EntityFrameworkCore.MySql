namespace Doka.Caching.MySql;

/// <summary>
/// Registers the MySQL-backed distributed cache.
/// </summary>
public static class MySqlCacheServiceCollectionExtensions
{
    private static readonly ServiceDescriptor s_cacheRegistration =
        ServiceDescriptor.Singleton<IDistributedCache>(static provider =>
            provider.GetRequiredService<MySqlDistributedCache>());

    private static readonly ServiceDescriptor s_bufferCacheRegistration =
        ServiceDescriptor.Singleton<IBufferDistributedCache>(static provider =>
            provider.GetRequiredService<MySqlDistributedCache>());

    /// <summary>
    /// Adds one MySQL-backed cache instance for both
    /// <see cref="IDistributedCache"/> and <see cref="IBufferDistributedCache"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The cache configuration callback.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDistributedMySqlCache(
        this IServiceCollection services,
        Action<MySqlCacheOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<MySqlCacheOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MySqlCacheOptions>, MySqlCacheOptionsValidator>());

        services.TryAddSingleton(static provider => new MySqlDistributedCache(
            provider.GetRequiredService<IOptions<MySqlCacheOptions>>(),
            provider.GetService<ILogger<MySqlDistributedCache>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MySqlDistributedCache>.Instance,
            provider.GetService<TimeProvider>()));

        services.Remove(s_cacheRegistration);
        services.Remove(s_bufferCacheRegistration);
        services.Add(s_cacheRegistration);
        services.Add(s_bufferCacheRegistration);

        return services;
    }
}
