namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies the actual expression-only window surface against both SQL dialects without opening a database.</summary>
public sealed partial class MySqlWindowFunctionTests
{
    /// <summary>Supplies every function family independently for both engine forms.</summary>
    public static TheoryData<bool, string, string> FunctionCases
    {
        get
        {
            var cases = new TheoryData<bool, string, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var (name, sql) in new[]
                    {
                        ("count", "COUNT(*)"),
                        ("long-count", "COUNT(*)"),
                        ("sum-int", "SUM("),
                        ("sum-long", "SUM("),
                        ("sum-float", "SUM("),
                        ("sum-double", "SUM("),
                        ("sum-decimal", "SUM("),
                        ("sum-nullable", "SUM("),
                        ("average-int", "AVG("),
                        ("average-long", "AVG("),
                        ("average-float", "AVG("),
                        ("average-double", "AVG("),
                        ("average-decimal", "AVG("),
                        ("average-nullable", "AVG("),
                        ("min", "MIN("),
                        ("max", "MAX("),
                        ("min-string", "MIN("),
                        ("row-number", "ROW_NUMBER()"),
                        ("rank", "RANK()"),
                        ("dense-rank", "DENSE_RANK()"),
                        ("ntile", "NTILE(3)"),
                        ("percent-rank", "PERCENT_RANK()"),
                        ("cume-dist", "CUME_DIST()"),
                        ("lag", "LAG("),
                        ("lead", "LEAD("),
                        ("lag-zero", "LAG("),
                        ("lead-two", "LEAD("),
                        ("first", "FIRST_VALUE("),
                        ("last", "LAST_VALUE("),
                        ("nth", "NTH_VALUE("),
                        ("lag-string", "LAG("),
                        ("lag-guid", "LAG("),
                    })
                {
                    cases.Add(mariaDb, name, sql);
                }
            }

            return cases;
        }
    }

    /// <summary>Every public family reaches a typed SQL window and preserves the source filter.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    /// <param name="function">The independently selected scalar scenario.</param>
    /// <param name="expected">The native function fragment.</param>
    [Theory]
    [MemberData(nameof(FunctionCases))]
    public void Scalar_functions_translate_with_mapped_partition_order_and_source_filter(
        bool mariaDb,
        string function,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Where(row => row.Active)
            .Select(Selector(function));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains(expected, sql, StringComparison.Ordinal);
        Assert.Contains("OVER (PARTITION BY", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains("Active", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Partition aggregates without ordering retain the whole-partition contract.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Whole_partition_sum_keeps_no_ordering_and_typed_zero_coalesce(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .Sum(row.Amount));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("COALESCE(SUM(", sql, StringComparison.Ordinal);
        Assert.Contains("OVER (PARTITION BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
    }

    /// <summary>Missing-neighbor defaults use a presence marker only on the dialect without a native third argument.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lag_default_does_not_replace_a_present_null_neighbor(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lag(row.NullableValue, 1, -99));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("LAG(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(LAG", sql, StringComparison.Ordinal);
        if (mariaDb)
        {
            Assert.Contains("CASE WHEN LAG(1, 1) OVER", sql, StringComparison.Ordinal);
            Assert.Contains(" IS NULL THEN -99 ELSE LAG(", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("CASE WHEN", sql, StringComparison.Ordinal);
            Assert.Contains(", 1, -99) OVER", sql, StringComparison.Ordinal);
        }
    }

    /// <summary>Repeated partition keys and explicit tie-breakers remain separate scalar components.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Composite_partition_and_descending_tie_breaker_preserve_exact_ordering(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context.Rows.Select(row => EF
            .Functions
            .Window()
            .PartitionBy(new
            {
                row.AccountId,
                row.Name,
            })
            .OrderByDescending(row.Sequence)
            .ThenBy(row.Id)
            .Rank());

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("PARTITION BY", sql, StringComparison.Ordinal);
        Assert.Contains("`AccountId`,", sql, StringComparison.Ordinal);
        Assert.Contains("`Name` ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("`Sequence` DESC,", sql, StringComparison.Ordinal);
        Assert.Contains("`Id`)", sql, StringComparison.Ordinal);
    }

    private static Expression<Func<WindowRow, object?>> Selector(
        string function
    ) => function switch
    {
        "count" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Count(),
        "long-count" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .LongCount(),
        "sum-int" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.Sequence),
        "sum-long" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.LongValue),
        "sum-float" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.FloatValue),
        "sum-double" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.DoubleValue),
        "sum-decimal" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.Amount),
        "sum-nullable" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Sum(row.NullableValue),
        "average-int" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.Sequence),
        "average-long" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.LongValue),
        "average-float" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.FloatValue),
        "average-double" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.DoubleValue),
        "average-decimal" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.Amount),
        "average-nullable" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Average(row.NullableValue),
        "min" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Min(row.NullableValue),
        "max" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Max(row.NullableValue),
        "min-string" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Min(row.Name),
        "row-number" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .RowNumber(),
        "rank" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Rank(),
        "dense-rank" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .DenseRank(),
        "ntile" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Ntile(3),
        "percent-rank" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .PercentRank(),
        "cume-dist" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .CumeDist(),
        "lag" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lag(row.NullableValue),
        "lead" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lead(row.NullableValue),
        "lag-zero" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lag(row.NullableValue, 0),
        "lead-two" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lead(row.NullableValue, 2),
        "first" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .FirstValue(row.NullableValue),
        "last" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .LastValue(row.NullableValue),
        "nth" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .NthValue(row.NullableValue, 2),
        "lag-string" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lag(row.Name),
        "lag-guid" => row => EF
            .Functions
            .Window()
            .PartitionBy(row.AccountId)
            .OrderBy(row.Id)
            .Lag<Guid>(row.GuidValue),
        _ => throw new ArgumentOutOfRangeException(nameof(function)),
    };

    private static WindowContext CreateContext(
        bool mariaDb
    )
    {
        var builder = MySqlFunctionalTestOptions
            .CreateTransientBuilder<WindowContext>()
            .UseMySql("Server=localhost;Database=windows;User ID=unused;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                    : MySqlServerVersion.MySql(new Version(8, 4, 13)));

        return new WindowContext(builder.Options);
    }

    private sealed class WindowContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public WindowContext(
            DbContextOptions<WindowContext> options
        ) : base(options) { }

        public DbSet<WindowRow> Rows => Set<WindowRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<WindowRow>()
                .ToTable("WindowRows");

            modelBuilder
                .Entity<WindowRow>()
                .Property(row => row.Amount)
                .HasPrecision(18, 2);

            modelBuilder
                .Entity<WindowRow>()
                .Property(row => row.GuidValue)
                .HasColumnType("char(36)");

            modelBuilder
                .Entity<WindowRow>()
                .Property(row => row.BinaryGuidValue)
                .HasColumnType("binary(16)");
        }
    }

    private sealed class WindowRow
    {
        public int Id { get; set; }
        public int? AccountId { get; set; }
        public bool Active { get; set; }
        public int Sequence { get; set; }
        public long LongValue { get; set; }
        public float FloatValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal Amount { get; set; }
        public int? NullableValue { get; set; }
        public string? Name { get; set; }
        public Guid GuidValue { get; set; }
        public Guid BinaryGuidValue { get; set; }
    }
}
