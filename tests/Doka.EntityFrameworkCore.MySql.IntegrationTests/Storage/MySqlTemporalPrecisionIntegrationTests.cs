namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies supported temporal precision against representative MySQL and
/// MariaDB servers.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlTemporalPrecisionIntegrationTests
{
    private const string TableName = "IntTemporalPrecisionItems";

    /// <summary>
    /// Verifies precision-six temporal round trips on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_round_trips_supported_temporal_precision() =>
        AssertTemporalPrecisionAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies precision-six temporal round trips on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_round_trips_supported_temporal_precision() =>
        AssertTemporalPrecisionAsync(IntegrationDatabaseTarget.MariaDb118);

    private static async Task AssertTemporalPrecisionAsync(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<TemporalPrecisionContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));
        await using var context = new TemporalPrecisionContext(builder.Options);
        await DropTableAsync(context);

        try
        {
            var fraction = TimeSpan.FromTicks(1234560);
            var entity = new TemporalPrecisionEntity
            {
                Id = 1,
                DateTime = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Unspecified).Add(fraction),
                Timestamp = new DateTime(2026, 9, 19, 12, 34, 56, DateTimeKind.Utc).Add(fraction),
                TimeOnly = new TimeOnly(12, 34, 56).Add(fraction),
                TimeSpan = TimeSpan.FromHours(27) + fraction,
            };

            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Add(entity);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            var actual = await context.Items
                .SingleAsync(CancellationToken.None);

            Assert.Equal(entity.DateTime, actual.DateTime);
            Assert.Equal(entity.Timestamp, actual.Timestamp);
            Assert.Equal(entity.TimeOnly, actual.TimeOnly);
            Assert.Equal(entity.TimeSpan, actual.TimeSpan);
        }
        finally
        {
            await DropTableAsync(context);
        }
    }

    private static async Task DropTableAsync(
        TemporalPrecisionContext context
    ) => await context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{TableName}`;",
        CancellationToken.None);

    private sealed class TemporalPrecisionContext(
        DbContextOptions<TemporalPrecisionContext> options
    ) : DbContext(options)
    {
        public DbSet<TemporalPrecisionEntity> Items => Set<TemporalPrecisionEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalPrecisionEntity>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.DateTime)
                    .HasPrecision(6);
                entity
                    .Property(item => item.Timestamp)
                    .HasColumnType("timestamp(6)");
                entity
                    .Property(item => item.TimeOnly)
                    .HasPrecision(6);
                entity
                    .Property(item => item.TimeSpan)
                    .HasPrecision(6);
            });
        }
    }

    private sealed class TemporalPrecisionEntity
    {
        public int Id { get; set; }

        public DateTime DateTime { get; set; }

        public DateTime Timestamp { get; set; }

        public TimeOnly TimeOnly { get; set; }

        public TimeSpan TimeSpan { get; set; }
    }
}
