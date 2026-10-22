namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlJsonCollectionFacetIntegrationTests
{
    private const string ExactText = "alphabet";
    private const decimal ExactAmount = 123.456m;
    private static readonly DateTime s_exactDateTime = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Unspecified)
        .AddTicks(1234560);

    private static readonly DateTime s_exactDate = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Unspecified);
    private static readonly TimeOnly s_exactTime = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234560));
    private static readonly TimeSpan s_exactDuration = TimeSpan.FromHours(27).Add(TimeSpan.FromTicks(1234560));
    private static readonly DateTimeOffset s_exactConvertedTimestamp = new DateTimeOffset(
        2026,
        9,
        19,
        10,
        0,
        0,
        TimeSpan.Zero);

    private static readonly Guid s_exactGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly TimeSpan s_positiveOutOfRangeDuration = TimeSpan.FromHours(839);
    private static readonly TimeSpan s_negativeOutOfRangeDuration = TimeSpan.FromHours(-839);
    private static readonly byte[] s_exactPayload = [1, 2, 3, 4];

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
                            value => Encoding.UTF8.GetBytes(value.Value),
                            value => new BinaryValue(Encoding.UTF8.GetString(value)))
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
