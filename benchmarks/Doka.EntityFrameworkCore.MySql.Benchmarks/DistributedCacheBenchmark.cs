using Doka.Caching.MySql;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class DistributedCacheBenchmark : IDisposable
{
    private const int SmallValueSize = 1024;
    private const int LargeValueSize = 1024 * 1024;
    private const int ParallelReaders = 16;
    private const string TableName = "DistributedCacheBenchmarks";

    private readonly byte[] _smallValue = new byte[SmallValueSize];
    private readonly byte[] _largeValue = new byte[LargeValueSize];
    private readonly FixedBufferWriter _smallWriter = new(SmallValueSize);
    private readonly FixedBufferWriter _largeWriter = new(LargeValueSize);

    private readonly FixedBufferWriter[] _parallelWriters = Enumerable
        .Range(0, ParallelReaders)
        .Select(static _ => new FixedBufferWriter(SmallValueSize))
        .ToArray();

    private readonly Task<bool>[] _parallelReads = new Task<bool>[ParallelReaders];

    private readonly DistributedCacheEntryOptions _expiration = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    };

    private MySqlDistributedCache _cache = null!;
    private ReadOnlySequence<byte> _largeSequence;
    private ReadOnlySequence<byte> _segmentedSequence;

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkEnvironment.EnsureInitialized();
        var connectionString = BenchmarkEnvironment.CreateConnectionString(BenchmarkEnvironment.DatabaseNameValue);
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var command = new MySqlCommand(
            MySqlCacheSchema.GetCreateScript(BenchmarkEnvironment.DatabaseNameValue, TableName),
            connection);
        command.ExecuteNonQuery();

        _cache = new MySqlDistributedCache(
            Options.Create(
                new MySqlCacheOptions
                {
                    ConnectionString = connectionString,
                    SchemaName = BenchmarkEnvironment.DatabaseNameValue,
                    TableName = TableName,
                }),
            NullLogger<MySqlDistributedCache>.Instance);

        _smallValue.AsSpan().Fill(42);
        _largeValue.AsSpan().Fill(42);
        _cache.Set("small", _smallValue, _expiration);
        _cache.Set("large", _largeValue, _expiration);
        _cache.Set("sliding", _smallValue, new DistributedCacheEntryOptions());
        _largeSequence = new ReadOnlySequence<byte>(_largeValue);
        var first = new Segment(_largeValue.AsMemory(0, LargeValueSize / 2));
        var last = first.Append(_largeValue.AsMemory(LargeValueSize / 2));
        _segmentedSequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        // Warm the pooled multi-segment buffer before allocation measurement.
        _cache.Set("segmented", _segmentedSequence, _expiration);
    }

    [Benchmark]
    public byte[]? GetSmall() => _cache.Get("small");

    [Benchmark]
    public byte[]? GetLarge() => _cache.Get("large");

    [Benchmark]
    public byte[]? GetSliding() => _cache.Get("sliding");

    [Benchmark]
    public int BufferSliding()
    {
        _smallWriter.Reset();
        return _cache.TryGet("sliding", _smallWriter) ? _smallWriter.WrittenCount : -1;
    }

    [Benchmark]
    public int BufferSmall()
    {
        _smallWriter.Reset();
        return _cache.TryGet("small", _smallWriter) ? _smallWriter.WrittenCount : -1;
    }

    [Benchmark]
    public int BufferLarge()
    {
        _largeWriter.Reset();
        return _cache.TryGet("large", _largeWriter) ? _largeWriter.WrittenCount : -1;
    }

    [Benchmark]
    public byte[]? GetMissing() => _cache.Get("missing");

    [Benchmark]
    public ValueTask<bool> BufferMissingAsync() => _cache.TryGetAsync("missing", _smallWriter, CancellationToken.None);

    [Benchmark]
    public Task SetSmallAsync() => _cache.SetAsync("write-small", _smallValue, _expiration, CancellationToken.None);

    [Benchmark]
    public ValueTask SetLargeAsync() =>
        _cache.SetAsync("write-large", _largeSequence, _expiration, CancellationToken.None);

    [Benchmark]
    public ValueTask SetLargeMultiSegmentAsync() =>
        _cache.SetAsync("write-large", _segmentedSequence, _expiration, CancellationToken.None);

    [Benchmark]
    public Task<int> ParallelBufferReadsAsync() => ReadBuffersAsync("small");

    [Benchmark]
    public Task<int> ParallelSlidingBufferReadsAsync() => ReadBuffersAsync("sliding");

    private async Task<int> ReadBuffersAsync(
        string key
    )
    {
        for (var index = 0; index < ParallelReaders; index++)
        {
            _parallelWriters[index].Reset();
            _parallelReads[index] = _cache
                .TryGetAsync(key, _parallelWriters[index], CancellationToken.None)
                .AsTask();
        }

        var results = await Task
            .WhenAll(_parallelReads)
            .ConfigureAwait(false);

        var written = 0;
        for (var index = 0; index < ParallelReaders; index++)
        {
            if (!results[index]
                || _parallelWriters[index].WrittenCount != SmallValueSize)
            {
                throw new InvalidOperationException("A parallel cache read did not return the complete value.");
            }

            written += _parallelWriters[index].WrittenCount;
        }

        return written;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _cache?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedBufferWriter(int capacity) : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[capacity];

        public int WrittenCount { get; private set; }

        public void Reset() => WrittenCount = 0;

        public void Advance(
            int count
        )
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - WrittenCount);
            WrittenCount += count;
        }

        public Memory<byte> GetMemory(
            int sizeHint = 0
        )
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeHint, _buffer.Length - WrittenCount);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(
            int sizeHint = 0
        ) => GetMemory(sizeHint)
            .Span;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(
            ReadOnlyMemory<byte> memory
        ) => Memory = memory;

        public Segment Append(
            ReadOnlyMemory<byte> memory
        )
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
