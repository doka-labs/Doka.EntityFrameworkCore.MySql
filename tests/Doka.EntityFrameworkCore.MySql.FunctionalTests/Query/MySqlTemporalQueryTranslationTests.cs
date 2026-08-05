namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Query;

/// <summary>
/// Verifies the complete temporal query contract without requiring a live database.
/// </summary>
public sealed class MySqlTemporalQueryTranslationTests
{
    private static readonly DateTime s_from =
        new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly DateTime s_to =
        new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    /// <summary>
    /// MariaDB query roots map directly to the native system-time grammar.
    /// </summary>
    [Theory]
    [InlineData("AsOf", "FOR SYSTEM_TIME AS OF")]
    [InlineData("FromTo", "FOR SYSTEM_TIME FROM")]
    [InlineData("Between", "FOR SYSTEM_TIME BETWEEN")]
    [InlineData("All", "FOR SYSTEM_TIME ALL")]
    public void MariaDb_temporal_queries_use_native_system_time(
        string operation,
        string expectedClause
    )
    {
        using var context = CreateContext<MariaDbConfiguration>(MySqlServerVersion.MariaDb(new Version(11, 4, 0)));

        var sql = CreateTemporalQuery(context.Records, operation)
            .Where(record => record.Id > 0)
            .ToQueryString();

        Assert.Contains(expectedClause, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNION ALL", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB's missing contained-in operator is expressed over its native ALL source.
    /// </summary>
    [Fact]
    public void MariaDb_contained_in_uses_native_history_with_explicit_boundaries()
    {
        using var context = CreateContext<MariaDbConfiguration>(MySqlServerVersion.MariaDb(new Version(11, 4, 0)));

        var sql = context
            .Records.TemporalContainedIn(s_from, s_to)
            .ToQueryString();

        Assert.Contains("FOR SYSTEM_TIME ALL", sql, StringComparison.Ordinal);
        Assert.Contains("`PeriodStart` >= '2026-01-02 03:04:05.000000'", sql, StringComparison.Ordinal);
        Assert.Contains("`PeriodEnd` <= '2026-02-03 04:05:06.000000'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// MySQL temporal roots read both provider-owned physical tables.
    /// </summary>
    [Theory]
    [InlineData("AsOf")]
    [InlineData("FromTo")]
    [InlineData("Between")]
    [InlineData("ContainedIn")]
    [InlineData("All")]
    public void MySql_temporal_queries_use_current_history_union(
        string operation
    )
    {
        using var context = CreateContext<MySqlConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var sql = CreateTemporalQuery(context.Records, operation)
            .ToQueryString();

        Assert.Contains("FROM `TemporalRecords`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `TemporalRecordsHistory`", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR SYSTEM_TIME", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A separately configured history database is always emitted as a qualified identifier.
    /// </summary>
    [Fact]
    public void MySql_temporal_queries_qualify_configured_history_schema()
    {
        using var context = CreateContext<MySqlCrossSchemaConfiguration>(
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var sql = context
            .Records.TemporalAll()
            .ToQueryString();

        Assert.Contains("FROM `TemporalRecords`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `audit`.`TemporalRecordsHistory`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// MySQL emulation keeps each public operator's documented interval semantics.
    /// </summary>
    [Theory]
    [InlineData("AsOf", "`PeriodStart` <=", "`PeriodEnd` >")]
    [InlineData("FromTo", "`PeriodStart` <", "`PeriodEnd` >")]
    [InlineData("Between", "`PeriodStart` <=", "`PeriodEnd` >")]
    [InlineData("ContainedIn", "`PeriodStart` >=", "`PeriodEnd` <=")]
    public void MySql_temporal_queries_preserve_boundary_semantics(
        string operation,
        string expectedStartPredicate,
        string expectedEndPredicate
    )
    {
        using var context = CreateContext<MySqlConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var sql = CreateTemporalQuery(context.Records, operation)
            .ToQueryString();

        Assert.Equal(2, CountOccurrences(sql, expectedStartPredicate));
        Assert.Equal(2, CountOccurrences(sql, expectedEndPredicate));
    }

    /// <summary>
    /// Every temporal root is no-tracking before provider translation begins.
    /// </summary>
    [Fact]
    public void Temporal_queries_are_always_no_tracking()
    {
        using var context = CreateContext<MySqlConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var query = context.Records.TemporalAll();

        Assert.Contains("AsNoTracking", query.Expression.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ambiguous local or unspecified timestamps are rejected before translation.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Temporal_queries_require_utc_boundaries(
        DateTimeKind kind
    )
    {
        using var context = CreateContext<MySqlConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));
        var boundary = DateTime.SpecifyKind(s_from, kind);

        var exception = Assert.Throws<ArgumentException>(() => context.Records.TemporalAsOf(boundary));

        Assert.Equal("utcPointInTime", exception.ParamName);
    }

    /// <summary>
    /// An inverted temporal range is rejected before it can reach SQL generation.
    /// </summary>
    [Fact]
    public void Temporal_queries_reject_inverted_ranges()
    {
        using var context = CreateContext<MySqlConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => context.Records.TemporalFromTo(s_to, s_from));

        Assert.Equal("utcTo", exception.ParamName);
    }

    /// <summary>
    /// Temporal operators cannot silently fall back to an ordinary table query.
    /// </summary>
    [Fact]
    public void Temporal_query_rejects_non_temporal_entity()
    {
        using var context = CreateContext<NonTemporalConfiguration>(MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Records.TemporalAll()
            .ToQueryString());

        Assert.Contains("non-temporal entity type", exception.Message, StringComparison.Ordinal);
    }

    private static IQueryable<TemporalQueryRecord> CreateTemporalQuery(
        DbSet<TemporalQueryRecord> source,
        string operation
    ) => operation switch
    {
        "AsOf" => source.TemporalAsOf(s_from),
        "FromTo" => source.TemporalFromTo(s_from, s_to),
        "Between" => source.TemporalBetween(s_from, s_to),
        "ContainedIn" => source.TemporalContainedIn(s_from, s_to),
        "All" => source.TemporalAll(),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static int CountOccurrences(
        string value,
        string fragment
    )
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private static TemporalQueryContext<TConfiguration> CreateContext<TConfiguration>(
        MySqlServerVersion serverVersion
    )
        where TConfiguration : ITemporalQueryConfiguration, new()
    {
        var options = new DbContextOptionsBuilder<TemporalQueryContext<TConfiguration>>().UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion)
            .Options;

        return new TemporalQueryContext<TConfiguration>(options);
    }

    private interface ITemporalQueryConfiguration
    {
        bool IsTemporal { get; }

        string? HistorySchema { get; }
    }

    private sealed class MySqlConfiguration : ITemporalQueryConfiguration
    {
        public bool IsTemporal => true;

        public string? HistorySchema => null;
    }

    private sealed class MySqlCrossSchemaConfiguration : ITemporalQueryConfiguration
    {
        public bool IsTemporal => true;

        public string? HistorySchema => "audit";
    }

    private sealed class MariaDbConfiguration : ITemporalQueryConfiguration
    {
        public bool IsTemporal => true;

        public string? HistorySchema => null;
    }

    private sealed class NonTemporalConfiguration : ITemporalQueryConfiguration
    {
        public bool IsTemporal => false;

        public string? HistorySchema => null;
    }

    private sealed class TemporalQueryContext<TConfiguration> : DbContext
        where TConfiguration : ITemporalQueryConfiguration, new()
    {
        public TemporalQueryContext(
            DbContextOptions<TemporalQueryContext<TConfiguration>> options
        ) : base(options) { }

        public DbSet<TemporalQueryRecord> Records => Set<TemporalQueryRecord>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var configuration = new TConfiguration();
            var entity = modelBuilder.Entity<TemporalQueryRecord>();
            entity.HasKey(record => record.Id);

            entity.ToTable(
                "TemporalRecords",
                table =>
                {
                    var temporal = table.IsTemporal(configuration.IsTemporal);

                    if (configuration.HistorySchema is not null)
                    {
                        temporal.UseHistoryTable("TemporalRecordsHistory", configuration.HistorySchema);
                    }
                });
        }
    }

    private sealed class TemporalQueryRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;
    }
}
