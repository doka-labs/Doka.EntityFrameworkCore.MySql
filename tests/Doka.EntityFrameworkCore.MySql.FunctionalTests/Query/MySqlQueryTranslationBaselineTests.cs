namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the query-translation baseline.
/// </summary>
public sealed class MySqlQueryTranslationBaselineTests
{
    /// <summary>
    /// Verifies that the baseline string and temporal member translations stay server-side.
    /// </summary>
    [Fact]
    public void String_and_temporal_members_translate_server_side()
    {
        using var context = new QueryTranslationContext(CreateOptions());

        var sql = context
            .Entities.Select(entity => new
            {
                NameLength = entity.Name.Length,
                DatePortion = entity.CreatedAt.Date,
                entity.CreatedAt.Year,
                entity.BirthDate.Month,
                entity.StartTime.Second,
            })
            .ToQueryString();

        MySqlSqlAssert.ContainsFunction(sql, "CHAR_LENGTH");
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        MySqlSqlAssert.ContainsFunction(sql, "DATE");
        MySqlSqlAssert.ContainsFunction(sql, "YEAR");
        MySqlSqlAssert.ContainsFunction(sql, "MONTH");
        MySqlSqlAssert.ContainsFunction(sql, "SECOND");
    }

    /// <summary>
    /// Verifies that the baseline null and math translations stay server-side.
    /// </summary>
    [Fact]
    public void Null_and_math_methods_translate_server_side()
    {
        using var context = new QueryTranslationContext(CreateOptions());

        var sql = context
            .Entities.Where(entity =>
                !string.IsNullOrEmpty(entity.OptionalName)
                && Math.Abs(entity.Score) > 0
                && Math.Ceiling(entity.Score) > 0
                && Math.Floor(entity.Score) > 0
                && Math.Round(entity.Score, 2) > 0
                && Math.Truncate(entity.Score) > 0)
            .ToQueryString();

        Assert.Contains("`OptionalName`", sql, StringComparison.Ordinal);
        MySqlSqlAssert.ContainsFunction(sql, "ABS");
        MySqlSqlAssert.ContainsFunction(sql, "CEILING");
        MySqlSqlAssert.ContainsFunction(sql, "FLOOR");
        MySqlSqlAssert.ContainsFunction(sql, "ROUND");
        MySqlSqlAssert.ContainsFunction(sql, "TRUNCATE");
    }

    /// <summary>
    /// Verifies that variable query inputs stay parameterized in the supported baseline.
    /// </summary>
    [Fact]
    public void Variable_query_inputs_stay_parameterized()
    {
        using var context = new QueryTranslationContext(CreateOptions());
        var requiredName = "alpha";
        var minimumScore = 2.5;

        var sql = context
            .Entities.Where(entity => entity.Name == requiredName && entity.Score > minimumScore)
            .ToQueryString();

        Assert.Contains("@requiredName", sql, StringComparison.Ordinal);
        Assert.Contains("@minimumScore", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("= 'alpha'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("> 2.5", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a parameterized-collection <c>Contains</c> against an entity column resolves
    /// the collection type-mapping path (FindCollectionMapping inherited from base) and expands
    /// into inlined SQL constants at translation time. A null <c>FindCollectionMapping</c> override
    /// would re-introduce the NullTypeMappingInSqlTree failure observed on the EF Core 10
    /// specification suite (NorthwindWhereQueryRelationalTestBase, 16 IN-Contains tests).
    /// </summary>
    [Fact]
    public void Collection_parameter_contains_translates_to_inline_in_constants()
    {
        using var context = new QueryTranslationContext(CreateOptions());
        var names = new List<string> { "alpha", "beta", "gamma" };

        var sql = context
            .Entities.Where(entity => names.Contains(entity.Name))
            .ToQueryString();

        Assert.Contains("IN (", sql, StringComparison.Ordinal);
        Assert.Contains("'alpha'", sql, StringComparison.Ordinal);
        Assert.Contains("'beta'", sql, StringComparison.Ordinal);
        Assert.Contains("'gamma'", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an unstored <see cref="DateTime.Kind"/> cannot silently acquire
    /// server semantics.
    /// </summary>
    /// <remarks>
    /// MySQL <c>DATETIME</c> stores date and time fields without a CLR kind or time-zone
    /// attribute. Source retrieved 2026-07-29:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/datetime.html">
    /// MySQL date and time types</see>.
    /// </remarks>
    [Fact]
    public void Unstored_datetime_kind_fails_explicitly()
    {
        using var context = new QueryTranslationContext(CreateOptions());

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Entities.Where(entity => entity.CreatedAt.Kind == DateTimeKind.Utc)
            .ToQueryString());

        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DbContextOptions<QueryTranslationContext> CreateOptions()
    {
        var builder = new DbContextOptionsBuilder<QueryTranslationContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class QueryTranslationContext : DbContext
    {
        public QueryTranslationContext(
            DbContextOptions<QueryTranslationContext> options
        ) : base(options) { }

        public DbSet<QueryEntity> Entities => Set<QueryEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<QueryEntity>(entity =>
            {
                entity.ToTable("Phase2QueryEntities");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .IsRequired();
                entity.Property(item => item.OptionalName);
                entity.Property(item => item.CreatedAt);
                entity.Property(item => item.BirthDate);
                entity.Property(item => item.StartTime);
                entity.Property(item => item.Score);
            });
        }
    }

    private sealed class QueryEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? OptionalName { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateOnly BirthDate { get; set; }

        public TimeOnly StartTime { get; set; }

        public double Score { get; set; }
    }
}
