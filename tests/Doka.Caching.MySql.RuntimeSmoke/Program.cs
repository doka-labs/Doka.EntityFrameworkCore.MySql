namespace Doka.Caching.MySql.RuntimeSmoke;

internal static class Program
{
    private const string DatabaseName = "runtime_smoke_cache";
    private const string TableName = "entries";

    public static async Task<int> Main(
        string[] arguments
    )
    {
        var connectionString = Environment.GetEnvironmentVariable("DOKA_RUNTIME_SMOKE_CONNECTION_STRING")
            ?? "Server=127.0.0.1;Port=33068;User ID=root;Password=root_password;";

        await VerifyRegistrationAsync(connectionString);
        if (arguments.Contains("--registration-only", StringComparer.Ordinal))
        {
            Console.WriteLine("Cache package registration contract OK.");
            return 0;
        }

        await DeploySchemaAsync(connectionString);
        await VerifyInjectedTimeProviderAsync(connectionString);
        await using var services = CreateServices(connectionString);

        var cache = services.GetRequiredService<IDistributedCache>();
        var bufferCache = services.GetRequiredService<IBufferDistributedCache>();

        VerifySynchronousOperations(cache, bufferCache);
        await VerifyAsynchronousOperationsAsync(cache, bufferCache);
        await VerifyCancellationAsync(cache);
        await VerifyConcurrentOperationsAsync(cache);
        await VerifyExternalDataSourceAsync(connectionString);

        Console.WriteLine(
            "Cache runtime smoke OK (sync, async, buffer, cancellation, concurrency, external pool, time provider).");
        return 0;
    }

