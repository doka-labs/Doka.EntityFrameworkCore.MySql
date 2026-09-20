using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that relational facets do not narrow values stored inside owned
/// JSON collections before filtering or materialization.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlJsonCollectionFacetIntegrationTests
{
    private const string TableName = "IntJsonCollectionFacetOwners";

    /// <summary>
    /// Verifies owned-JSON extraction on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies owned-JSON extraction on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies owned-JSON extraction on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies owned-JSON extraction on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies owned-JSON extraction on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies owned-JSON extraction on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_owned_json_values_before_comparison() =>
        AssertOwnedJsonValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertOwnedJsonValuesRemainLosslessAsync(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<JsonCollectionFacetContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));
        await using var context = new JsonCollectionFacetContext(builder.Options);
        await DropTableAsync(context);

        const string exactText = "alphabet";
        const decimal exactAmount = 123.456m;
        var exactDateTime = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Unspecified)
            .AddTicks(1234560);
        var exactDate = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Unspecified);
        var exactTime = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234560));
        var exactDuration = TimeSpan.FromHours(27).Add(TimeSpan.FromTicks(1234560));
        var exactConvertedTimestamp = new DateTimeOffset(
            2026,
            9,
            19,
            10,
            0,
            0,
            TimeSpan.Zero);
        var exactGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var positiveOutOfRangeDuration = TimeSpan.FromHours(839);
        var negativeOutOfRangeDuration = TimeSpan.FromHours(-839);
        byte[] exactPayload = [1, 2, 3, 4];

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.AddRange(
                new JsonCollectionFacetOwner
                {
                    Id = 1,
                    Values =
                    [
                        new JsonCollectionFacetValue
                        {
                            Marker = 1,
                            Text = exactText,
                            Amount = exactAmount,
                            OccurredAt = exactDateTime,
                            DateValue = exactDate,
                            StartsAt = exactTime,
                            Status = CollectionStatus.Active,
                            ConvertedAmount = new PriceValue(exactAmount),
                            Duration = exactDuration,
                            ConvertedDuration = new ElapsedValue(exactDuration),
                            ConvertedTimestamp = exactConvertedTimestamp,
                            ConvertedPayload = new BinaryValue("alpha"),
                            GuidValue = exactGuid,
                            CharGuidValue = exactGuid,
                            Payload = exactPayload,
                        },
                    ],
                },
                new JsonCollectionFacetOwner
                {
                    Id = 2,
                    Values =
                    [
                        new JsonCollectionFacetValue
                        {
                            Marker = 2,
                            Duration = positiveOutOfRangeDuration,
                            ConvertedDuration = new ElapsedValue(positiveOutOfRangeDuration),
                            ConvertedPayload = new BinaryValue(string.Empty),
                        },
                        new JsonCollectionFacetValue
                        {
                            Marker = 3,
                            Duration = negativeOutOfRangeDuration,
                            ConvertedDuration = new ElapsedValue(negativeOutOfRangeDuration),
                            ConvertedPayload = new BinaryValue(string.Empty),
                        },
                    ],
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var results = new JsonCollectionFacetResults(
                await SelectMarkersAsync(context, value => value.Text == exactText),
                await SelectMarkersAsync(context, value => value.Text == "alph"),
                await SelectMarkersAsync(context, value => value.Amount == exactAmount),
                await SelectMarkersAsync(context, value => value.Amount == 123.5m),
                await SelectMarkersAsync(context, value => value.OccurredAt == exactDateTime),
                await SelectMarkersAsync(
                    context,
                    value => value.OccurredAt == new DateTime(2026, 9, 19, 12, 34, 56)),
                await SelectMarkersAsync(context, value => value.DateValue == exactDate),
                await SelectMarkersAsync(context, value => value.DateValue == exactDate.Date),
                await SelectMarkersAsync(context, value => value.StartsAt == exactTime),
                await SelectMarkersAsync(context, value => value.StartsAt == new TimeOnly(12, 34, 56)),
                await SelectMarkersAsync(context, value => value.Status == CollectionStatus.Active),
                await SelectMarkersAsync(context, value => value.Status == (CollectionStatus)42),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedAmount == new PriceValue(exactAmount)),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedAmount == new PriceValue(123.5m)),
                await SelectMarkersAsync(context, value => value.Duration == exactDuration),
                await SelectMarkersAsync(
                    context,
                    value => value.Duration == TimeSpan.FromHours(27)),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedDuration == new ElapsedValue(exactDuration)),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedDuration
                        == new ElapsedValue(exactDuration.Add(TimeSpan.FromMilliseconds(500)))),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedTimestamp == exactConvertedTimestamp),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedTimestamp == exactConvertedTimestamp.AddMilliseconds(500)),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedPayload == new BinaryValue("alpha")),
                await SelectMarkersAsync(
                    context,
                    value => value.ConvertedPayload == new BinaryValue("missing")),
                await SelectMarkersAsync(context, value => value.GuidValue == exactGuid),
                await SelectMarkersAsync(context, value => value.GuidValue == Guid.Empty),
                await SelectMarkersAsync(context, value => value.CharGuidValue == exactGuid),
                await SelectMarkersAsync(context, value => value.CharGuidValue == Guid.Empty),
                await context.Owners
                    .AsNoTracking()
                    .Where(owner => owner.Id == 1)
                    .SelectMany(owner => owner.Values)
                    .Select(value => value.Payload)
                    .SingleAsync(CancellationToken.None),
                await context.Owners
                    .AsNoTracking()
                    .SingleAsync(owner => owner.Id == 2, CancellationToken.None));

            Assert.Equal([1], results.ExactText);
            Assert.Empty(results.TruncatedText);
            Assert.Equal([1], results.ExactAmount);
            Assert.Empty(results.RoundedAmount);
            Assert.Equal([1], results.ExactDateTime);
            Assert.Empty(results.TruncatedDateTime);
            Assert.Equal([1], results.ExactDate);
            Assert.Empty(results.TruncatedDate);
            Assert.Equal([1], results.ExactTime);
            Assert.Empty(results.TruncatedTime);
            Assert.Equal([1], results.ExactConvertedValue);
            Assert.Empty(results.AbsentConvertedValue);
            Assert.Equal([1], results.ExactConvertedAmount);
            Assert.Empty(results.RoundedConvertedAmount);
            Assert.Equal([1], results.ExactDuration);
            Assert.Empty(results.TruncatedDuration);
            Assert.Equal([1], results.ExactConvertedDuration);
            Assert.Empty(results.TruncatedConvertedDuration);
            Assert.Equal([1], results.ExactConvertedTimestamp);
            Assert.Empty(results.TruncatedConvertedTimestamp);
            Assert.Equal([1], results.ExactConvertedPayload);
            Assert.Empty(results.AbsentConvertedPayload);
            Assert.Equal([1], results.ExactGuid);
            Assert.Empty(results.AbsentGuid);
            Assert.Equal([1], results.ExactCharGuid);
            Assert.Empty(results.AbsentCharGuid);
            Assert.Equal(exactPayload, results.MaterializedPayload);
            Assert.Equal(
                [positiveOutOfRangeDuration, negativeOutOfRangeDuration],
                results.OutOfRangeOwner.Values.Select(value => value.Duration));
            Assert.Equal(
                [positiveOutOfRangeDuration, negativeOutOfRangeDuration],
                results.OutOfRangeOwner.Values.Select(value => value.ConvertedDuration.Value));
        }
        finally
        {
            await DropTableAsync(context);
        }
    }

    private static async Task<int[]> SelectMarkersAsync(
        JsonCollectionFacetContext context,
        Expression<Func<JsonCollectionFacetValue, bool>> predicate
    ) => await context.Owners
        .Where(owner => owner.Id == 1)
        .SelectMany(owner => owner.Values)
        .Where(predicate)
        .Select(value => value.Marker)
        .ToArrayAsync(CancellationToken.None);

    private static async Task DropTableAsync(
        JsonCollectionFacetContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private sealed record JsonCollectionFacetResults(
        int[] ExactText,
        int[] TruncatedText,
        int[] ExactAmount,
        int[] RoundedAmount,
        int[] ExactDateTime,
        int[] TruncatedDateTime,
        int[] ExactDate,
        int[] TruncatedDate,
        int[] ExactTime,
        int[] TruncatedTime,
        int[] ExactConvertedValue,
        int[] AbsentConvertedValue,
        int[] ExactConvertedAmount,
        int[] RoundedConvertedAmount,
        int[] ExactDuration,
        int[] TruncatedDuration,
        int[] ExactConvertedDuration,
        int[] TruncatedConvertedDuration,
        int[] ExactConvertedTimestamp,
        int[] TruncatedConvertedTimestamp,
        int[] ExactConvertedPayload,
        int[] AbsentConvertedPayload,
        int[] ExactGuid,
        int[] AbsentGuid,
        int[] ExactCharGuid,
        int[] AbsentCharGuid,
        byte[] MaterializedPayload,
        JsonCollectionFacetOwner OutOfRangeOwner
    );

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
                entity.ToTable(TableName);
                entity.HasKey(owner => owner.Id);
                entity
                    .Property(owner => owner.Id)
                    .ValueGeneratedNever();
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
