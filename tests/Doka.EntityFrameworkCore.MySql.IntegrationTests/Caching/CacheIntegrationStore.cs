using System.Runtime.CompilerServices;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal sealed class CacheIntegrationStore : IAsyncDisposable
{
    private readonly MySqlConnection _connection;
    private readonly ServiceProvider _provider;

    private CacheIntegrationStore(
        string connectionString,
        string schemaName,
        string tableName
    )
    {
        ConnectionString = connectionString;
        SchemaName = schemaName;
        TableName = tableName;
        QualifiedTableName = MySqlCacheIdentifier.GetQualifiedName(schemaName, tableName);
        _connection = new MySqlConnection(connectionString);
        var services = new ServiceCollection();
        services.AddDistributedMySqlCache(options =>
        {
            options.ConnectionString = connectionString;
            options.SchemaName = schemaName;
            options.TableName = tableName;
        });
        _provider = services.BuildServiceProvider();
        Cache = _provider.GetRequiredService<IBufferDistributedCache>();
        Assert.Same(Cache, _provider.GetRequiredService<IDistributedCache>());
    }

    public string ConnectionString { get; }
    public string SchemaName { get; }
    public string TableName { get; }
    public string QualifiedTableName { get; }
    public IBufferDistributedCache Cache { get; }

    /// <summary>Creates an isolated cache store for the selected database target.</summary>
    /// <param name="target">The database target used for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    /// <param name="ownerFilePath">The source file that owns the isolated database.</param>
    public static async Task<CacheIntegrationStore> CreateAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken,
        [CallerFilePath] string ownerFilePath = ""
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // WHY: Forward the test-class source path so both cache test classes
        // retain separate databases even though connection creation is shared.
        var builder = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target, ownerFilePath))
        {
            DateTimeKind = MySqlDateTimeKind.Unspecified,
            MaximumPoolSize = 16,
        };

        var store = new CacheIntegrationStore(
            builder.ConnectionString,
            builder.Database,
            "doka_cache_"
            + Guid
                .NewGuid()
                .ToString("N"));

        try
        {
            await store
                ._connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            return store;
        }
        catch
        {
            await store
                ._provider
                .DisposeAsync()
                .ConfigureAwait(false);

            await store
                ._connection
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // WHY: Disposal must remove the cache table and release owned resources after a cancelled test.
        await _provider
            .DisposeAsync()
            .ConfigureAwait(false);
        try
        {
            await ExecuteAsync($"DROP TABLE IF EXISTS {QualifiedTableName};", CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _connection
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    public MySqlDistributedCache CreateCache(
        TimeProvider timeProvider,
        CacheRecordingLogger? logger = null,
        string? connectionString = null
    ) => new(
        Options.Create(
            new MySqlCacheOptions
            {
                ConnectionString = connectionString ?? ConnectionString,
                SchemaName = SchemaName,
                TableName = TableName,
                ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5),
            }),
        logger ?? new CacheRecordingLogger(),
        timeProvider);

    public MySqlDistributedCache CreateCache(
        TimeProvider timeProvider,
        MySqlDataSource dataSource,
        CacheRecordingLogger? logger = null
    ) => new(
        Options.Create(
            new MySqlCacheOptions
            {
                DataSource = dataSource,
                SchemaName = SchemaName,
                TableName = TableName,
                ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(5),
            }),
        logger ?? new CacheRecordingLogger(),
        timeProvider);

    /// <summary>Executes a SQL statement on the store's open connection.</summary>
    /// <param name="commandText">The SQL statement to execute.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(commandText, _connection);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Executes a SQL statement with a bound cache key.</summary>
    /// <param name="commandText">The SQL statement to execute.</param>
    /// <param name="key">The cache key for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task ExecuteForKeyAsync(
        string commandText,
        string key,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(commandText, _connection);
        command.Parameters.AddWithValue("@key", key);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the database server's current UTC timestamp.</summary>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<DateTime> ReadUtcNowAsync(
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand("SELECT UTC_TIMESTAMP(6);", _connection);
        var value = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Assert.IsType<DateTime>(value);
    }

    /// <summary>Reads persisted expiration metadata for one cache key.</summary>
    /// <param name="key">The cache key for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<EntryState> ReadEntryAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(
            $"""
             SELECT `ExpiresAtUtc`, `AbsoluteExpirationUtc`, `SlidingExpirationMicroseconds`, `Revision`
             FROM {QualifiedTableName} WHERE `Id` = CAST(@key AS BINARY);
             """,
            _connection);

        command.Parameters.AddWithValue("@key", key);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        Assert.True(
            await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false));

        return new EntryState(
            reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.GetInt64(3));
    }

    /// <summary>Counts the persisted rows for one cache key.</summary>
    /// <param name="key">The cache key for this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<long> CountKeyAsync(
        string key,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(
            $"SELECT COUNT(*) FROM {QualifiedTableName} WHERE `Id` = CAST(@key AS BINARY);",
            _connection);

        command.Parameters.AddWithValue("@key", key);

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Counts the store table in database metadata.</summary>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<long> CountTablesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var command = CreateTableMetadataCommand("COUNT(*)");

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the comment attached to the store table.</summary>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<string> ReadTableCommentAsync(
        CancellationToken cancellationToken
    )
    {
        await using var command = CreateTableMetadataCommand("TABLE_COMMENT");

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>Counts rows whose expiration has passed on the server.</summary>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public async Task<long> CountExpiredAsync(
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(
            $"SELECT COUNT(*) FROM {QualifiedTableName} WHERE `ExpiresAtUtc` <= UTC_TIMESTAMP(6);",
            _connection);

        return Convert.ToInt64(
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Adds expired rows for the cleanup workload.</summary>
    /// <param name="count">The count used by this operation.</param>
    /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
    public Task InsertExpiredEntriesAsync(
        int count,
        CancellationToken cancellationToken
    )
    {
        var values = string.Join(
            ",",
            Enumerable
                .Range(0, count)
                .Select(index =>
                    $"('expired-{index}',X'01',TIMESTAMPADD(SECOND,-1,UTC_TIMESTAMP(6)),NULL,NULL,{index})"));

        return ExecuteAsync(
            $"""
             INSERT INTO {QualifiedTableName}
                 (`Id`, `Value`, `ExpiresAtUtc`, `SlidingExpirationMicroseconds`, `AbsoluteExpirationUtc`, `Revision`)
             VALUES {values};
             """,
            cancellationToken);
    }

    private MySqlCommand CreateTableMetadataCommand(
        string selection
    )
    {
        var command = new MySqlCommand(
            $"""
             SELECT {selection} FROM information_schema.TABLES
             WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table;
             """,
            _connection);

        command.Parameters.AddWithValue("@schema", SchemaName);
        command.Parameters.AddWithValue("@table", TableName);

        return command;
    }

    internal readonly record struct EntryState(
        DateTime ExpiresAt,
        DateTime? AbsoluteExpiration,
        long? SlidingMicroseconds,
        long Revision
    );
}

internal class ExactBufferWriter : IBufferWriter<byte>
{
    private readonly byte[] _buffer;

    public ExactBufferWriter(
        int capacity
    )
    {
        _buffer = new byte[capacity];
    }

    public int WrittenCount { get; private set; }
    public int BufferRequests { get; private set; }

    public byte[] WrittenBytes =>
        _buffer
            .AsSpan(0, WrittenCount)
            .ToArray();

    public void Advance(
        int count
    )
    {
        Assert.InRange(count, 0, _buffer.Length - WrittenCount);
        WrittenCount += count;
    }

    public virtual Memory<byte> GetMemory(
        int sizeHint = 0
    )
    {
        BufferRequests++;
        Assert.InRange(sizeHint, 1, _buffer.Length - WrittenCount);

        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(
        int sizeHint = 0
    ) => GetMemory(sizeHint)
        .Span;
}

internal sealed class PausingBufferWriter : ExactBufferWriter, IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PausingBufferWriter(
        int capacity
    ) : base(capacity) { }

    public Task ReadStarted => _readStarted.Task;

    public override Memory<byte> GetMemory(
        int sizeHint = 0
    )
    {
        _readStarted.TrySetResult();

        return _release.Wait(TimeSpan.FromSeconds(15))
            ? base.GetMemory(sizeHint)
            : throw new TimeoutException("The cache read was not released by its concurrent writer.");

    }

    public void Release() => _release.Set();
    public void Dispose() => _release.Dispose();
}

internal sealed class CacheSequenceSegment : ReadOnlySequenceSegment<byte>
{
    public CacheSequenceSegment(
        ReadOnlyMemory<byte> memory
    )
    {
        Memory = memory;
    }

    public CacheSequenceSegment Append(
        ReadOnlyMemory<byte> memory
    )
    {
        var segment = new CacheSequenceSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = segment;

        return segment;
    }
}

internal sealed class CacheManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Volatile.Read(ref _timestamp);

    public void Advance(
        TimeSpan elapsed
    ) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
}

internal sealed class CacheRecordingLogger : ILogger<MySqlDistributedCache>
{
    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(
        TState state
    )
        where TState : notnull => null;

    public bool IsEnabled(
        LogLevel logLevel
    ) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => Entries.Add(new Entry(logLevel, eventId, formatter(state, exception), exception));

    internal readonly record struct Entry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception
    );
}
