namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlPrimitiveCollectionFacetIntegrationTests
{
    private const string TableName = "IntPrimitiveCollectionFacetItems";

    private static PrimitiveCollectionFacetContext CreateContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<PrimitiveCollectionFacetContext>();

        // WHY: all partials use this one call site so fixture setup, queries and
        // cleanup share the schema selected by GetConnectionString's CallerFilePath.
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        return new PrimitiveCollectionFacetContext(builder.Options);
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

    /// <summary>Seeds immutable rows once per engine for independently reported queries.</summary>
    public sealed class SeededDatabaseFixture : IAsyncLifetime
    {
        private readonly Dictionary<IntegrationDatabaseTarget, Task> _seeds = new();

        /// <summary>Defers database setup until a selected engine needs it.</summary>
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <summary>Returns the original seed task, including any initialization failure.</summary>
        /// <param name="target">The selected database engine.</param>
        /// <returns>The completion of that engine's shared seed.</returns>
        internal Task EnsureSeededAsync(
            IntegrationDatabaseTarget target
        )
        {
            // WHY: xUnit serializes cases in this class; caching the task prevents
            // repeated DDL and keeps a seed failure visible to every dependent case.
            if (!_seeds.TryGetValue(target, out var seed))
            {
                seed = SeedAsync(target);
                _seeds.Add(target, seed);
            }

            return seed;
        }

        /// <summary>Drops each initialized table even if another engine's cleanup fails.</summary>
        public async ValueTask DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var target in _seeds.Keys)
            {
                try
                {
                    await using var context = CreateContext(target);

                    await DropTableAsync(context);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {TableName} on {target}.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }

        private static async Task SeedAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateContext(target);

            await DropTableAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 1,
                    Price = 3.1m,
                    OccurredAt = s_exactDateTime,
                    OptionalOccurredAt = s_exactDateTime,
                    RecordedAt = s_exactTimestamp,
                    BirthDate = s_birthDate,
                    CalendarDate = s_calendarDate,
                    OptionalCalendarDate = s_calendarDate,
                    StartsAt = s_exactTimeOnly,
                    Duration = s_exactTimeSpan,
                    OptionalDuration = s_exactTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 2,
                    Price = 10m,
                    OccurredAt = s_exactDateTime.AddDays(1),
                    RecordedAt = s_exactTimestamp.AddDays(1),
                    BirthDate = s_birthDate.AddDays(1),
                    CalendarDate = s_calendarDate.AddDays(1),
                    StartsAt = new TimeOnly(0, 0),
                    Duration = s_negativeTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 3,
                    Price = 20m,
                    OccurredAt = s_exactDateTime.AddDays(2),
                    RecordedAt = s_exactTimestamp.AddDays(2),
                    BirthDate = s_birthDate.AddDays(2),
                    CalendarDate = s_calendarDate.AddDays(2),
                    StartsAt = new TimeOnly(1, 0),
                    Duration = s_maximumStoredTimeSpan,
                });
            context.Add(
                new PrimitiveCollectionFacetEntity
                {
                    Id = 4,
                    Price = 30m,
                    OccurredAt = s_exactDateTime.AddDays(3),
                    RecordedAt = s_exactTimestamp.AddDays(3),
                    BirthDate = s_birthDate.AddDays(3),
                    CalendarDate = s_calendarDate.AddDays(3),
                    StartsAt = new TimeOnly(2, 0),
                    Duration = s_minimumStoredTimeSpan,
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
        }
    }
}
