namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies bounded cache cleanup against the selected database engines.</summary>
[IntegrationDatabaseTest]
public sealed class MySqlCacheCleanupIntegrationTests
{
    /// <summary>Reports each selected engine for the public DI cleanup contract.</summary>
    public static TheoryData<string> SelectedTargets => CreateSelectedTargets();

    /// <summary>Reports each selected engine and cleanup-concurrency variant.</summary>
    public static TheoryData<string, bool, int, bool> CleanupCases => CreateCleanupCases();

    /// <summary>Reports each selected engine and binary-key access mode.</summary>
    public static TheoryData<string, bool> BinaryKeyCases => CreateBinaryKeyCases();

    /// <summary>Cleanup keeps binary candidate keys as binary SQL parameters.</summary>
    [Fact]
    public void Cleanup_command_preserves_binary_candidate_keys()
    {
        // Arrange
        var sql = new MySqlCacheSql("`DokaProbe`");
        using var command = new MySqlCommand();
        byte[] candidate = [0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD];

        // Act
        sql.ConfigureDeleteExpiredCommand(command, [candidate]);

        // Assert
        Assert.Contains("FORCE INDEX (PRIMARY)", command.CommandText, StringComparison.Ordinal);

        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(MySqlDbType.VarBinary, parameter.MySqlDbType);
        Assert.Equal(candidate, Assert.IsType<byte[]>(parameter.Value));
    }

    /// <summary>Verifies that DI forwards the cleanup clock on each selected engine.</summary>
    [Theory]
    [MemberData(nameof(SelectedTargets))]
    public Task Registered_time_provider_controls_public_DI_cleanup_schedule(
        string targetName
    ) => AssertPublicDiCleanupScheduleAsync(Enum.Parse<IntegrationDatabaseTarget>(targetName));

    /// <summary>Verifies bounded primary-key cleanup with and without overlapping upserts.</summary>
    [Theory]
    [MemberData(nameof(CleanupCases))]
    public Task Cleanup_uses_primary_ranges_preserves_upserts_and_continues_full_batches(
        string targetName,
        bool useAsync,
        int expiredCount,
        bool overlappingCleanup
    ) => AssertConcurrentUpsertAsync(
        Enum.Parse<IntegrationDatabaseTarget>(targetName),
        useAsync,
        expiredCount,
        overlappingCleanup);

    /// <summary>Verifies that cleanup deletes expired binary keys only.</summary>
    [Theory]
    [MemberData(nameof(BinaryKeyCases))]
    public Task Cleanup_deletes_expired_binary_keys_without_touching_live_entries(
        string targetName,
        bool useAsync
    ) => AssertBinaryKeysAsync(Enum.Parse<IntegrationDatabaseTarget>(targetName), useAsync);

    private static TheoryData<string> CreateSelectedTargets()
    {
        var cases = new TheoryData<string>();

        // WHY: Public xUnit theory methods cannot expose the internal target enum.
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            cases.Add(target.ToString());
        }

