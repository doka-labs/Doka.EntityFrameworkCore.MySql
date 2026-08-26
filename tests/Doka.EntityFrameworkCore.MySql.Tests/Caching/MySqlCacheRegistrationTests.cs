using System.Buffers;
using System.Net;
using Doka.Caching.MySql;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers cache registration, shared ownership, and startup validation.
/// </summary>
public sealed class MySqlCacheRegistrationTests
{
    /// <summary>
    /// Verifies that registration preserves the caller's service collection.
    /// </summary>
    [Fact]
    public void Registration_returns_the_service_collection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions));
    }

    /// <summary>
    /// Verifies that invalid registration arguments fail immediately.
    /// </summary>
    [Fact]
    public void Registration_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>("services", () =>
            MySqlCacheServiceCollectionExtensions.AddDistributedMySqlCache(
                null!, MySqlCacheTestFactory.ConfigureValidOptions));
        Assert.Throws<ArgumentNullException>("configure", () =>
            new ServiceCollection().AddDistributedMySqlCache(null!));
    }

    /// <summary>
    /// Verifies that both interfaces resolve one singleton across scopes and threads.
    /// </summary>
    [Fact]
    public void Both_interfaces_share_one_singleton_across_scopes_and_concurrent_resolution()
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        Assert.IsType<IBufferDistributedCache>(cache, exactMatch: false);
        Assert.Same(cache, provider.GetRequiredService<IBufferDistributedCache>());
        Parallel.For(0, 32, _ =>
        {
            using var scope = provider.CreateScope();
            Assert.Same(cache, scope.ServiceProvider.GetRequiredService<IDistributedCache>());
            Assert.Same(cache, scope.ServiceProvider.GetRequiredService<IBufferDistributedCache>());
        });
    }

    /// <summary>
    /// Verifies that repeated registration does not create multiple cache owners.
    /// </summary>
    [Fact]
    public void Repeated_registration_retains_one_instance_for_each_interface()
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);
        services.AddDistributedMySqlCache(options => options.TableName = "other_entries");
        using var provider = services.BuildServiceProvider();

        var cache = Assert.Single(provider.GetServices<IDistributedCache>());
        Assert.Same(cache, Assert.Single(provider.GetServices<IBufferDistributedCache>()));
        Assert.Equal("other_entries", provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value.TableName);
    }

    /// <summary>
    /// Verifies that an earlier cache registration cannot split the two contracts.
    /// </summary>
    [Fact]
    public void Earlier_cache_registration_does_not_split_the_interface_instances()
    {
        var services = new ServiceCollection();
        var previous = new UnusedCache();
        services.AddSingleton<IDistributedCache>(previous);
        services.AddSingleton<IBufferDistributedCache>(previous);
        services.AddDistributedMySqlCache(MySqlCacheTestFactory.ConfigureValidOptions);
        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDistributedCache>();
        Assert.NotSame(previous, cache);
        Assert.Same(cache, provider.GetRequiredService<IBufferDistributedCache>());
        Assert.Equal(new[] { previous, cache }, provider.GetServices<IDistributedCache>());
        Assert.Equal(new[] { previous, cache }, provider.GetServices<IBufferDistributedCache>());
    }

    /// <summary>
    /// Verifies repeated backend selection preserves foreign and keyed registrations without duplicating aliases.
    /// </summary>
    [Fact]
    public void Repeated_registration_preserves_foreign_and_keyed_cache_descriptors()
    {
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
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();

        Assert.Same(cache, bufferCache);
        Assert.Equal(new[] { previous, later, cache }, provider.GetServices<IDistributedCache>());
        Assert.Equal(new[] { previous, later, bufferCache }, provider.GetServices<IBufferDistributedCache>());
        Assert.Same(keyed, provider.GetRequiredKeyedService<IDistributedCache>("other"));
        Assert.Same(keyed, provider.GetRequiredKeyedService<IBufferDistributedCache>("other"));
    }

    /// <summary>
    /// Verifies alias disposal is idempotent and cannot reopen a disposed cache owner.
    /// </summary>
    [Fact]
    public void Disposing_shared_aliases_is_idempotent_and_rejects_further_database_access()
    {
        using var provider = MySqlCacheTestFactory.CreateProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bufferCache = provider.GetRequiredService<IBufferDistributedCache>();

        Assert.IsAssignableFrom<IDisposable>(cache).Dispose();
        Assert.IsAssignableFrom<IDisposable>(bufferCache).Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Get("key"));
    }

    /// <summary>
    /// Verifies cache disposal rejects every operation while leaving a borrowed data source available.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_cache_rejects_operations_without_disposing_a_borrowed_data_source(
        bool asynchronousDisposal
    )
    {
        await using var dataSource = new MySqlDataSource(
            "Server=127.0.0.1;Port=1;User ID=cache;AutoEnlist=false;Pooling=false");

        await using var provider = MySqlCacheTestFactory.CreateProvider(options =>
        {
            options.ConnectionString = string.Empty;
            options.DataSource = dataSource;
        });

        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        if (asynchronousDisposal)
        {
            await Assert
                .IsAssignableFrom<IAsyncDisposable>(cache)
                .DisposeAsync();
        }
        else
        {
            // WHY: The synchronous disposal contract must also preserve caller ownership.
            // ReSharper disable once MethodHasAsyncOverload
            Assert
                .IsAssignableFrom<IDisposable>(cache)
                .Dispose();
        }

        var options = new DistributedCacheEntryOptions();
        var writer = new ArrayBufferWriter<byte>();

        // WHY: All synchronous entry points must reject a disposed owner before using its borrowed pool.
        // ReSharper disable MethodHasAsyncOverload
        Assert.Throws<ObjectDisposedException>(() => cache.Get("key"));
        Assert.Throws<ObjectDisposedException>(() => cache.Set("key", new byte[] { 1 }, options));
        Assert.Throws<ObjectDisposedException>(() => cache.Set("key", ReadOnlySequence<byte>.Empty, options));
        Assert.Throws<ObjectDisposedException>(() => cache.TryGet("key", writer));
        Assert.Throws<ObjectDisposedException>(() => cache.Refresh("key"));
        Assert.Throws<ObjectDisposedException>(() => cache.Remove("key"));
        // ReSharper restore MethodHasAsyncOverload

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.GetAsync("key", CancellationToken.None));

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.SetAsync(
                "key",
                new byte[] { 1 },
                options,
                CancellationToken.None));

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() =>
                cache
                    .SetAsync("key", ReadOnlySequence<byte>.Empty, options, CancellationToken.None)
                    .AsTask());

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() =>
                cache
                    .TryGetAsync("key", writer, CancellationToken.None)
                    .AsTask());

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.RefreshAsync("key", CancellationToken.None));

        await Assert
            .ThrowsAsync<ObjectDisposedException>(() => cache.RemoveAsync("key", CancellationToken.None));

        await provider
            .DisposeAsync();

        await using var connection = dataSource.CreateConnection();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Verifies validation at startup without requiring a cache operation.
    /// </summary>
    [Fact]
    public void Startup_validation_rejects_missing_configuration_before_the_first_operation()
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(static _ =>
        {
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("ConnectionString", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("SchemaName", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("TableName", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies eager validation when either cache interface is first resolved.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolution_rejects_invalid_configuration(
        bool buffered
    )
    {
        using var provider = MySqlCacheTestFactory.CreateProvider(options => options.TableName = string.Empty);

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = buffered
                ? provider.GetRequiredService<IBufferDistributedCache>()
                : provider.GetRequiredService<IDistributedCache>();
        });
    }

    /// <summary>
    /// Verifies that configuring and resolving the cache never opens a database connection.
    /// </summary>
    [Fact]
    public void Startup_and_resolution_do_not_connect_or_create_the_schema()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var provider = MySqlCacheTestFactory.CreateProvider(options =>
            options.ConnectionString = $"Server=127.0.0.1;Port={port};User ID=cache;Pooling=false");

        provider.GetRequiredService<IStartupValidator>().Validate();
        _ = provider.GetRequiredService<IDistributedCache>();
        _ = provider.GetRequiredService<IBufferDistributedCache>();

        Assert.False(listener.Pending());
    }

    /// <summary>
    /// Verifies a DI-provided pool is resolved through normal options configuration without opening a connection.
    /// </summary>
    [Fact]
    public async Task External_data_source_supports_DI_configuration_without_startup_network_access()
    {
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
        provider
            .GetRequiredService<IStartupValidator>()
            .Validate();

        Assert.Same(
            provider.GetRequiredService<IDistributedCache>(),
            provider.GetRequiredService<IBufferDistributedCache>());

        Assert.Same(dataSource, provider.GetRequiredService<IOptions<MySqlCacheOptions>>().Value.DataSource);
        Assert.False(listener.Pending());
    }

    private sealed class UnusedCache : IBufferDistributedCache
    {
        public byte[]? Get(
            string key
        ) => throw new InvalidOperationException();

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        public void Set(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options
        ) => throw new InvalidOperationException();

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        public void Refresh(
            string key
        ) => throw new InvalidOperationException();

        public Task RefreshAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        public void Remove(
            string key
        ) => throw new InvalidOperationException();

        public Task RemoveAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        public bool TryGet(
            string key,
            IBufferWriter<byte> destination
        ) => throw new InvalidOperationException();

        public ValueTask<bool> TryGetAsync(
            string key,
            IBufferWriter<byte> destination,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        public void Set(
            string key,
            ReadOnlySequence<byte> value,
            DistributedCacheEntryOptions options
        ) => throw new InvalidOperationException();

        public ValueTask SetAsync(
            string key,
            ReadOnlySequence<byte> value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        ) => throw new InvalidOperationException();
    }
}
