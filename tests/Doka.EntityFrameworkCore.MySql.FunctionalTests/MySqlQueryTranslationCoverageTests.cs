namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Comprehensive coverage tests for all query translations that were previously untested:
/// DateTime.Add*, TimeSpan members, string methods, math functions, GROUP_CONCAT, edge cases.
/// </summary>
public sealed class MySqlQueryTranslationCoverageTests
{
    // ── DateTime.Add* Translations ──

    [Fact]
    public void DateTime_AddYears_translates_to_interval_year()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddYears(1) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("YEAR", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_AddMonths_translates_to_interval_month()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMonths(6) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MONTH", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_AddDays_translates_to_interval_day()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddDays(30) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_AddHours_translates_to_interval_hour()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddHours(12) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOUR", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_AddMinutes_translates_to_interval_minute()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddMinutes(45) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINUTE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_AddSeconds_translates_to_interval_second()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.CreatedAt.AddSeconds(90) > DateTime.Now)
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SECOND", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── TimeSpan Member Translations ──

    [Fact]
    public void TimeSpan_TotalSeconds_translates_to_time_to_sec()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalSeconds)
            .ToQueryString();

        Assert.Contains("TIME_TO_SEC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeSpan_TotalMinutes_translates_to_time_to_sec_divided()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalMinutes)
            .ToQueryString();

        Assert.Contains("TIME_TO_SEC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("60", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeSpan_TotalHours_translates_to_time_to_sec_divided()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Duration.TotalHours)
            .ToQueryString();

        Assert.Contains("TIME_TO_SEC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3600", sql, StringComparison.Ordinal);
    }

    // ── String Method Translations ──

    [Fact]
    public void String_TrimStart_translates_to_ltrim()
    {
        using var context = CreateContext();

        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimStart())
            .ToQueryString();

        Assert.Contains("LTRIM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_TrimEnd_translates_to_rtrim()
    {
        using var context = CreateContext();

        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.TrimEnd())
            .ToQueryString();

        Assert.Contains("RTRIM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_PadLeft_translates_to_lpad()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadLeft(10, '0'))
            .ToQueryString();

        Assert.Contains("LPAD", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_PadRight_translates_to_rpad()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.PadRight(10, '_'))
            .ToQueryString();

        Assert.Contains("RPAD", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Substring_two_args_translates_to_mysql_substring()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.Name.Substring(2, 5))
            .ToQueryString();

        Assert.Contains("SUBSTRING", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Concat_three_args_translates_to_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => string.Concat(e.Name, " ", e.Name))
            .ToQueryString();

        Assert.Contains("CONCAT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_Equals_instance_translates_server_side()
    {
        using var context = CreateContext();

        // string.Equals inside an IQueryable expression — EF translates it to a
        // server-side equality comparison; CLR StringComparison is not consulted.
#pragma warning disable CA1309
        var sql = context
            .Set<CoverageEntity>()
            .Where(e => e.Name.Equals("test"))
            .ToQueryString();
#pragma warning restore CA1309

        Assert.Contains("`Name`", sql, StringComparison.Ordinal);
        Assert.Contains("=", sql, StringComparison.Ordinal);
    }

    // ── Math Function Translations ──

    [Fact]
    public void Math_Log10_translates_to_log10()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log10(e.Score))
            .ToQueryString();

        Assert.Contains("LOG10", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Math_Exp_translates_to_exp()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Exp(e.Score))
            .ToQueryString();

        Assert.Contains("EXP", sql, StringComparison.OrdinalIgnoreCase);
    }

    // Math.Sign already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Sin_translates_to_sin()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Sin(e.Score))
            .ToQueryString();

        Assert.Contains("SIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Math_Cos_translates_to_cos()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Cos(e.Score))
            .ToQueryString();

        Assert.Contains("COS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Math_Tan_translates_to_tan()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Tan(e.Score))
            .ToQueryString();

        Assert.Contains("TAN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Math_Atan2_translates_to_atan2()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Atan2(e.Score, 1.0))
            .ToQueryString();

        Assert.Contains("ATAN2", sql, StringComparison.OrdinalIgnoreCase);
    }

    // Math.Pow, Math.Sqrt, Math.Log (1-arg) already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Log_two_args_translates_to_log()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log(e.Score, 2.0))
            .ToQueryString();

        Assert.Contains("LOG", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Math_Round_one_arg_translates_to_round()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Round(e.Score))
            .ToQueryString();

        Assert.Contains("ROUND", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── GROUP_CONCAT ──

    [Fact]
    public void String_Join_translates_to_group_concat()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .GroupBy(e => e.Category)
            .Select(g => new
            {
                Category = g.Key,
                Names = string.Join(", ", g.Select(e => e.Name))
            })
            .ToQueryString();

        Assert.Contains("GROUP_CONCAT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SEPARATOR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP_CONCAT(`c`.`Name`, ", sql, StringComparison.Ordinal);
    }

    // ── DateTime member translations ──

    [Fact]
    public void DateTime_Day_translates_to_day_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Day)
            .ToQueryString();

        Assert.Contains("DAY(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_Hour_translates_to_hour_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Hour)
            .ToQueryString();

        Assert.Contains("HOUR(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateTime_Minute_translates_to_minute_function()
    {
        using var context = CreateContext();
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => e.CreatedAt.Minute)
            .ToQueryString();

        Assert.Contains("MINUTE(", sql, StringComparison.OrdinalIgnoreCase);
    }

    // DateTime.Now and DateTime.UtcNow already tested in MySqlQueryTranslationExtendedTests.

    // ── Helpers ──

    private static CoverageContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<CoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new CoverageContext(builder.Options);
    }

    private sealed class CoverageEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Score { get; set; }
        public double AltScore { get; set; }
        public int AltId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateOnly BirthDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    private sealed class CoverageContext : DbContext
    {
        public CoverageContext(
            DbContextOptions<CoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<CoverageEntity>(e =>
            {
                e.ToTable("CoverageEntities");
                e.HasKey(x => x.Id);
            });
        }
    }
}