        return cases;
    }

    private static TheoryData<string, bool, int, bool> CreateCleanupCases()
    {
        var cases = new TheoryData<string, bool, int, bool>();

        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            cases.Add(target.ToString(), false, 2, false);
            cases.Add(target.ToString(), true, 2, false);
            cases.Add(target.ToString(), false, 1005, false);
            cases.Add(target.ToString(), true, 1005, false);
            cases.Add(target.ToString(), false, 1005, true);
            cases.Add(target.ToString(), true, 1005, true);
        }

        return cases;
    }

    private static TheoryData<string, bool> CreateBinaryKeyCases()
    {
        var cases = new TheoryData<string, bool>();

        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            cases.Add(target.ToString(), false);
            cases.Add(target.ToString(), true);
        }

        return cases;
    }

    private static async Task AssertBinaryKeysAsync(
        IntegrationDatabaseTarget target,
        bool useAsync
    )
    {
        await using var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        await store
            .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
            .ConfigureAwait(false);

        string[] keys =
        [
            "Key",
            "key",
            "key ",
            "key' OR 1=1 --",
            "key\\slash",
            "key\0",
            "\uE000",
            "\U00010000",
            new string('k', MySqlCacheDatabaseOperations.MaximumKeyByteLength),
        ];

        for (var index = 0; index < keys.Length; index++)
        {
            await store
                .Cache
                .SetAsync(
                    keys[index],
                    [1],
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (index % 2 == 0)
            {
                await store
                    .ExecuteForKeyAsync(
                        $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6)) "
                        + "WHERE `Id` = CAST(@key AS BINARY);",
                        keys[index])
                    .ConfigureAwait(false);
            }
        }

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        await using var cache = store.CreateCache(time, logger);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(
            await RunCleanupAsync(cache, useAsync, CancellationToken.None)
                .ConfigureAwait(false));

        for (var index = 0; index < keys.Length; index++)
        {
            Assert.Equal(
                index % 2 == 0 ? 0L : 1L,
                await store
                    .CountKeyAsync(keys[index])
                    .ConfigureAwait(false));
        }

        await store
            .ExecuteAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6));")
            .ConfigureAwait(false);

        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(
            await RunCleanupAsync(cache, useAsync, CancellationToken.None)
                .ConfigureAwait(false));

        foreach (var key in keys)
        {
            Assert.Equal(
                0L,
                await store
                    .CountKeyAsync(key)
                    .ConfigureAwait(false));
        }

        Assert.Empty(logger.Entries);
    }

    private static async Task AssertPublicDiCleanupScheduleAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        await store
            .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(1006)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var cacheConnectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            Database = string.Empty,
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddDistributedMySqlCache(options =>
        {
            options.ConnectionString = cacheConnectionString;
            options.SchemaName = store.SchemaName;
            options.TableName = store.TableName;
            options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5);
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IBufferDistributedCache>();

        await cache
            .SetAsync(
                "live",
                [1],
                new DistributedCacheEntryOptions(),
                CancellationToken.None)
            .ConfigureAwait(false);

        await cache
            .SetAsync(
                "expired-0",
                [2],
                new DistributedCacheEntryOptions(),
                CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            1005L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        time.Advance(TimeSpan.FromMinutes(5));

        // WHY: The public synchronous contract must consume the first bounded cleanup batch.
        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(cache.Get("cleanup-miss"));

        Assert.Equal(
            5L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Null(
            await cache
                .GetAsync("cleanup-miss", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 1 },
            await cache
                .GetAsync("live", CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 2 },
            await cache
                .GetAsync("expired-0", CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task AssertConcurrentUpsertAsync(
        IntegrationDatabaseTarget target,
        bool useAsync,
        int expiredCount,
        bool overlappingCleanup
    )
    {
        await using var store = await CacheIntegrationStore
            .CreateAsync(target)
            .ConfigureAwait(false);

        await store
            .ExecuteAsync(MySqlCacheSchema.GetCreateScript(store.SchemaName, store.TableName))
            .ConfigureAwait(false);

        await store
            .InsertExpiredEntriesAsync(expiredCount)
            .ConfigureAwait(false);

        const string key = "expired-0";

        await store
            .ExecuteForKeyAsync(
                $"UPDATE {store.QualifiedTableName} SET `ExpiresAtUtc` = TIMESTAMPADD(SECOND, -2, UTC_TIMESTAMP(6)) "
                + "WHERE `Id` = CAST(@key AS BINARY);",
                key)
            .ConfigureAwait(false);

        var time = new CacheManualTimeProvider();
        var logger = new CacheRecordingLogger();
        var otherLogger = new CacheRecordingLogger();
        var connectionString = new MySqlConnectionStringBuilder(store.ConnectionString)
        {
            DefaultCommandTimeout = 10,
        }.ConnectionString;

        await using var cache = store.CreateCache(time, logger, connectionString);
        await using var otherCache = overlappingCleanup ? store.CreateCache(time, otherLogger, connectionString) : null;
        await using var writer = new MySqlConnection(connectionString);
        await using var observer = new MySqlConnection(connectionString);

        await writer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await observer
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var transaction = await writer
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, CancellationToken.None)
            .ConfigureAwait(false);

        await using (var locked = new MySqlCommand(
                         $"SELECT `Id` FROM {store.QualifiedTableName} WHERE `Id` = CAST(@key AS BINARY) FOR UPDATE;",
                         writer,
                         transaction))
        {
            locked.Parameters.AddWithValue("@key", key);

            Assert.NotNull(
                await locked
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false));
        }

        time.Advance(TimeSpan.FromMinutes(5));
        using var cancellation = new CancellationTokenSource();
        Task pending = otherCache is null
            ? RunCleanupAsync(cache, useAsync, cancellation.Token)
            : Task.WhenAll(
                RunCleanupAsync(cache, useAsync, cancellation.Token),
                RunCleanupAsync(otherCache, useAsync, cancellation.Token));

        try
        {
            await AssertCleanupWaitsForRowAsync(
                    observer,
                    target,
                    store.TableName,
                    pending,
                    otherCache is null ? 1 : 2)
                .ConfigureAwait(false);

            await AssertPrimaryRangePlanAsync(observer, store)
                .ConfigureAwait(false);

            // Use the real upsert while holding its primary record; cleanup must not own the expiry entry first.
            await using var upsert = new MySqlCommand(
                new MySqlCacheSql(store.QualifiedTableName).Set,
                writer,
                transaction);
            upsert.Parameters.AddWithValue("@key", key);
            upsert.Parameters.Add("@value", MySqlDbType.LongBlob).Value = new byte[] { 2 };
            upsert.Parameters.Add("@absoluteExpirationUtc", MySqlDbType.DateTime).Value = DBNull.Value;
            upsert.Parameters.Add("@absoluteExpirationRelativeMicroseconds", MySqlDbType.Int64).Value = 60_000_000L;
            upsert.Parameters.Add("@slidingExpirationMicroseconds", MySqlDbType.Int64).Value = DBNull.Value;
            upsert.Parameters.Add("@revision", MySqlDbType.Int64).Value = 42L;

            await upsert
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await pending
                .WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await cancellation
                    .CancelAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await transaction
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    // Observe the owned task to completion; the synchronous path is bounded by its command timeout.
                    await pending.ConfigureAwait(false);
                }
            }
        }

        Assert.Empty(logger.Entries);
        Assert.Empty(otherLogger.Entries);

        Assert.Equal(
            Math.Max(0, expiredCount - MySqlCacheDatabaseOperations.CleanupBatchSize),
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        await RunCleanupAsync(otherCache ?? cache, useAsync, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(
            0L,
            await store
                .CountExpiredAsync()
                .ConfigureAwait(false));

        Assert.Equal(
            new byte[] { 2 },
            await cache
                .GetAsync(key, CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static Task<byte[]?> RunCleanupAsync(
        MySqlDistributedCache cache,
        bool useAsync,
        CancellationToken cancellationToken
    )
    {
        return useAsync
            ? cache.GetAsync("cleanup-miss", cancellationToken)
            // WHY: The synchronous cleanup needs its own thread while the test coordinates a database lock.
            // ReSharper disable once MethodHasAsyncOverload
            : Task.Run(() => cache.Get("cleanup-miss"), CancellationToken.None);
    }

    private static async Task AssertCleanupWaitsForRowAsync(
        MySqlConnection observer,
        IntegrationDatabaseTarget target,
        string tableName,
        Task pending,
        int expectedWaiters
    )
    {
        // WHY: MySQL exposes live lock relationships in Performance Schema;
        // MariaDB uses the INNODB_TRX view for this test contract.
        var lockProbe = target is IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MySql97
            ? """
              SELECT COUNT(DISTINCT waits.REQUESTING_THREAD_ID)
              FROM performance_schema.data_lock_waits AS waits
              JOIN performance_schema.data_locks AS locks
                  ON locks.ENGINE = waits.ENGINE
                  AND locks.ENGINE_LOCK_ID = waits.REQUESTING_ENGINE_LOCK_ID
              WHERE locks.OBJECT_SCHEMA = DATABASE()
                  AND locks.OBJECT_NAME = @tableName
                  AND locks.LOCK_TYPE = 'RECORD';
              """
            : """
              SELECT COUNT(*) FROM information_schema.INNODB_TRX
              WHERE TRX_STATE = 'LOCK WAIT'
                  AND LOCATE(@tableName, COALESCE(CONVERT(CAST(TRX_QUERY AS BINARY) USING latin1), '')) > 0
                  AND UPPER(LTRIM(CONVERT(CAST(TRX_QUERY AS BINARY) USING latin1))) LIKE 'DELETE %';
              """;

        await using var command = new MySqlCommand(
            lockProbe,
            observer);

        command.Parameters.AddWithValue("@tableName", tableName);
        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (pending.IsFaulted)
            {
                await pending.ConfigureAwait(false);
            }

            Assert.False(pending.IsCompleted, "Cleanup completed before reaching the locked primary record.");

            var waiting = Convert.ToInt64(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (waiting == expectedWaiters)
            {
                return;
            }

            // InnoDB's transaction snapshot needs more than 100 ms without a read before it can refresh.
            await Task
                .Delay(200, CancellationToken.None)
                .ConfigureAwait(false);
        }

        command.CommandText = """
                              SELECT GROUP_CONCAT(CONCAT_WS(' | ', p.ID, p.STATE, t.TRX_STATE, t.TRX_OPERATION_STATE,
                                  CONVERT(CAST(p.INFO AS BINARY) USING latin1))
                                  SEPARATOR '\n')
                              FROM information_schema.PROCESSLIST AS p
                              LEFT JOIN information_schema.INNODB_TRX AS t ON t.TRX_MYSQL_THREAD_ID = p.ID
                              WHERE p.ID <> CONNECTION_ID()
                                  AND LOCATE(@tableName, COALESCE(CONVERT(CAST(p.INFO AS BINARY) USING latin1), '')) > 0;
                              """;

        var diagnostics = await command
            .ExecuteScalarAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Fail($"Cleanup did not enter a row-lock wait within the test deadline. Active statements: {diagnostics}");
    }

    private static async Task AssertPrimaryRangePlanAsync(
        MySqlConnection observer,
        CacheIntegrationStore store
    )
    {
        var sql = new MySqlCacheSql(store.QualifiedTableName);
        List<byte[]> candidates = [];

        // WHY: Process-list SQL is not reliable here: MySQL 9.7 can reject an
        // unrelated binary statement, and Performance Schema truncates large
        // cleanup batches. Build the exact command through production SQL instead.
        await using (var select = new MySqlCommand(sql.SelectExpired, observer))
        {
            select.Parameters.Add("@batchSize", MySqlDbType.Int32).Value = MySqlCacheDatabaseOperations.CleanupBatchSize;

            await using var candidateReader = await select
                .ExecuteReaderAsync(CommandBehavior.SequentialAccess, CancellationToken.None)
                .ConfigureAwait(false);

            while (await candidateReader
                       .ReadAsync(CancellationToken.None)
                       .ConfigureAwait(false))
            {
                candidates.Add(candidateReader.GetFieldValue<byte[]>(0));
            }
        }

        Assert.NotEmpty(candidates);

        await using var command = new MySqlCommand { Connection = observer };
        sql.ConfigureDeleteExpiredCommand(command, candidates);
        command.CommandText = "EXPLAIN FORMAT=TRADITIONAL " + command.CommandText;

        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(
            await reader
                .ReadAsync(CancellationToken.None)
                .ConfigureAwait(false));

        Assert.Equal("PRIMARY", reader.GetString("key"));
        Assert.Equal("range", reader.GetString("type"));

        Assert.False(
            await reader
                .ReadAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

}
