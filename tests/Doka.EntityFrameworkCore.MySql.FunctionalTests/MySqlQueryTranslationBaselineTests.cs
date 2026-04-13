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

        Assert.Contains("CHAR_LENGTH(", sql, StringComparison.Ordinal);
        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Contains("DATE(", sql, StringComparison.Ordinal);
        Assert.Contains("YEAR(", sql, StringComparison.Ordinal);
        Assert.Contains("MONTH(", sql, StringComparison.Ordinal);
        Assert.Contains("SECOND(", sql, StringComparison.Ordinal);
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
        Assert.Contains("ABS(", sql, StringComparison.Ordinal);
        Assert.Contains("CEILING(", sql, StringComparison.Ordinal);
        Assert.Contains("FLOOR(", sql, StringComparison.Ordinal);
        Assert.Contains("ROUND(", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE(", sql, StringComparison.Ordinal);
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
    /// Verifies that unsupported members still fail explicitly instead of falling back.
    /// </summary>
    [Fact]
    public void Unsupported_members_fail_explicitly()
    {
        using var context = new QueryTranslationContext(CreateOptions());

        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Entities.Where(entity => entity.CreatedAt.DayOfWeek == DayOfWeek.Monday)
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
