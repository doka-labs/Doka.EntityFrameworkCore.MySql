namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlPrimitiveCollectionFacetIntegrationTests
{
    private static readonly DateTime s_exactDateTime = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Unspecified);
    private static readonly DateTime s_exactTimestamp = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Utc);
    private static readonly DateTime s_birthDate = new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateOnly s_calendarDate = new DateOnly(2026, 9, 19);
    private static readonly TimeOnly s_exactTimeOnly = new TimeOnly(12, 34, 56);
    private static readonly TimeSpan s_exactTimeSpan = TimeSpan.FromHours(27);
    private static readonly TimeSpan s_negativeTimeSpan = TimeSpan.FromHours(-27);
    private static readonly TimeSpan s_maximumStoredTimeSpan = TimeSpan.FromHours(838)
        + TimeSpan.FromMinutes(59)
        + TimeSpan.FromSeconds(59);

    private static readonly TimeSpan s_minimumStoredTimeSpan = -s_maximumStoredTimeSpan;

    private sealed class PrimitiveCollectionFacetContext(
        DbContextOptions<PrimitiveCollectionFacetContext> options
    ) : DbContext(options)
    {
        public DbSet<PrimitiveCollectionFacetEntity> Items => Set<PrimitiveCollectionFacetEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PrimitiveCollectionFacetEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.Price)
                    .HasPrecision(10, 1);
                entity
                    .Property(item => item.OccurredAt)
                    .HasPrecision(0);
                entity
                    .Property(item => item.OptionalOccurredAt)
                    .HasPrecision(0);
                entity
                    .Property(item => item.RecordedAt)
                    .HasColumnType("timestamp(0)");
                entity
                    .Property(item => item.BirthDate)
                    .HasColumnType("date");
                entity
                    .Property(item => item.CalendarDate)
                    .HasColumnType("date");
                entity
                    .Property(item => item.OptionalCalendarDate)
                    .HasColumnType("date");
                entity
                    .Property(item => item.StartsAt)
                    .HasPrecision(0);
                entity
                    .Property(item => item.Duration)
                    .HasPrecision(0);
                entity
                    .Property(item => item.OptionalDuration)
                    .HasPrecision(0);
            });
        }
    }

    private sealed class PrimitiveCollectionFacetEntity
    {
        public int Id { get; set; }

        public decimal Price { get; set; }

        public DateTime OccurredAt { get; set; }

        public DateTime? OptionalOccurredAt { get; set; }

        public DateTime RecordedAt { get; set; }

        public DateTime BirthDate { get; set; }

        public DateOnly CalendarDate { get; set; }

        public DateOnly? OptionalCalendarDate { get; set; }

        public TimeOnly StartsAt { get; set; }

        public TimeSpan Duration { get; set; }

        public TimeSpan? OptionalDuration { get; set; }
    }
}
