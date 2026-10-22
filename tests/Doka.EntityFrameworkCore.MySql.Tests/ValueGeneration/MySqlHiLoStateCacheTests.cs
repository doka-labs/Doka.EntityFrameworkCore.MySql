namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the cross-DbContext sharing contract of <see cref="MySqlHiLoStateCache"/>: every
/// caller that asks for the same database, sequence-name, and block-size tuple receives
/// the same HiLoValueGeneratorState instance. Distinct databases never share a range,
/// and high-cardinality database churn cannot grow the process-wide cache without bound.
/// </summary>
public sealed partial class MySqlHiLoStateCacheTests : IDisposable
{
    private static readonly MySqlDatabaseIdentity s_database = DatabaseIdentity("orders");

    /// <summary>Starts each cache-sharing scenario with an empty process-wide test cache.</summary>
    public MySqlHiLoStateCacheTests()
    {
        MySqlHiLoStateCache.ResetForTesting();
    }

    /// <inheritdoc />
    public void Dispose() => MySqlHiLoStateCache.ResetForTesting();

    /// <summary>
    /// Verifies repeated requests for the same database, sequence, and block size share one state.
    /// </summary>
    [Fact]
    public void GetOrCreate_returns_same_instance_for_same_key()
    {
        // Arrange
        var first = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);

        // Act
        var second = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }

    /// <summary>
    /// Verifies different sequence names cannot share a generated-value range.
    /// </summary>
    [Fact]
    public void GetOrCreate_returns_distinct_instances_for_distinct_sequence_names()
    {
        // Arrange
        var orders = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);

        // Act
        var invoices = MySqlHiLoStateCache.GetOrCreate(s_database, "invoices_seq", blockSize: 10);

        // Assert
        Assert.NotSame(orders, invoices);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    /// <summary>
    /// Verifies different block sizes identify separate generated-value ranges.
    /// </summary>
    [Fact]
    public void GetOrCreate_treats_distinct_block_sizes_as_distinct_keys()
    {
        // Arrange
        var small = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10);

        // Act
        var large = MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 100);

        // Assert
        Assert.NotSame(small, large);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    /// <summary>
    /// Verifies the same logical sequence stays isolated between different databases.
    /// </summary>
    [Fact]
    public void GetOrCreate_isolates_same_sequence_between_databases()
    {
        // Arrange
        var orders = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity("orders"), "shared_seq", blockSize: 10);

        // Act
        var billing = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity("billing"), "shared_seq", blockSize: 10);

        // Assert
        Assert.NotSame(orders, billing);
        Assert.Equal(2, MySqlHiLoStateCache.Count);
    }

    /// <summary>
    /// Verifies a missing or blank sequence name cannot create a cache entry.
    /// </summary>
    /// <param name="sequenceName">The missing or blank sequence name.</param>
    /// <param name="exceptionType">The exact argument-validation failure.</param>
    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData("   ", typeof(ArgumentException))]
    public void GetOrCreate_rejects_null_or_blank_sequence_name(
        string? sequenceName,
        Type exceptionType
    )
    {
        // Arrange
        var databaseIdentity = s_database;

        // Act
        var exception = Record.Exception(() =>
            MySqlHiLoStateCache.GetOrCreate(databaseIdentity, sequenceName!, blockSize: 10));

        // Assert
        Assert.IsType(exceptionType, exception);
    }

    /// <summary>
    /// Verifies a nonpositive block size cannot create an unusable sequence state.
    /// </summary>
    /// <param name="blockSize">The invalid sequence block size.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetOrCreate_rejects_non_positive_block_size(
        int blockSize
    )
    {
        // Arrange
        const string sequenceName = "orders_seq";

        // Act
        var exception = Record.Exception(() => MySqlHiLoStateCache.GetOrCreate(s_database, sequenceName, blockSize));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    /// Verifies concurrent admission for one key returns one shared sequence state.
    /// </summary>
    [Fact]
    public void GetOrCreate_under_high_concurrency_returns_single_instance_per_key()
    {
        // Arrange
        var instances = new ConcurrentBag<HiLoValueGeneratorState>();

        // Act
        Parallel.For(
            0,
            2000,
            _ => instances.Add(MySqlHiLoStateCache.GetOrCreate(s_database, "orders_seq", blockSize: 10)));

        var distinct = instances.Distinct().ToArray();

        // Assert
        Assert.Single(distinct);
        Assert.Equal(1, MySqlHiLoStateCache.Count);
    }

    /// <summary>
    /// Verifies high-cardinality database churn cannot exceed the cache's fixed resource budget.
    /// </summary>
    [Fact]
    public void High_cardinality_database_churn_stays_within_capacity()
    {
        // Arrange
        var databaseCount = MySqlHiLoStateCache.Capacity * 2;

        // Act
        for (var databaseIndex = 0; databaseIndex < databaseCount; databaseIndex++)
        {
            _ = MySqlHiLoStateCache.GetOrCreate(
                DatabaseIdentity($"tenant_{databaseIndex}"),
                "orders_seq",
                blockSize: 10);
        }

        // Assert
        Assert.InRange(MySqlHiLoStateCache.Count, 1, MySqlHiLoStateCache.Capacity);
    }

    /// <summary>
    /// Verifies overlapping old and replacement owners lease disjoint server ranges after eviction.
    /// </summary>
    [Fact]
    public void Evicted_and_recreated_state_lease_disjoint_server_ranges()
    {
        // Arrange
        const int blockSize = 10;
        var databaseIdentity = DatabaseIdentity("evicted");
        var originalState = MySqlHiLoStateCache.GetOrCreate(databaseIdentity, "orders_seq", blockSize);
        var nextServerLow = 1 - blockSize;
        long LeaseRange() => Interlocked.Add(ref nextServerLow, blockSize);
        var values = new ConcurrentBag<int>
        {
            originalState.Next<int>(LeaseRange),
        };

        // Act
        // WHY: Retaining a partially consumed old lease exposes collisions with the replacement owner after eviction.
        for (var databaseIndex = 0; databaseIndex < MySqlHiLoStateCache.Capacity; databaseIndex++)
        {
            _ = MySqlHiLoStateCache.GetOrCreate(DatabaseIdentity($"tenant_{databaseIndex}"), "orders_seq", blockSize);
        }

        var replacementState = MySqlHiLoStateCache.GetOrCreate(databaseIdentity, "orders_seq", blockSize);

        Parallel.For(
            0,
            blockSize * 2,
            index =>
            {
                var state = index % 2 == 0 ? originalState : replacementState;
                values.Add(state.Next<int>(LeaseRange));
            });

        // Assert
        Assert.NotSame(originalState, replacementState);
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
