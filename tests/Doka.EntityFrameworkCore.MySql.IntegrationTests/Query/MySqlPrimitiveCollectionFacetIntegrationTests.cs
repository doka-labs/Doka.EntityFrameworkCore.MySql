namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that parameterized primitive collections are extracted without
/// applying lossy property facets before comparison.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlPrimitiveCollectionFacetIntegrationTests
{
    private const string TableName = "IntPrimitiveCollectionFacetItems";

    /// <summary>
    /// Verifies lossless collection extraction on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies lossless collection extraction on MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MySql97);

    /// <summary>
    /// Verifies lossless collection extraction on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies lossless collection extraction on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies lossless collection extraction on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies lossless collection extraction on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_parameter_values_before_comparison() =>
        AssertParameterValuesRemainLosslessAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertParameterValuesRemainLosslessAsync(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<PrimitiveCollectionFacetContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));
        await using var context = new PrimitiveCollectionFacetContext(builder.Options);
        await DropTableAsync(context);

        var exactDateTime = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Unspecified);
        var exactTimestamp = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Utc);
        var birthDate = new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);
        var calendarDate = new DateOnly(2026, 9, 19);
        var exactTimeOnly = new TimeOnly(12, 34, 56);
        var exactTimeSpan = TimeSpan.FromHours(27);
        var negativeTimeSpan = TimeSpan.FromHours(-27);
        var maximumStoredTimeSpan = TimeSpan.FromHours(838)
            + TimeSpan.FromMinutes(59)
            + TimeSpan.FromSeconds(59);
        var minimumStoredTimeSpan = -maximumStoredTimeSpan;

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 1,
                    Price = 3.1m,
                    OccurredAt = exactDateTime,
                    OptionalOccurredAt = exactDateTime,
                    RecordedAt = exactTimestamp,
                    BirthDate = birthDate,
                    CalendarDate = calendarDate,
                    OptionalCalendarDate = calendarDate,
                    StartsAt = exactTimeOnly,
                    Duration = exactTimeSpan,
                    OptionalDuration = exactTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 2,
                    Price = 10m,
                    OccurredAt = exactDateTime.AddDays(1),
                    RecordedAt = exactTimestamp.AddDays(1),
                    BirthDate = birthDate.AddDays(1),
                    CalendarDate = calendarDate.AddDays(1),
                    StartsAt = new TimeOnly(0, 0),
                    Duration = negativeTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 3,
                    Price = 20m,
                    OccurredAt = exactDateTime.AddDays(2),
                    RecordedAt = exactTimestamp.AddDays(2),
                    BirthDate = birthDate.AddDays(2),
                    CalendarDate = calendarDate.AddDays(2),
                    StartsAt = new TimeOnly(1, 0),
                    Duration = maximumStoredTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 4,
                    Price = 30m,
                    OccurredAt = exactDateTime.AddDays(3),
                    RecordedAt = exactTimestamp.AddDays(3),
                    BirthDate = birthDate.AddDays(3),
                    CalendarDate = calendarDate.AddDays(3),
                    StartsAt = new TimeOnly(2, 0),
                    Duration = minimumStoredTimeSpan,
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var exactDecimals = new[] { 3.1m };
            var truncatedDecimals = new[] { 3.14159m };
            var exactDateTimes = new[] { exactDateTime };
            var truncatedDateTimes = new[] { exactDateTime.AddMilliseconds(500) };
            DateTime?[] exactNullableDateTimes = [exactDateTime];
            DateTime?[] outOfRangeNullableDateTimes = [DateTime.MinValue];
            var exactTimestamps = new[] { exactTimestamp };
            var truncatedTimestamps = new[] { exactTimestamp.AddMilliseconds(500) };
            var outOfRangeDateTimes = new[] { DateTime.MinValue };
            var exactDates = new[] { birthDate };
            var timeBearingDates = new[] { birthDate.AddHours(12) };
            var exactCalendarDates = new[] { calendarDate };
            var absentCalendarDates = new[] { calendarDate.AddYears(1) };
            DateOnly?[] exactNullableCalendarDates = [calendarDate];
            DateOnly?[] outOfRangeNullableCalendarDates = [DateOnly.MinValue];
            var exactTimeOnlyValues = new[] { exactTimeOnly };
            var truncatedTimeOnlyValues = new[] { exactTimeOnly.Add(TimeSpan.FromMilliseconds(500)) };
            var exactTimeSpanValues = new[] { exactTimeSpan };
            var truncatedTimeSpanValues = new[] { exactTimeSpan.Add(TimeSpan.FromMilliseconds(500)) };
            TimeSpan?[] exactNullableTimeSpanValues = [exactTimeSpan];
            var negativeTimeSpanValues = new[] { negativeTimeSpan };
            var positiveOutOfRangeTimeSpanValues = new[] { TimeSpan.FromHours(839) };
            var negativeOutOfRangeTimeSpanValues = new[] { TimeSpan.FromHours(-839) };

            var results = new PrimitiveCollectionFacetResults(
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactDecimals).Count(value => value == item.Price) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(truncatedDecimals).Count(value => value == item.Price) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactDateTimes).Count(value => value == item.OccurredAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(truncatedDateTimes).Count(value => value == item.OccurredAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactNullableDateTimes).Contains(item.OptionalOccurredAt))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(outOfRangeNullableDateTimes).Contains(item.OptionalOccurredAt))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactTimestamps).Count(value => value == item.RecordedAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(truncatedTimestamps).Count(value => value == item.RecordedAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(outOfRangeDateTimes).Count(value => value == item.OccurredAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactDates).Count(value => value == item.BirthDate) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(timeBearingDates).Count(value => value == item.BirthDate) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactCalendarDates).Contains(item.CalendarDate))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(absentCalendarDates).Contains(item.CalendarDate))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactNullableCalendarDates).Contains(item.OptionalCalendarDate))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(outOfRangeNullableCalendarDates).Contains(item.OptionalCalendarDate))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactTimeOnlyValues).Count(value => value == item.StartsAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(truncatedTimeOnlyValues).Count(value => value == item.StartsAt) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactTimeSpanValues).Count(value => value == item.Duration) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(truncatedTimeSpanValues).Count(value => value == item.Duration) > 0)),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(exactNullableTimeSpanValues).Contains(item.OptionalDuration))),
                await SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(negativeTimeSpanValues).Count(value => value == item.Duration) > 0)),
                await Record.ExceptionAsync(() => SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(positiveOutOfRangeTimeSpanValues)
                        .Count(value => value == item.Duration) > 0))),
                await Record.ExceptionAsync(() => SelectIdsAsync(context.Items.Where(
                    item => EF.Parameter(negativeOutOfRangeTimeSpanValues)
                        .Count(value => value == item.Duration) > 0))));

            Assert.Equal([1], results.ExactDecimal);
            Assert.Empty(results.TruncatedDecimal);
            Assert.Equal([1], results.ExactDateTime);
            Assert.Empty(results.TruncatedDateTime);
            Assert.Equal([1], results.ExactNullableDateTime);
            Assert.Empty(results.OutOfRangeNullableDateTime);
            Assert.Equal([1], results.ExactTimestamp);
            Assert.Empty(results.TruncatedTimestamp);
            Assert.Empty(results.OutOfRangeDateTime);
            Assert.Equal([1], results.ExactDate);
            Assert.Empty(results.TimeBearingDate);
            Assert.Equal([1], results.ExactCalendarDate);
            Assert.Empty(results.AbsentCalendarDate);
            Assert.Equal([1], results.ExactNullableCalendarDate);
            Assert.Empty(results.OutOfRangeNullableCalendarDate);
            Assert.Equal([1], results.ExactTimeOnly);
            Assert.Empty(results.TruncatedTimeOnly);
            Assert.Equal([1], results.ExactTimeSpan);
            Assert.Empty(results.TruncatedTimeSpan);
            Assert.Equal([1], results.ExactNullableTimeSpan);
            Assert.Equal([2], results.NegativeTimeSpan);
            Assert.Contains(
                "exceeds the MySQL TIME range",
                Assert.IsType<InvalidOperationException>(results.PositiveOutOfRangeTimeSpan).Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "exceeds the MySQL TIME range",
                Assert.IsType<InvalidOperationException>(results.NegativeOutOfRangeTimeSpan).Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await DropTableAsync(context);
        }
    }

    private static async Task<int[]> SelectIdsAsync(
        IQueryable<PrimitiveCollectionFacetEntity> query
    ) => await query
        .Select(item => item.Id)
        .ToArrayAsync(CancellationToken.None);

    private static async Task DropTableAsync(
        PrimitiveCollectionFacetContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private sealed record PrimitiveCollectionFacetResults(
        int[] ExactDecimal,
        int[] TruncatedDecimal,
        int[] ExactDateTime,
        int[] TruncatedDateTime,
        int[] ExactNullableDateTime,
        int[] OutOfRangeNullableDateTime,
        int[] ExactTimestamp,
        int[] TruncatedTimestamp,
        int[] OutOfRangeDateTime,
        int[] ExactDate,
        int[] TimeBearingDate,
        int[] ExactCalendarDate,
        int[] AbsentCalendarDate,
        int[] ExactNullableCalendarDate,
        int[] OutOfRangeNullableCalendarDate,
        int[] ExactTimeOnly,
        int[] TruncatedTimeOnly,
        int[] ExactTimeSpan,
        int[] TruncatedTimeSpan,
        int[] ExactNullableTimeSpan,
        int[] NegativeTimeSpan,
        Exception? PositiveOutOfRangeTimeSpan,
        Exception? NegativeOutOfRangeTimeSpan
    );

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
