namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies explicit deployment and table isolation.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies that a cache operation never deploys its missing table.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Cache_access_before_deployment_fails_without_creating_a_table(
        string targetName
    )
    {
        // Arrange
        await using var store = await CacheIntegrationStore
            .CreateAsync(ParseTarget(targetName), MySqlDistributedCacheFixture.DatabaseOwner)
            .ConfigureAwait(false);

        // Act
        var failure = await Record
            .ExceptionAsync(() => store.Cache.GetAsync("before-deployment", CancellationToken.None))
            .ConfigureAwait(false);

        // Assert
        Assert.IsType<MySqlException>(failure);
        Assert.Equal(0L, await store.CountTablesAsync().ConfigureAwait(false));
    }

    /// <summary>Verifies that repeated deployment preserves the existing table and its entries.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Deployment_is_idempotent_and_preserves_entries(
        string targetName
    )
    {
        // Arrange
        await using var store = await CreateDeployedStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);
        var script = MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName);
        await store.Cache
            .SetAsync("deployment-marker", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        await store.ExecuteAsync(script).ConfigureAwait(false);

        // Assert
        Assert.Equal(new byte[] { 1 },
            await store.Cache.GetAsync("deployment-marker", CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(1L, await store.CountTablesAsync().ConfigureAwait(false));
        Assert.Equal($"Doka.Caching.MySql schema version {MySqlCacheSchema.Version}",
            await store.ReadTableCommentAsync().ConfigureAwait(false));
    }

    /// <summary>Verifies writing and reading a value through a quoted table identifier.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Quoted_table_identifiers_preserve_written_values(
        string targetName
    )
    {
        // Arrange
        var store = await fixture.GetStoreAsync(ParseTarget(targetName)).ConfigureAwait(false);
        var tableName = store.TableName + "`quoted";
        var qualifiedName = MySqlCacheIdentifier.GetQualifiedName(store.SchemaName, tableName);
        await store.ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, tableName)).ConfigureAwait(false);

        try
        {
            await using var provider = CreateQuotedCacheProvider(store, tableName);
            var cache = provider.GetRequiredService<IDistributedCache>();

            // Act
            await cache.SetAsync("quoted", [1, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);
            var result = await cache.GetAsync("quoted", CancellationToken.None).ConfigureAwait(false);

            // Assert
            Assert.Equal(new byte[] { 1, 2 }, result);
        }
        finally
        {
            await store.ExecuteAsync($"DROP TABLE {qualifiedName};").ConfigureAwait(false);
        }
    }

    /// <summary>Verifies removal through a quoted table identifier.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Quoted_table_identifiers_support_removal(
        string targetName
    )
    {
        // Arrange
        var store = await fixture.GetStoreAsync(ParseTarget(targetName)).ConfigureAwait(false);
        var tableName = store.TableName + "`quoted";
        var qualifiedName = MySqlCacheIdentifier.GetQualifiedName(store.SchemaName, tableName);
        await store.ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, tableName)).ConfigureAwait(false);

        try
        {
            await using var provider = CreateQuotedCacheProvider(store, tableName);
            var cache = provider.GetRequiredService<IDistributedCache>();
            await cache.SetAsync("quoted", [1, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            // Act
            await cache.RemoveAsync("quoted", CancellationToken.None).ConfigureAwait(false);

            // Assert
            Assert.Null(await cache.GetAsync("quoted", CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.ExecuteAsync($"DROP TABLE {qualifiedName};").ConfigureAwait(false);
        }
    }

    /// <summary>Verifies that replacing a value changes only the selected cache table.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TableIsolationCases))]
    public async Task Deployment_writes_are_table_scoped(
        string targetName,
        bool useSecondTable
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        var previousStore = await fixture.GetStoreAsync(target).ConfigureAwait(false);
        await using var nextStore = await CreateDeployedStoreAsync(target).ConfigureAwait(false);
        const string key = "deployment-write-isolation";
        var options = new DistributedCacheEntryOptions();
        await previousStore.Cache.SetAsync(key, [1], options, CancellationToken.None).ConfigureAwait(false);
        await nextStore.Cache.SetAsync(key, [2], options, CancellationToken.None).ConfigureAwait(false);
        var selectedStore = useSecondTable ? nextStore : previousStore;

        // Act
        await selectedStore.Cache.SetAsync(key, [3], options, CancellationToken.None).ConfigureAwait(false);
        var previousValue = await previousStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
        var nextValue = await nextStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(previousStore.SchemaName, nextStore.SchemaName);
        Assert.NotEqual(previousStore.TableName, nextStore.TableName);
        Assert.Equal(new byte[] { useSecondTable ? (byte)1 : (byte)3 }, previousValue);
        Assert.Equal(new byte[] { useSecondTable ? (byte)3 : (byte)2 }, nextValue);
    }

    /// <summary>Verifies that refreshing one deployment leaves the other expiration unchanged.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TableIsolationCases))]
    public async Task Deployment_refreshes_are_table_scoped(
        string targetName,
        bool useSecondTable
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        var previousStore = await fixture.GetStoreAsync(target).ConfigureAwait(false);
        await using var nextStore = await CreateDeployedStoreAsync(target).ConfigureAwait(false);
        const string key = "deployment-refresh-isolation";
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10), };

        foreach (var store in new[] { previousStore, nextStore })
        {
            await store.Cache.SetAsync(key, [1], options, CancellationToken.None).ConfigureAwait(false);
            await store.ExecuteForKeyAsync(
                    $"UPDATE {store.QualifiedTableName} "
                    + "SET `ExpiresAtUtc` = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) "
                    + "WHERE `Id` = CAST(@key AS BINARY);", key)
                .ConfigureAwait(false);
        }

        var selectedStore = useSecondTable ? nextStore : previousStore;
        var untouchedStore = useSecondTable ? previousStore : nextStore;
        var before = await selectedStore.ReadEntryAsync(key).ConfigureAwait(false);
        var untouchedBefore = await untouchedStore.ReadEntryAsync(key).ConfigureAwait(false);

        // Act
        await selectedStore.Cache.RefreshAsync(key, CancellationToken.None).ConfigureAwait(false);
        var after = await selectedStore.ReadEntryAsync(key).ConfigureAwait(false);
        var untouchedAfter = await untouchedStore.ReadEntryAsync(key).ConfigureAwait(false);

        // Assert
        Assert.True(after.ExpiresAt > before.ExpiresAt);
        Assert.Equal(untouchedBefore, untouchedAfter);
    }

    /// <summary>Verifies that removing an entry leaves the other deployment's value available.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TableIsolationCases))]
    public async Task Deployment_removals_are_table_scoped(
        string targetName,
        bool useSecondTable
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        var previousStore = await fixture.GetStoreAsync(target).ConfigureAwait(false);
        await using var nextStore = await CreateDeployedStoreAsync(target).ConfigureAwait(false);
        const string key = "deployment-remove-isolation";
        var options = new DistributedCacheEntryOptions();
        await previousStore.Cache.SetAsync(key, [1], options, CancellationToken.None).ConfigureAwait(false);
        await nextStore.Cache.SetAsync(key, [2], options, CancellationToken.None).ConfigureAwait(false);
        var selectedStore = useSecondTable ? nextStore : previousStore;
        var untouchedStore = useSecondTable ? previousStore : nextStore;

        // Act
        await selectedStore.Cache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        var removed = await selectedStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
        var untouched = await untouchedStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Null(removed);
        Assert.Equal(new byte[] { useSecondTable ? (byte)1 : (byte)2 }, untouched);
    }

    /// <summary>Verifies that disposing one deployment preserves reads and writes on another.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Disposing_one_deployment_preserves_other_deployments(
        string targetName
    )
    {
        // Arrange
        var target = ParseTarget(targetName);
        var previousStore = await fixture.GetStoreAsync(target).ConfigureAwait(false);
        const string key = "deployment-disposal-isolation";
        var options = new DistributedCacheEntryOptions();

        // Act
        // WHY: The sibling's complete lifetime is the operation under test; reads
        // and writes must still succeed after its provider and table are disposed.
        await using (var nextStore = await CreateDeployedStoreAsync(target).ConfigureAwait(false))
        {
            await previousStore.Cache.SetAsync(key, [3], options, CancellationToken.None).ConfigureAwait(false);
            await nextStore.Cache.SetAsync(key, [4], options, CancellationToken.None).ConfigureAwait(false);
        }

        var retained = await previousStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);
        await previousStore.Cache.SetAsync(key, [5], options, CancellationToken.None).ConfigureAwait(false);
        var replacement = await previousStore.Cache.GetAsync(key, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Equal(new byte[] { 3 }, retained);
        Assert.Equal(new byte[] { 5 }, replacement);
    }

    /// <summary>Creates an independently deployed table with the common class schema owner.</summary>
    private static async Task<CacheIntegrationStore> CreateDeployedStoreAsync(
        IntegrationDatabaseTarget target
    )
    {
        var store = await CacheIntegrationStore
            .CreateAsync(target, MySqlDistributedCacheFixture.DatabaseOwner)
            .ConfigureAwait(false);

        try
        {
            await store.ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
                .ConfigureAwait(false);

            return store;
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Builds a provider for the separately deployed quoted table.</summary>
    private static ServiceProvider CreateQuotedCacheProvider(
        CacheIntegrationStore store,
        string tableName
    )
    {
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            options.ConnectionString = store.ConnectionString;
            options.SchemaName = store.SchemaName;
            options.TableName = tableName;
        });

        return services.BuildServiceProvider();
    }
}