    private static ServiceProvider CreateServices(
        string connectionString,
        MySqlDataSource? dataSource = null,
        TimeProvider? timeProvider = null
    )
    {
        var services = new ServiceCollection();
        if (timeProvider is not null)
        {
            services.AddSingleton<TimeProvider>(timeProvider);
        }

        services.AddDistributedMySqlCache(options =>
        {
            options.ConnectionString = dataSource is null ? connectionString : string.Empty;
            options.DataSource = dataSource;
            options.SchemaName = DatabaseName;
            options.TableName = TableName;
            if (timeProvider is not null)
            {
                options.ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5);
            }
        });
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    private static async Task VerifyRegistrationAsync(
        string connectionString
    )
    {
        await using var services = CreateServices(connectionString);
        services
            .GetRequiredService<IStartupValidator>()
            .Validate();
        if (!ReferenceEquals(
                services.GetRequiredService<IDistributedCache>(),
                services.GetRequiredService<IBufferDistributedCache>()))
        {
            throw new InvalidOperationException("The cache interfaces did not resolve to the same singleton.");
        }

        await using var dataSource = CreateDataSource(connectionString);
        await using var externalServices = CreateServices(connectionString, dataSource);
        externalServices
            .GetRequiredService<IStartupValidator>()
            .Validate();

        Require(
            ReferenceEquals(
                externalServices.GetRequiredService<IDistributedCache>(),
                externalServices.GetRequiredService<IBufferDistributedCache>()),
            "An external data source changed the shared singleton registration.");

        var invalidServices = new ServiceCollection();
        invalidServices.AddDistributedMySqlCache(_ => { });
        await using var invalidProvider = invalidServices.BuildServiceProvider();
        try
        {
            invalidProvider
                .GetRequiredService<IStartupValidator>()
                .Validate();
        }
        catch (OptionsValidationException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid cache options passed startup validation.");
    }

    private static async Task VerifyInjectedTimeProviderAsync(
        string connectionString
    )
    {
        var time = new ManualTimeProvider();
        await using var services = CreateServices(connectionString, timeProvider: time);
        var cache = services.GetRequiredService<IDistributedCache>();

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             DELETE FROM `{DatabaseName}`.`{TableName}`;
             INSERT INTO `{DatabaseName}`.`{TableName}`
                 (`Id`, `Value`, `ExpiresAtUtc`, `SlidingExpirationMicroseconds`, `AbsoluteExpirationUtc`, `Revision`)
             VALUES
                 ('runtime-clock-renewed', X'01', TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6)), NULL, NULL, 1),
                 ('runtime-clock-expired', X'01', TIMESTAMPADD(SECOND, -1, UTC_TIMESTAMP(6)), NULL, NULL, 2);
             """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);

        try
        {
            await cache.SetAsync(
                "runtime-clock-renewed",
                [2],
                new DistributedCacheEntryOptions(),
                CancellationToken.None);
            await cache.SetAsync(
                "runtime-clock-live",
                [3],
                new DistributedCacheEntryOptions(),
                CancellationToken.None);

            command.CommandText =
                $"SELECT COUNT(*) FROM `{DatabaseName}`.`{TableName}` WHERE `ExpiresAtUtc` <= UTC_TIMESTAMP(6);";
            Require(
                Convert.ToInt64(
                    await command.ExecuteScalarAsync(CancellationToken.None),
                    CultureInfo.InvariantCulture) == 1,
                "The cleanup clock fixture did not contain exactly one expired row.");

            time.Advance(TimeSpan.FromMinutes(5));
            Require(
                await cache.GetAsync("runtime-clock-miss", CancellationToken.None) is null,
                "The cleanup trigger unexpectedly returned a value.");

            Require(
                Convert.ToInt64(
                    await command.ExecuteScalarAsync(CancellationToken.None),
                    CultureInfo.InvariantCulture) == 0,
                "The DI-provided time source did not trigger expired-row cleanup.");
            Require(
                (await cache.GetAsync("runtime-clock-renewed", CancellationToken.None))
                .AsSpan()
                .SequenceEqual(new byte[] { 2 }),
                "Cleanup deleted the renewed entry.");
            Require(
                (await cache.GetAsync("runtime-clock-live", CancellationToken.None))
                .AsSpan()
                .SequenceEqual(new byte[] { 3 }),
                "Cleanup deleted the live entry.");
        }
        finally
        {
            command.CommandText = $"DELETE FROM `{DatabaseName}`.`{TableName}`;";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task DeploySchemaAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var createDatabase = connection.CreateCommand();
        createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS `{DatabaseName}`;";
        await createDatabase.ExecuteNonQueryAsync(CancellationToken.None);
        await using var createTable = connection.CreateCommand();
        createTable.CommandText = MySqlCacheSchema.GetCreateScript(DatabaseName, TableName);
        await createTable.ExecuteNonQueryAsync(CancellationToken.None);
        await createTable.ExecuteNonQueryAsync(CancellationToken.None);
    }

    // WHY: The runtime probe verifies the synchronous public API as well as the asynchronous API.
    // ReSharper disable MethodHasAsyncOverload
    private static void VerifySynchronousOperations(
        IDistributedCache cache,
        IBufferDistributedCache bufferCache
    )
    {
        const string key = "runtime-sync";
        byte[] expected = [1, 2, 3, 4];
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
        };
        cache.Remove(key);
        Require(cache.Get(key) is null, "A missing cache entry returned data.");
        var buffer = new ArrayBufferWriter<byte>();
        Require(!bufferCache.TryGet(key, buffer) && buffer.WrittenCount == 0, "A cache miss wrote to the buffer.");

        cache.Set(key, expected, options);
        Require(
            cache
                .Get(key)
                .AsSpan()
                .SequenceEqual(expected),
            "Synchronous cache data did not round-trip.");

        cache.Refresh(key);
        Require(bufferCache.TryGet(key, buffer), "The buffer cache could not read the byte-array entry.");
        Require(buffer.WrittenSpan.SequenceEqual(expected), "The buffer cache changed the byte-array entry.");

        bufferCache.Set(key, new ReadOnlySequence<byte>(ReadOnlyMemory<byte>.Empty), options);
        Require(cache.Get(key) is { Length: 0 }, "An empty cache entry became a miss.");
        cache.Remove(key);
        cache.Refresh(key);
        Require(cache.Get(key) is null, "Refresh resurrected a removed entry.");
    }
    // ReSharper restore MethodHasAsyncOverload

    private static async Task VerifyAsynchronousOperationsAsync(
        IDistributedCache cache,
        IBufferDistributedCache bufferCache
    )
    {
        const string key = "runtime-async";
        var expected = new byte[32 * 1024];
        for (var index = 0; index < expected.Length; index++)
        {
            expected[index] = (byte)(index % 251);
        }

        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
        };

        await cache.SetAsync(key, expected, options, CancellationToken.None);
        Require(
            (await cache.GetAsync(key, CancellationToken.None))
            .AsSpan()
            .SequenceEqual(expected),
            "Asynchronous data did not round-trip.");

        await cache.RefreshAsync(key, CancellationToken.None);

        var first = new BufferSegment(expected.AsMemory(0, 13));
        var last = first.Append(expected.AsMemory(13));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        await bufferCache.SetAsync(key, sequence, options, CancellationToken.None);
        var buffer = new ArrayBufferWriter<byte>(expected.Length);
        Require(
            await bufferCache.TryGetAsync(key, buffer, CancellationToken.None),
            "The segmented buffer entry was missing.");
        Require(buffer.WrittenSpan.SequenceEqual(expected), "The segmented buffer entry was changed.");
        await cache.RemoveAsync(key, CancellationToken.None);
        await cache.RefreshAsync(key, CancellationToken.None);
        buffer.Clear();
        Require(
            !await bufferCache.TryGetAsync(key, buffer, CancellationToken.None) && buffer.WrittenCount == 0,
            "An asynchronous cache miss wrote to the buffer.");
    }

    private static async Task VerifyCancellationAsync(
        IDistributedCache cache
    )
    {
        const string key = "runtime-canceled";
        await cache.RemoveAsync(key, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await cache.SetAsync(key, [7], new DistributedCacheEntryOptions(), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Require(
                await cache.GetAsync(key, CancellationToken.None) is null,
                "A pre-canceled write modified the cache.");
            return;
        }

        throw new InvalidOperationException("A pre-canceled cache operation did not honor cancellation.");
    }

    private static async Task VerifyConcurrentOperationsAsync(
        IDistributedCache cache
    )
    {
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async index =>
                {
                    var key = $"runtime-parallel-{index}";
                    var value = new byte[] { (byte)index, };
                    await cache.SetAsync(key, value, new DistributedCacheEntryOptions(), CancellationToken.None);
                    await cache.RefreshAsync(key, CancellationToken.None);
                    Require(
                        (await cache.GetAsync(key, CancellationToken.None))
                        .AsSpan()
                        .SequenceEqual(value),
                        "Concurrent cache data changed.");
                    await cache.RemoveAsync(key, CancellationToken.None);
                }));
    }

    private static MySqlDataSource CreateDataSource(
        string connectionString
    ) => new MySqlDataSourceBuilder(
        new MySqlConnectionStringBuilder(connectionString)
        {
            AutoEnlist = false,
            MaximumPoolSize = 1,
        }
        .ConnectionString)
        .Build();

    private static async Task VerifyExternalDataSourceAsync(
        string connectionString
    )
    {
        await using var dataSource = CreateDataSource(connectionString);
        await using (var services = CreateServices(connectionString, dataSource))
        {
            await VerifyAsynchronousOperationsAsync(
                services.GetRequiredService<IDistributedCache>(),
                services.GetRequiredService<IBufferDistributedCache>());
        }

        await using var connection = await dataSource
            .OpenConnectionAsync(CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        Require(
            Convert.ToInt32(
                await command.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture) == 1,
            "Disposing the cache disposed its caller-owned data source.");
    }

    private static void Require(
        bool condition,
        string message
    )
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(
            ReadOnlyMemory<byte> memory
        )
        {
            Memory = memory;
        }

        public BufferSegment Append(
            ReadOnlyMemory<byte> memory
        )
        {
            var next = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = next;
            return next;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(
            TimeSpan elapsed
        ) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
