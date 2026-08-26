using Doka.Caching.MySql;

namespace Doka.EntityFrameworkCore.MySql.Tests;

internal static class MySqlCacheTestFactory
{
    public static ServiceProvider CreateProvider(
        Action<MySqlCacheOptions>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            ConfigureValidOptions(options);
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    public static MySqlCacheOptions CreateValidOptions()
    {
        var options = new MySqlCacheOptions();
        ConfigureValidOptions(options);
        return options;
    }

    public static void ConfigureValidOptions(
        MySqlCacheOptions options
    )
    {
        options.ConnectionString = "Server=127.0.0.1;Port=1;User ID=cache;Connection Timeout=1;Pooling=false";
        options.SchemaName = "cache_database";
        options.TableName = "cache_entries";
    }
}
