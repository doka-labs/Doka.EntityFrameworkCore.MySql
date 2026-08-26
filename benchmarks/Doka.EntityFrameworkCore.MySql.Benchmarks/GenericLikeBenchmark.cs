namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class GenericLikeBenchmark : IDisposable
{
    private static readonly NonCachingMemoryCache s_compilationCache = new();
    private BenchmarkContext _cachedContext = null!;
    private BenchmarkContext _compilationContext = null!;

    [GlobalSetup]
    public void Setup() => Initialize(BenchmarkEnvironment.CreateOptions<BenchmarkContext>());

    internal void Initialize(
        DbContextOptions<BenchmarkContext> options
    )
    {
        _cachedContext = new BenchmarkContext(options);

        var compilationOptions = new DbContextOptionsBuilder<BenchmarkContext>(options)
            .UseModel(_cachedContext.Model)
            .UseMemoryCache(s_compilationCache)
            .Options;

        _compilationContext = new BenchmarkContext(compilationOptions);
        CachedScalarQueryToQueryString();
        CompileScalarQueryToQueryString();
    }

    [Benchmark]
    public string CachedScalarQueryToQueryString()
    {
        return ScalarQuery(_cachedContext)
            .ToQueryString();
    }

    [Benchmark]
    public string CompileScalarQueryToQueryString()
    {
        return ScalarQuery(_compilationContext)
            .ToQueryString();
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _compilationContext?.Dispose();
        _cachedContext?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IQueryable<TranslationBenchmarkEntity> ScalarQuery(
        BenchmarkContext context
    )
    {
        var numberPattern = "%42%";
        var datePattern = "2026-08-%";
        var guidPattern = "00112233-%";
        var escape = "!";
        return context.TranslationEntities.Where(entity =>
            EF.Functions.Like(entity.SignedValue, numberPattern)
            || EF.Functions.Like(entity.CreatedAt, datePattern, escape)
            || EF.Functions.Like(entity.Token, guidPattern));
    }

    // A cache miss must compile the query without rebuilding the context, model, or services.
    private sealed class NonCachingMemoryCache : IMemoryCache
    {
        public bool TryGetValue(
            object key,
            out object? value
        )
        {
            value = null;
            return false;
        }

        public ICacheEntry CreateEntry(
            object key
        ) => new UnstoredEntry(key);

        public void Remove(
            object key
        )
        {
        }

        public void Dispose()
        {
        }

        private sealed class UnstoredEntry : ICacheEntry
        {
            public UnstoredEntry(
                object key
            )
            {
                Key = key;
            }

            public object Key { get; }
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = [];
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = [];
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }

            public void Dispose()
            {
            }
        }
    }
}
