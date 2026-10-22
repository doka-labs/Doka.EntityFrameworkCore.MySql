namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies caller-owned data-source and transaction boundaries.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies that ambient rollback does not roll back cache writes.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Ambient_transactions_do_not_own_cache_writes(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var key = useAsync ? "ambient-async" : "ambient-sync";

        // Act
        using (var scope = new System.Transactions.TransactionScope(
                   System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            // WHY: Neither cache path may enlist its write in the ambient transaction.
            if (useAsync)
            {
                await store
                    .Cache
                    .SetAsync(key, [1], new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                // WHY: The false variant verifies that the synchronous cache write ignores the ambient transaction.
                // ReSharper disable once MethodHasAsyncOverload
                store.Cache.Set(key, [1], new DistributedCacheEntryOptions());
            }
        }

        // Assert
        Assert.Equal(
            new byte[] { 1 },
            await store
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies that a supplied data source runs caller-owned connection hooks.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_run_caller_connection_hooks(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var passwordRequests = 0;
        var connectionOpens = 0;
        await using var dataSource = CreateExternalDataSource(
            store,
            () => Interlocked.Increment(ref passwordRequests),
            () => Interlocked.Increment(ref connectionOpens));

        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();

        // Act
        await cache
            .SetAsync("external-hooks", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        var actual = await cache
            .GetAsync("external-hooks", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.Equal(new byte[] { 4, 2 }, actual);
        Assert.True(Volatile.Read(ref passwordRequests) > 0);
        Assert.True(Volatile.Read(ref connectionOpens) > 0);
    }

    /// <summary>Verifies that a supplied data source shares its caller-owned pool.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_share_the_caller_pool(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await using var dataSource = CreateExternalDataSource(store);
        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        await cache
            .SetAsync("external-pool", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);
        bool completedWhileExhausted;
        Exception? cancellationFailure;

        // Act
        await using (var lease = await dataSource
                         .OpenConnectionAsync(CancellationToken.None)
                         .ConfigureAwait(false))
        {
            using var cancellation = new CancellationTokenSource();
            var pending = cache.GetAsync("external-pool", cancellation.Token);

            try
            {
                completedWhileExhausted = pending.IsCompleted;
            }
            finally
            {
                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);

                cancellationFailure = await Record
                    .ExceptionAsync(() => pending.WaitAsync(
                        TimeSpan.FromSeconds(15),
                        CancellationToken.None))
                    .ConfigureAwait(false);
            }
        }

        // Assert
        Assert.False(
            completedWhileExhausted,
            "The cache must share the caller's exhausted single-connection pool.");
        Assert.IsAssignableFrom<OperationCanceledException>(cancellationFailure);
        Assert.Equal(
            new byte[] { 4, 2 },
            await cache
                .GetAsync("external-pool", CancellationToken.None)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies that DI disposal does not dispose a caller-owned data source.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task External_data_sources_survive_provider_disposal(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        await using var dataSource = CreateExternalDataSource(store);
        await using var provider = CreateExternalCacheProvider(store, dataSource);
        var cache = provider.GetRequiredService<IBufferDistributedCache>();
        await cache
            .SetAsync("external-disposal", [4, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        await provider
            .DisposeAsync()
            .ConfigureAwait(false);

        var failure = await Record
            .ExceptionAsync(() => cache.GetAsync("external-disposal", CancellationToken.None))
            .ConfigureAwait(false);

        await using var survivingConnection = await dataSource
            .OpenConnectionAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var command = new MySqlCommand("SELECT 1;", survivingConnection);
        var result = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<ObjectDisposedException>(failure);
        Assert.Equal(1L, result);
    }

    /// <summary>Creates a caller-owned single-connection pool with observable connection hooks.</summary>
    private static MySqlDataSource CreateExternalDataSource(
        CacheIntegrationStore store,
        Action? onPasswordRequest = null,
        Action? onConnectionOpen = null
    )
    {
        var connectionBuilder = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = 1,
        };

        var password = connectionBuilder.Password;
        connectionBuilder.Remove("Password");
        var dataSourceBuilder = new MySqlDataSourceBuilder(connectionBuilder.ConnectionString);

        // WHY: A supplied data source must retain its caller-owned password and open callbacks.
        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                onPasswordRequest?.Invoke();

                return ValueTask.FromResult(password);
            },
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(1));

        dataSourceBuilder.UseConnectionOpenedCallback((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            onConnectionOpen?.Invoke();

            return ValueTask.CompletedTask;
        });

        return dataSourceBuilder.Build();
    }

    /// <summary>Registers the cache against an existing caller-owned data source.</summary>
    private static ServiceProvider CreateExternalCacheProvider(
        CacheIntegrationStore store,
        MySqlDataSource dataSource
    )
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            options.DataSource = dataSource;
            options.SchemaName = store.SchemaName;
            options.TableName = store.TableName;
        });

        return services.BuildServiceProvider();
    }
}
