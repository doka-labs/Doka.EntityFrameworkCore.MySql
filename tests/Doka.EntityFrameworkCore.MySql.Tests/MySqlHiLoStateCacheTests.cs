namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the cross-DbContext sharing contract of <see cref="MySqlHiLoStateCache"/>: every
/// caller that asks for the same database, sequence-name, and block-size tuple receives
/// the same HiLoValueGeneratorState instance. Distinct databases never share a range,
/// and high-cardinality database churn cannot grow the process-wide cache without bound.
/// </summary>
public sealed class MySqlHiLoStateCacheTests : IDisposable
{
    private static readonly MySqlDatabaseIdentity s_database = DatabaseIdentity("orders");

    public MySqlHiLoStateCacheTests()
    {
        MySqlHiLoStateCache.ResetForTesting();
    }

    public void Dispose() => MySqlHiLoStateCache.ResetForTesting();

    [Fact]
    public void GetOrCreate_returns_same_instance_for_same_key()
    {
        var first = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);
        var second = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);

        Assert.Same(first, second);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_returns_distinct_instances_for_distinct_sequence_names()
    {
        var orders = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);
        var invoices = MySqlHiLoStateCache.GetOrCreate(s_database, "invoices_seq", blockSize: 10);

        Assert.NotSame(orders, invoices);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_treats_distinct_block_sizes_as_distinct_keys()
    {
        var small = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);
        var large = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 100);

        Assert.NotSame(small, large);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void GetOrCreate_isolates_same_sequence_between_databases()
    {
        var orders = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity("orders"), "shared_seq", blockSize: 10);
        var billing = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity("billing"), "shared_seq", blockSize: 10);

        Assert.NotSame(orders, billing);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void Database_identity_excludes_password()
    {
        var identity = MySqlDatabaseIdentity.FromConnectionString(
            "Server=db.example;Port=3307;Database=orders;User ID=app;Password=top-secret;");

        Assert.Equal("db.example", identity.Server);
        Assert.Equal((uint)3307, identity.Port);
        Assert.Equal("orders", identity.Database);
        Assert.Equal("app", identity.UserId);
        Assert.DoesNotContain("top-secret", identity.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetOrCreate_rejects_null_or_blank_sequence_name()
    {
        Assert.Throws<ArgumentNullException>(() => MySqlHiLoStateCache.GetOrCreate(s_database, null!, blockSize: 10));
        Assert.Throws<ArgumentException>(() => MySqlHiLoStateCache.GetOrCreate(s_database, string.Empty, blockSize: 10));
        Assert.Throws<ArgumentException>(() => MySqlHiLoStateCache.GetOrCreate(s_database, "   ", blockSize: 10));
    }

    [Fact]
    public void GetOrCreate_rejects_non_positive_block_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: -1));
    }

    [Fact]
    public void GetOrCreate_under_high_concurrency_returns_single_instance_per_key()
    {
        var instances = new ConcurrentBag<HiLoValueGeneratorState>();

        Parallel.For(
            0,
            2000,
            _ => instances.Add(MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10)));

        var distinct = instances.Distinct().ToArray();
        Assert.Single(distinct);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }

    [Fact]
    public void High_cardinality_database_churn_stays_within_capacity()
    {
        for (var databaseIndex = 0; databaseIndex < MySqlHiLoStateCache.Capacity * 2; databaseIndex++)
        {
            _ = MySqlHiLoStateCache.GetOrCreate(
                DatabaseIdentity($"tenant_{databaseIndex}"),
                "orders_seq",
                blockSize: 10);
        }

        Assert.InRange(MySqlHiLoStateCache.Count, 1, MySqlHiLoStateCache.Capacity);
    }

    [Fact]
    public void Evicted_and_recreated_state_lease_disjoint_server_ranges()
    {
        const int blockSize = 10;
        var databaseIdentity = DatabaseIdentity("evicted");
        var originalState = MySqlHiLoStateCache.GetOrCreate(databaseIdentity, "orders_seq", blockSize);
        var nextServerLow = 1 - blockSize;
        long LeaseRange() => Interlocked.Add(ref nextServerLow, blockSize);

        var values = new ConcurrentBag<int>
        {
            originalState.Next<int>(LeaseRange),
        };

        for (var databaseIndex = 0; databaseIndex < MySqlHiLoStateCache.Capacity; databaseIndex++)
        {
            _ = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity($"tenant_{databaseIndex}"), "orders_seq", blockSize);
        }

        var replacementState = MySqlHiLoStateCache.GetOrCreate(databaseIdentity, "orders_seq", blockSize);

        Assert.NotSame(originalState, replacementState);

        Parallel.For(
            0,
            blockSize * 2,
            index =>
            {
                var state = index % 2 == 0 ? originalState : replacementState;
                values.Add(state.Next<int>(LeaseRange));
            });

        Assert.Equal(blockSize * 2 + 1, values.Count);
        Assert.Equal(
            values.Count,
            values
                .Distinct()
                .Count());
    }

    private static MySqlDatabaseIdentity DatabaseIdentity(
        string database
    ) => MySqlDatabaseIdentity.FromConnectionString(
        $"Server=localhost;Port=3306;Database={database};User ID=app;Password=not-retained;");
}
