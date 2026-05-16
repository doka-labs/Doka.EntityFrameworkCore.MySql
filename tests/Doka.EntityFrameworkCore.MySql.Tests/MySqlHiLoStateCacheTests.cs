namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the cross-DbContext sharing contract of <see cref="MySqlHiLoStateCache"/>: every
/// caller that asks for the same sequence-name and block-size pair receives the same
/// HiLoValueGeneratorState instance, so the client-side block window survives across
/// short-lived DbContexts. A second context can consume the remainder of the cached
/// window without going back to the server, and a misconfigured block-size pairing
/// surfaces as two cache entries rather than silent state corruption.
/// </summary>
public sealed class MySqlHiLoStateCacheTests : IDisposable
{
    public MySqlHiLoStateCacheTests()
    {
        MySqlHiLoStateCache.ResetForTesting();
    }

    public void Dispose() => MySqlHiLoStateCache.ResetForTesting();

    [Fact]
    public void GetOrCreate_returns_same_instance_for_same_key()
    {
        var first = MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 10);
        var second = MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 10);

        Assert.Same(first, second);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_returns_distinct_instances_for_distinct_sequence_names()
    {
        var orders = MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 10);
        var invoices = MySqlHiLoStateCache.GetOrCreate("invoices_seq", blockSize: 10);

        Assert.NotSame(orders, invoices);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_treats_distinct_block_sizes_as_distinct_keys()
    {
        var small = MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 10);
        var large = MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 100);

        Assert.NotSame(small, large);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_rejects_null_or_blank_sequence_name()
    {
        Assert.Throws<ArgumentNullException>(() => MySqlHiLoStateCache.GetOrCreate(null!, blockSize: 10));
        Assert.Throws<ArgumentException>(() => MySqlHiLoStateCache.GetOrCreate(string.Empty, blockSize: 10));
        Assert.Throws<ArgumentException>(() => MySqlHiLoStateCache.GetOrCreate("   ", blockSize: 10));
    }

    [Fact]
    public void GetOrCreate_rejects_non_positive_block_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: -1));
    }

    [Fact]
    public void GetOrCreate_under_high_concurrency_returns_single_instance_per_key()
    {
        var instances = new ConcurrentBag<HiLoValueGeneratorState>();

        Parallel.For(0, 2000, _ => instances.Add(MySqlHiLoStateCache.GetOrCreate("orders_seq", blockSize: 10)));

        var distinct = instances.Distinct().ToArray();
        Assert.Single(distinct);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }
}
