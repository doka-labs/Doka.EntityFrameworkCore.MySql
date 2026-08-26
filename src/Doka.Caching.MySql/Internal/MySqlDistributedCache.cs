namespace Doka.Caching.MySql;

internal sealed class MySqlDistributedCache : IBufferDistributedCache, IDisposable, IAsyncDisposable
{
    private static readonly Action<ILogger, string, int, Exception?> s_cleanupFailed =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(1, "ExpiredItemsCleanupFailed"),
            "The bounded expired-item cleanup failed ({ExceptionType}, database error {DatabaseErrorNumber}).");

    private readonly MySqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly MySqlCacheDatabaseOperations _operations;
    private readonly ILogger<MySqlDistributedCache> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cleanupInterval;
    private readonly long _defaultSlidingExpirationMicroseconds;

    private long _lastCleanupTimestamp;
    private int _cleanupActive;
    private int _disposed;

    public MySqlDistributedCache(
        IOptions<MySqlCacheOptions> options,
        ILogger<MySqlDistributedCache> logger,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var settings = new MySqlCacheSettings(options.Value);
        _ownsDataSource = settings.DataSource is null;
        _dataSource = settings.DataSource ?? new MySqlDataSource(settings.ConnectionString);
        _operations = new MySqlCacheDatabaseOperations(_dataSource, settings.QualifiedTableName);
        _defaultSlidingExpirationMicroseconds = settings.DefaultSlidingExpirationMicroseconds;
        _cleanupInterval = settings.ExpiredItemsDeletionInterval;
        _lastCleanupTimestamp = _timeProvider.GetTimestamp();
    }

    public byte[]? Get(
        string key
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var value = _operations.Get(key);
        DeleteExpiredItemsIfDue();
        return value;
    }

    public async Task<byte[]?> GetAsync(
        string key,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var value = await _operations
            .GetAsync(key, token)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(token)
            .ConfigureAwait(false);

        return value;
    }

    public bool TryGet(
        string key,
        IBufferWriter<byte> destination
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var found = _operations.TryGet(key, destination);
        DeleteExpiredItemsIfDue();
        return found;
    }

    public async ValueTask<bool> TryGetAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var found = await _operations
            .TryGetAsync(key, destination, token)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(token)
            .ConfigureAwait(false);

        return found;
    }

    public void Set(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(value);
        var expiration = MySqlCacheExpiration.Resolve(options, _defaultSlidingExpirationMicroseconds);
        _operations.Set(key, value, expiration);
        DeleteExpiredItemsIfDue();
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(value);
        return SetMemoryAsync(key, value, options, token);
    }

    public void Set(
        string key,
        ReadOnlySequence<byte> value,
        DistributedCacheEntryOptions options
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var expiration = MySqlCacheExpiration.Resolve(options, _defaultSlidingExpirationMicroseconds);
        if (value.IsSingleSegment)
        {
            _operations.Set(key, value.First, expiration);
            DeleteExpiredItemsIfDue();
            return;
        }

        MySqlCacheDatabaseOperations.ValidateKey(key);
        var length = GetSequenceLength(value);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            value.CopyTo(rented.AsSpan(0, length));
            _operations.Set(key, rented.AsMemory(0, length), expiration);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented.AsSpan(0, length));
            ArrayPool<byte>.Shared.Return(rented);
        }

        DeleteExpiredItemsIfDue();
    }

    public ValueTask SetAsync(
        string key,
        ReadOnlySequence<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var expiration = MySqlCacheExpiration.Resolve(options, _defaultSlidingExpirationMicroseconds);
        return value.IsSingleSegment
            ? SetSequenceMemoryAsync(key, value.First, expiration, token)
            : SetMultiSegmentAsync(key, value, expiration, token);
    }

    public void Refresh(
        string key
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _operations.Refresh(key);
        DeleteExpiredItemsIfDue();
    }

    public async Task RefreshAsync(
        string key,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _operations
            .RefreshAsync(key, token)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(token)
            .ConfigureAwait(false);
    }

    public void Remove(
        string key
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _operations.Remove(key);
        DeleteExpiredItemsIfDue();
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken token = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _operations
            .RemoveAsync(key, token)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(token)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0
            && _ownsDataSource)
        {
            _dataSource.Dispose();
        }
    }

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsDataSource
            ? _dataSource.DisposeAsync()
            : ValueTask.CompletedTask;

    private static int GetSequenceLength(
        ReadOnlySequence<byte> value
    )
    {
        if (value.Length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Cache values cannot exceed Int32.MaxValue bytes.");
        }

        return (int)value.Length;
    }

    private async Task SetMemoryAsync(
        string key,
        ReadOnlyMemory<byte> value,
        DistributedCacheEntryOptions options,
        CancellationToken cancellationToken
    )
    {
        var expiration = MySqlCacheExpiration.Resolve(options, _defaultSlidingExpirationMicroseconds);
        await _operations
            .SetAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask SetSequenceMemoryAsync(
        string key,
        ReadOnlyMemory<byte> value,
        MySqlCacheExpiration expiration,
        CancellationToken cancellationToken
    )
    {
        await _operations
            .SetAsync(key, value, expiration, cancellationToken)
            .ConfigureAwait(false);

        await DeleteExpiredItemsIfDueAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask SetMultiSegmentAsync(
        string key,
        ReadOnlySequence<byte> value,
        MySqlCacheExpiration expiration,
        CancellationToken cancellationToken
    )
    {
        MySqlCacheDatabaseOperations.ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        var length = GetSequenceLength(value);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            value.CopyTo(rented.AsSpan(0, length));
            await _operations
                .SetAsync(key, rented.AsMemory(0, length), expiration, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented.AsSpan(0, length));
            ArrayPool<byte>.Shared.Return(rented);
        }

        await DeleteExpiredItemsIfDueAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void DeleteExpiredItemsIfDue()
    {
        if (!TryStartCleanup())
        {
            return;
        }

        var fullBatch = false;
        try
        {
            fullBatch = _operations.DeleteExpiredItems();
        }
        catch (Exception exception)
        {
            LogCleanupFailure(exception);
        }
        finally
        {
            CompleteCleanup(fullBatch);
        }
    }

    private async ValueTask DeleteExpiredItemsIfDueAsync(
        CancellationToken cancellationToken
    )
    {
        if (!TryStartCleanup())
        {
            return;
        }

        var fullBatch = false;
        try
        {
            fullBatch = await _operations
                .DeleteExpiredItemsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // WHY: Only maintenance was canceled; the caller's cache operation already succeeded.
        }
        catch (Exception exception)
        {
            LogCleanupFailure(exception);
        }
        finally
        {
            CompleteCleanup(fullBatch);
        }
    }

    private bool TryStartCleanup()
    {
        var timestamp = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(Volatile.Read(ref _lastCleanupTimestamp), timestamp) < _cleanupInterval
            || Interlocked.CompareExchange(ref _cleanupActive, 1, 0) != 0)
        {
            return false;
        }

        if (_timeProvider.GetElapsedTime(Volatile.Read(ref _lastCleanupTimestamp), timestamp) < _cleanupInterval)
        {
            Volatile.Write(ref _cleanupActive, 0);
            return false;
        }

        return true;
    }

    private void CompleteCleanup(
        bool fullBatch
    )
    {
        // Renewed or concurrently removed candidates must not make a full batch look like a drained backlog.
        if (!fullBatch)
        {
            Volatile.Write(ref _lastCleanupTimestamp, _timeProvider.GetTimestamp());
        }

        Volatile.Write(ref _cleanupActive, 0);
    }

    private void LogCleanupFailure(
        Exception exception
    ) => s_cleanupFailed(
        _logger,
        exception.GetType().Name,
        exception is MySqlException databaseException ? databaseException.Number : 0,
        null);
}
