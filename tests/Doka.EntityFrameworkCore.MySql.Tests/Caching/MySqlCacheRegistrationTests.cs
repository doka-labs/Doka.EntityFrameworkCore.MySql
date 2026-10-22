using System.Net;
using Doka.Caching.MySql;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers cache registration, shared ownership, and startup validation.
/// </summary>
public sealed partial class MySqlCacheRegistrationTests
{
    /// <summary>
    /// Verifies that registration preserves the caller's service collection.
    /// </summary>
    [Fact]
    public void Registration_returns_the_service_collection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);

        // Assert
        Assert.Same(services, result);
    }

    /// <summary>
    /// Verifies that a missing service collection fails immediately.
    /// </summary>
    [Fact]
    public void Registration_rejects_a_null_service_collection()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act
        var exception = Record.Exception(() =>
            MySqlCacheServiceCollectionExtensions.AddDistributedMySqlCache(
                services, MySqlCacheTestFactory.ConfigureValidOptions));

        // Assert
        Assert.Equal("services", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    /// <summary>
    /// Verifies that a missing options callback fails immediately.
    /// </summary>
    [Fact]
    public void Registration_rejects_a_null_configuration_callback()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var exception = Record.Exception(() => services.AddDistributedMySqlCache(null!));

        // Assert
        Assert.Equal("configure", Assert.IsType<ArgumentNullException>(exception).ParamName);
    }

    /// <summary>
    /// Verifies that both interfaces resolve one singleton across scopes and threads.
    /// </summary>
    [Fact]
    public void Both_interfaces_share_one_singleton_across_scopes_and_concurrent_resolution()
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var resolutions = new (IDistributedCache Cache, IBufferDistributedCache BufferCache)[32];

        // Act
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        Parallel.For(0, resolutions.Length, index =>
        {
            using var scope = provider.CreateScope();
            resolutions[index] = (
                scope.ServiceProvider.GetRequiredService<IDistributedCache>(),
                scope.ServiceProvider.GetRequiredService<IBufferDistributedCache>());
        });

        // Assert
        Assert.IsType<IBufferDistributedCache>(cache, exactMatch: false);
        Assert.Same(cache, bufferCache);
        Assert.All(resolutions, resolution =>
        {
            Assert.Same(cache, resolution.Cache);
            Assert.Same(cache, resolution.BufferCache);
        });
    }

    /// <summary>
    /// Verifies that repeated registration does not create multiple cache owners.
    /// </summary>
    [Fact]
    public void Repeated_registration_retains_one_instance_for_each_interface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);
        services.AddDistributedMySqlCache(options => options.TableName = "other_entries");
        using var provider = services.BuildServiceProvider();

        // Act
        var caches = provider.GetServices<IDistributedCache>().ToArray();
        var bufferCaches = provider.GetServices<IBufferDistributedCache>().ToArray();
        var tableName = provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value.TableName;

        // Assert
        var cache = Assert.Single(caches);
        Assert.Same(cache, Assert.Single(bufferCaches));
        Assert.Equal("other_entries", tableName);
    }

    /// <summary>
    /// Verifies that an earlier cache registration cannot split the two contracts.
    /// </summary>
    [Fact]
    public void Earlier_cache_registration_does_not_split_the_interface_instances()
    {
        // Arrange
        var services = new ServiceCollection();
        var previous = new UnusedCache();
        services.AddSingleton<IDistributedCache>(previous);
        services.AddSingleton<IBufferDistributedCache>(previous);
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);
        using var provider = services.BuildServiceProvider();

        // Act
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var caches = provider.GetServices<IDistributedCache>().ToArray();
        var bufferCaches = provider.GetServices<IBufferDistributedCache>().ToArray();

        // Assert
        Assert.NotSame(previous, cache);
        Assert.Same(cache, bufferCache);
        Assert.Equal(new[] { previous, cache }, caches);
        Assert.Equal(new[] { previous, cache }, bufferCaches);
    }

    /// <summary>
    /// Verifies repeated backend selection preserves foreign and keyed registrations without duplicating aliases.
    /// </summary>
    [Fact]
    public void Repeated_registration_preserves_foreign_and_keyed_cache_descriptors()
    {
        // Arrange
        var services = new ServiceCollection();
        var previous = new UnusedCache();
        var keyed = new UnusedCache();
        services.AddSingleton<IDistributedCache>(previous);
        services.AddSingleton<IBufferDistributedCache>(previous);
        services.AddKeyedSingleton<IDistributedCache>("other", keyed);
        services.AddKeyedSingleton<IBufferDistributedCache>("other", keyed);
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);

        var later = new UnusedCache();
        services.AddSingleton<IDistributedCache>(later);
        services.AddSingleton<IBufferDistributedCache>(later);
        services.AddDistributedMySqlCache(static _ =>
        {
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var caches = provider.GetServices<IDistributedCache>().ToArray();
        var bufferCaches = provider.GetServices<IBufferDistributedCache>().ToArray();
        var keyedCache = provider.GetRequiredKeyedService<IDistributedCache>("other");
        var keyedBufferCache = provider.GetRequiredKeyedService<IBufferDistributedCache>("other");

        // Assert
        Assert.Same(cache, bufferCache);
        Assert.Equal(new[] { previous, later, cache }, caches);
        Assert.Equal(new[] { previous, later, bufferCache }, bufferCaches);
        Assert.Same(keyed, keyedCache);
        Assert.Same(keyed, keyedBufferCache);
    }

    /// <summary>
    /// Verifies validation at startup without requiring a cache operation.
    /// </summary>
    [Fact]
    public void Startup_validation_rejects_missing_configuration_before_the_first_operation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(static _ =>
        {
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var exception = Record.Exception(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());

        // Assert
        var validationException = Assert.IsType<OptionsValidationException>(exception);
        Assert.Contains(
            validationException.Failures,
            failure => failure.Contains("ConnectionString", StringComparison.Ordinal));

        Assert.Contains(
            validationException.Failures,
            failure => failure.Contains("SchemaName", StringComparison.Ordinal));

        Assert.Contains(
            validationException.Failures,
            failure => failure.Contains("TableName", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies eager validation when either cache interface is first resolved.
    /// </summary>
    /// <param name="buffered">Whether to resolve the buffer-oriented cache contract.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolution_rejects_invalid_configuration(
        bool buffered
    )
    {
        // Arrange
        using var provider = MySqlCacheTestFactory.CreateProvider(options => options.TableName = string.Empty);

        // Act
        var exception = Record.Exception(() =>
        {
            _ = buffered
                ? provider.GetRequiredService<IBufferDistributedCache>()
                : provider.GetRequiredService<IDistributedCache>();
        });

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    /// <summary>
    /// Verifies that cache resolution forwards the DI-provided cleanup clock.
    /// </summary>
    [Fact]
    public void Resolution_forwards_registered_time_provider_to_the_cache_singleton()
    {
        // Arrange
        var timeProvider = new RecordingTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);
        using var provider = services.BuildServiceProvider();

        var initialTimestampReadCount = timeProvider.TimestampReadCount;

        // Act
        _ = provider.GetRequiredService<IDistributedCache>();
        _ = provider.GetRequiredService<IBufferDistributedCache>();

        // Assert
        Assert.Equal(0, initialTimestampReadCount);
        Assert.Equal(1, timeProvider.TimestampReadCount);
    }

    /// <summary>
    /// Verifies that the system-time fallback resolves without requiring a clock or database connection.
    /// </summary>
    [Fact]
    public void Startup_and_resolution_without_a_time_provider_do_not_connect_or_create_the_schema()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = $"Server=127.0.0.1;Port={port};User ID=cache;Pooling=false");

        // Act
        provider.GetRequiredService<IStartupValidator>().Validate();
        _ = provider.GetRequiredService<IDistributedCache>();
        _ = provider.GetRequiredService<IBufferDistributedCache>();

        // Assert
        Assert.Null(provider.GetService<TimeProvider>());
        Assert.False(listener.Pending());
    }

    /// <summary>
    /// Verifies a DI-provided pool is resolved through normal options configuration without opening a connection.
    /// </summary>
    [Fact]
    public async Task External_data_source_supports_DI_configuration_without_startup_network_access()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var dataSource = new MySqlDataSource(
            $"Server=127.0.0.1;Port={port};User ID=cache;AutoEnlist=false;Pooling=false");

        var services = new ServiceCollection();
        services.AddSingleton(dataSource);
        services.AddDistributedMySqlCache(options =>
        {
            options.SchemaName = "cache_database";
            options.TableName = "cache_entries";
        });

        services
            .AddOptions<MySqlCacheOptions>()
            .Configure<MySqlDataSource>((options, source) => options.DataSource = source);

        await using var provider = services.BuildServiceProvider();

        // Act
        provider
            .GetRequiredService<IStartupValidator>()
            .Validate();

        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();
        var configuredDataSource = provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value.DataSource;

        // Assert
        Assert.Same(cache, bufferCache);
        Assert.Same(dataSource, configuredDataSource);
        Assert.False(listener.Pending());
    }
}
