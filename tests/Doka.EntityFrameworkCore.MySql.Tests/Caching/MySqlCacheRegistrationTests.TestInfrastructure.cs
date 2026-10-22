using System.Buffers;
using Microsoft.Extensions.Caching.Distributed;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlCacheRegistrationTests
{
    /// <summary>
    /// Records constructor clock access without introducing a database dependency.
    /// </summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private int _timestampReadCount;

        /// <summary>Gets the number of timestamp reads.</summary>
        public int TimestampReadCount => Volatile.Read(ref _timestampReadCount);

        /// <inheritdoc />
        public override long GetTimestamp() => Interlocked.Increment(ref _timestampReadCount);
    }

    /// <summary>
    /// Detects unintended cache operations on registrations that should remain externally owned.
    /// </summary>
    private sealed class UnusedCache : IBufferDistributedCache
    {
        /// <inheritdoc />
        public byte[]? Get(
            string key
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public void Set(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public void Refresh(
            string key
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public Task RefreshAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public void Remove(
            string key
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public Task RemoveAsync(
            string key,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public bool TryGet(
            string key,
            IBufferWriter<byte> destination
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public ValueTask<bool> TryGetAsync(
            string key,
            IBufferWriter<byte> destination,
            CancellationToken token = default
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public void Set(
            string key,
            ReadOnlySequence<byte> value,
            DistributedCacheEntryOptions options
        ) => throw new InvalidOperationException();

        /// <inheritdoc />
        public ValueTask SetAsync(
            string key,
            ReadOnlySequence<byte> value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        ) => throw new InvalidOperationException();
    }
}
