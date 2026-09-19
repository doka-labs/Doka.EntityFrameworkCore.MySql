namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies lossless <c>JSON_TABLE</c> projections for owned JSON values.
/// </summary>
public sealed class MySqlJsonCollectionFacetTests
{
    /// <summary>
    /// Relational property facets must not narrow values embedded in a JSON
    /// document before EF applies the property mapping.
    /// </summary>
    [Fact]
    public void Owned_json_collection_uses_lossless_extraction_mappings()
    {
        using var context = CreateContext();

        var sql = context.Owners
            .AsNoTracking()
            .SelectMany(owner => owner.Values)
            .Where(value => value.Text == "alphabet")
            .ToQueryString();

        Assert.Contains("`Text` longtext PATH '$.Text'", sql, StringComparison.Ordinal);
        Assert.Contains("`Amount` decimal(65,30) PATH '$.Amount'", sql, StringComparison.Ordinal);
        Assert.Contains("`OccurredAt` datetime(6) PATH '$.OccurredAt'", sql, StringComparison.Ordinal);
        Assert.Contains("`DateValue` datetime(6) PATH '$.DateValue'", sql, StringComparison.Ordinal);
        Assert.Contains("`StartsAt` time(6) PATH '$.StartsAt'", sql, StringComparison.Ordinal);
        Assert.Contains("`Status` longtext PATH '$.Status'", sql, StringComparison.Ordinal);
        Assert.Contains("`ConvertedAmount` decimal(65,30) PATH '$.ConvertedAmount'", sql, StringComparison.Ordinal);
        Assert.Contains("`Duration` longtext PATH '$.Duration'", sql, StringComparison.Ordinal);
        Assert.Contains("`ConvertedDuration` longtext PATH '$.ConvertedDuration'", sql, StringComparison.Ordinal);
        Assert.Contains("`ConvertedTimestamp` datetime(6) PATH '$.ConvertedTimestamp'", sql, StringComparison.Ordinal);
        Assert.Contains("`ConvertedPayload` longtext PATH '$.ConvertedPayload'", sql, StringComparison.Ordinal);
        Assert.Contains("`GuidValue` char(36) PATH '$.GuidValue'", sql, StringComparison.Ordinal);
        Assert.Contains("`CharGuidValue` longtext PATH '$.CharGuidValue'", sql, StringComparison.Ordinal);
        Assert.Contains("`Payload` longtext PATH '$.Payload'", sql, StringComparison.Ordinal);
        Assert.Contains("UNHEX(REPLACE(`v`.`GuidValue`, '-', ''))", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(`v`.`CharGuidValue`))", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`Payload`)", sql, StringComparison.Ordinal);
        Assert.Contains("FROM_BASE64(`v`.`ConvertedPayload`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Text` varchar(4)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Amount` decimal(5,1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`OccurredAt` datetime(0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`DateValue` date PATH", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`StartsAt` time(0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Status` varchar(4)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`ConvertedAmount` decimal(5,1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`Duration` time(0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`ConvertedDuration` time(0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`ConvertedTimestamp` datetime(0)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`ConvertedPayload` varbinary(8)", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewriting decoded JSON values in grouping subqueries must leave EF Core's
    /// internal select-identifier columns unchanged.
    /// </summary>
    [Fact]
    public void Owned_json_grouping_preserves_select_identifier_columns()
    {
        using var context = CreateContext();

        var sql = context.Owners
            .Where(owner => owner.Values
                .GroupBy(value => value.Text)
                .Select(group => group.Sum(value => value.Marker))
                .Any(sum => sum == 16))
            .ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_UNQUOTE(JSON_QUOTE(", sql, StringComparison.Ordinal);
    }

    private static JsonCollectionFacetContext CreateContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<JsonCollectionFacetContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new JsonCollectionFacetContext(builder.Options);
    }

    private sealed class JsonCollectionFacetContext(
        DbContextOptions<JsonCollectionFacetContext> options
    ) : DbContext(options)
    {
        public DbSet<JsonCollectionFacetOwner> Owners => Set<JsonCollectionFacetOwner>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonCollectionFacetOwner>(entity =>
            {
                entity.HasKey(owner => owner.Id);
                entity.OwnsMany(owner => owner.Values, owned =>
                {
                    owned.ToJson();
                    owned
                        .Property(value => value.Text)
                        .HasMaxLength(4);
                    owned
                        .Property(value => value.Amount)
                        .HasPrecision(5, 1);
                    owned
                        .Property(value => value.OccurredAt)
                        .HasPrecision(0);
                    owned
                        .Property(value => value.DateValue)
                        .HasColumnType("date");
                    owned
                        .Property(value => value.StartsAt)
                        .HasPrecision(0);
                    owned
                        .Property(value => value.Status)
                        .HasConversion<string>()
                        .HasMaxLength(4);
                    owned
                        .Property(value => value.ConvertedAmount)
                        .HasConversion(
                            value => value.Value,
                            value => new PriceValue(value))
                        .HasPrecision(5, 1);
                    owned
                        .Property(value => value.Duration)
                        .HasPrecision(0);
                    owned
                        .Property(value => value.ConvertedDuration)
                        .HasConversion(
                            value => value.Value,
                            value => new ElapsedValue(value))
                        .HasPrecision(0);
                    owned
                        .Property(value => value.ConvertedTimestamp)
                        .HasConversion(
                            value => value.UtcDateTime,
                            value => new DateTimeOffset(value, TimeSpan.Zero))
                        .HasPrecision(0);
                    owned
                        .Property(value => value.ConvertedPayload)
                        .HasConversion(
                            value => System.Text.Encoding.UTF8.GetBytes(value.Value),
                            value => new BinaryValue(System.Text.Encoding.UTF8.GetString(value)))
                        .HasColumnType("varbinary(8)");
                    owned
                        .Property(value => value.CharGuidValue)
                        .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                });
            });
        }
    }

    private sealed class JsonCollectionFacetOwner
    {
        public int Id { get; set; }

        public List<JsonCollectionFacetValue> Values { get; set; } = [];
    }

    private sealed class JsonCollectionFacetValue
    {
        public int Marker { get; set; }

        public string Text { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime OccurredAt { get; set; }

        public DateTime DateValue { get; set; }

        public TimeOnly StartsAt { get; set; }

        public CollectionStatus Status { get; set; }

        public PriceValue ConvertedAmount { get; set; }

        public TimeSpan Duration { get; set; }

        public ElapsedValue ConvertedDuration { get; set; }

        public DateTimeOffset ConvertedTimestamp { get; set; }

        public BinaryValue ConvertedPayload { get; set; }

        public Guid GuidValue { get; set; }

        public Guid CharGuidValue { get; set; }

        public byte[] Payload { get; set; } = [];
    }

    private enum CollectionStatus
    {
        Inactive,
        Active,
    }

    private readonly record struct PriceValue(decimal Value);

    private readonly record struct ElapsedValue(TimeSpan Value);

    private readonly record struct BinaryValue(string Value);
}
