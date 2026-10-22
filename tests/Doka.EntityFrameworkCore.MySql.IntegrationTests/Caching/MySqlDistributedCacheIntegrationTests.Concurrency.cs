namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies concurrent cache reads and writes.</summary>
public sealed partial class MySqlDistributedCacheIntegrationTests
{
    /// <summary>Verifies that concurrent upserts preserve whole values.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Concurrent_upserts_preserve_whole_values(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var concurrentConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            // WHY: The upsert race must not depend on the shared fixture's connection-pool limit.
            MaximumPoolSize = ConcurrentUpsertProbeCount + 1,
        }.ConnectionString;

        await using var cache = store.CreateCache(TimeProvider.System, connectionString: concurrentConnectionString);

        // Act
        var writes = Enumerable
            .Range(1, ConcurrentUpsertProbeCount)
            .Select(async marker =>
            {
                var value = new byte[64 * 1024];

                value
                    .AsSpan()
                    .Fill((byte)marker);

                await cache
                    .SetAsync("concurrent", value, new DistributedCacheEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);

                var observed = await cache
                    .GetAsync("concurrent", CancellationToken.None)
                    .ConfigureAwait(false);

                return observed;
            });

        var observations = await Task
            .WhenAll(writes)
            .ConfigureAwait(false);

        // Assert
        Assert.All(observations, observed =>
        {
            Assert.NotNull(observed);
            Assert.Equal(64 * 1024, observed.Length);
            Assert.InRange(observed[0], (byte)1, (byte)ConcurrentUpsertProbeCount);
            Assert.True(observed.AsSpan().IndexOfAnyExcept(observed[0]) < 0);
        });

        Assert.Equal(
            1L,
            await store
                .CountKeyAsync("concurrent")
                .ConfigureAwait(false));
    }

    /// <summary>Verifies removal wins a race with refresh without resurrecting the entry.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Concurrent_remove_and_refresh_do_not_resurrect_entries(
        string targetName
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var concurrentConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            MaximumPoolSize = ConcurrentUpsertProbeCount + 1,
        }.ConnectionString;

        await using var cache = store.CreateCache(TimeProvider.System, connectionString: concurrentConnectionString);
        var observations = new byte[]?[8];

        // Act
        // WHY: Repeating the race on one cache instance exercises different scheduler
        // orderings and stale state carried across removal/reinsertion cycles.
        for (var iteration = 0; iteration < observations.Length; iteration++)
        {
            await cache
                .SetAsync("remove-race", [1], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            await Task
                .WhenAll(
                    cache.RefreshAsync("remove-race", CancellationToken.None),
                    cache.RemoveAsync("remove-race", CancellationToken.None))
                .ConfigureAwait(false);

            observations[iteration] = await cache
                .GetAsync("remove-race", CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Assert
        for (var iteration = 0; iteration < observations.Length; iteration++)
        {
            Assert.True(observations[iteration] is null, $"Race iteration {iteration} resurrected the removed entry.");
        }
    }

    /// <summary>Verifies either buffer-read API against concurrent replacement.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Buffer_reads_preserve_snapshot_during_replacement(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var key = $"read-replacement-race-{useAsync}";

        await store
            .Cache
            .SetAsync(key, [1, 2], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        using var paused = new PausingBufferWriter(2);
        // Act
        var reading = Task.Run(
            async () => useAsync
                ? await store
                    .Cache
                    .TryGetAsync(key, paused, CancellationToken.None)
                    .ConfigureAwait(false)
                // WHY: The false variant holds synchronous TryGet across the replacement race.
                // ReSharper disable once MethodHasAsyncOverload
                : store.Cache.TryGet(key, paused),
            CancellationToken.None);

        CacheIntegrationStore.EntryState replacement;

        try
        {
            await paused
                .ReadStarted
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);

            await store
                .Cache
                .SetAsync(key, [3, 4], new DistributedCacheEntryOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            await store
                .ExecuteForKeyAsync(
                    $"UPDATE {store.QualifiedTableName} "
                    + "SET `ExpiresAtUtc` = TIMESTAMPADD(MINUTE, 1, UTC_TIMESTAMP(6)) "
                    + "WHERE `Id` = CAST(@key AS BINARY);",
                    key)
                .ConfigureAwait(false);

            replacement = await store
                .ReadEntryAsync(key)
                .ConfigureAwait(false);
        }
        finally
        {
            paused.Release();
        }

        // Assert
        Assert.True(
            await reading
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(new byte[] { 1, 2 }, paused.WrittenBytes);

        Assert.Equal(
            replacement,
            await store
                .ReadEntryAsync(key)
                .ConfigureAwait(false));
    }

    /// <summary>Verifies either buffer-read API against concurrent removal.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SyncAsyncCases))]
    public async Task Buffer_reads_preserve_snapshot_during_removal(
        string targetName,
        bool useAsync
    )
    {
        // Arrange
        var store = await fixture
            .GetStoreAsync(ParseTarget(targetName))
            .ConfigureAwait(false);

        var key = $"read-removal-race-{useAsync}";

        await store
            .Cache
            .SetAsync(key, [3, 4], new DistributedCacheEntryOptions(), CancellationToken.None)
            .ConfigureAwait(false);

        using var paused = new PausingBufferWriter(2);

        // Act
        var reading = Task.Run(
            async () => useAsync
                ? await store
                    .Cache
                    .TryGetAsync(key, paused, CancellationToken.None)
                    .ConfigureAwait(false)
                // WHY: The false variant holds synchronous TryGet across the removal race.
                // ReSharper disable once MethodHasAsyncOverload
                : store.Cache.TryGet(key, paused),
            CancellationToken.None);

        try
        {
            await paused
                .ReadStarted
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);

            await store
                .Cache
                .RemoveAsync(key, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            paused.Release();
        }

        // Assert
        Assert.True(
            await reading
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(new byte[] { 3, 4 }, paused.WrittenBytes);

        Assert.Null(
            await store
                .Cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }
}
