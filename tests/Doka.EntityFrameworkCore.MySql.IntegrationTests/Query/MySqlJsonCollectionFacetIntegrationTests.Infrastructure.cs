using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlJsonCollectionFacetIntegrationTests
{
    private const string TableName = "IntJsonCollectionFacetOwners";

    private static JsonCollectionFacetContext CreateContext(
        IntegrationDatabaseTarget target
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<JsonCollectionFacetContext>();

        // WHY: all partials use this one call site so fixture setup, queries and
        // cleanup share the schema selected by GetConnectionString's CallerFilePath.
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        return new JsonCollectionFacetContext(builder.Options);
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
            context.Owners.AddRange(
                new JsonCollectionFacetOwner
                {
                    Id = 1,
                    Values =
                    [
                        new JsonCollectionFacetValue
                        {
                            Marker = 1,
                            Text = ExactText,
                            Amount = ExactAmount,
                            OccurredAt = s_exactDateTime,
                            DateValue = s_exactDate,
                            StartsAt = s_exactTime,
                            Status = CollectionStatus.Active,
                            ConvertedAmount = new PriceValue(ExactAmount),
                            Duration = s_exactDuration,
                            ConvertedDuration = new ElapsedValue(s_exactDuration),
                            ConvertedTimestamp = s_exactConvertedTimestamp,
                            ConvertedPayload = new BinaryValue("alpha"),
                            GuidValue = s_exactGuid,
                            CharGuidValue = s_exactGuid,
                            Payload = s_exactPayload,
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
                            Duration = s_positiveOutOfRangeDuration,
                            ConvertedDuration = new ElapsedValue(s_positiveOutOfRangeDuration),
                            ConvertedPayload = new BinaryValue(string.Empty),
                        },
                        new JsonCollectionFacetValue
                        {
                            Marker = 3,
                            Duration = s_negativeOutOfRangeDuration,
                            ConvertedDuration = new ElapsedValue(s_negativeOutOfRangeDuration),
                            ConvertedPayload = new BinaryValue(string.Empty),
                        },
                    ],
                });
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
        }
    }
}
