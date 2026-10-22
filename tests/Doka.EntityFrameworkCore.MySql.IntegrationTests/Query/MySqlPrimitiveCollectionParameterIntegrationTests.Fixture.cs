namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Owns one seeded store per engine and one SQL-mode store.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>
    /// Owns one lazily seeded, class-scoped schema per selected engine.
    /// </summary>
    public sealed class SeededDatabaseFixture : IAsyncLifetime
    {
        private readonly Dictionary<IntegrationDatabaseTarget, Task> _seeds = new();
        private Task? _sqlModeSeed;

        /// <summary>Leaves seeding to the first selected test for each engine.</summary>
        public Task InitializeAsync() => Task.CompletedTask;

        /// <summary>Ensures the 2,200-row fixture is written once for the selected engine.</summary>
        /// <param name="target">The selected database engine.</param>
        /// <returns>A task that completes when both primitive-collection tables are ready.</returns>
        internal Task EnsureSeededAsync(
            IntegrationDatabaseTarget target
        )
        {
            // WHY: xUnit serializes methods in this class. Retaining the Task
            // also retains seed failures, so later cases do not retry partial DDL.
            if (!_seeds.TryGetValue(target, out var seed))
            {
                seed = SeedAsync(target);
                _seeds.Add(target, seed);
            }

            return seed;
        }

        /// <summary>Ensures the MariaDB SQL-mode rows are written once for this class.</summary>
        internal Task EnsureSqlModeSeededAsync() => _sqlModeSeed ??= SeedSqlModeAsync();

        /// <summary>Drops every table created by this class, including after test failure.</summary>
        public async Task DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var target in _seeds.Keys)
            {
                // WHY: a partially initialized engine may fail to drop one table;
                // the other table and remaining engines still need cleanup.
                try
                {
                    await using var charContext = CreateCharContext(target);
                    await DropCharTableAsync(charContext);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {CharTableName} on {target}.", exception));
                }

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

            if (_sqlModeSeed is not null)
            {
                try
                {
                    await using var context = CreateSqlModeContext();
                    await DropSqlModeTableAsync(context);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(
                        new InvalidOperationException($"Could not drop {SqlModeTableName} on MariaDb118.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Primitive collection fixture cleanup failed.", failures);
            }
        }

        private static async Task SeedSqlModeAsync()
        {
            await using var context = CreateSqlModeContext();
            await DropSqlModeTableAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.AddRange(
                new SqlModeStringCollectionEntity
                {
                    Id = 1,
                    Value = "\"quoted\"",
                    OptionalValue = null,
                    ConvertedValue = "alpha",
                    ConvertedText = new TextValue("\"quoted\""),
                    Values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null],
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 2,
                    Value = "\"\\q\"",
                    OptionalValue = "\"quoted\"",
                    ConvertedValue = "beta",
                    ConvertedText = new TextValue("\"\\q\""),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 3,
                    Value = string.Empty,
                    OptionalValue = "other",
                    ConvertedValue = "gamma",
                    ConvertedText = new TextValue(string.Empty),
                },
                new SqlModeStringCollectionEntity
                {
                    Id = 4,
                    Value = "gr\u00FCn",
                    OptionalValue = "other",
                    ConvertedValue = "delta",
                    ConvertedText = new TextValue("gr\u00FCn"),
                });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        private static async Task SeedAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateContext(target);
            await using var charContext = CreateCharContext(target);
            await DropTableAsync(context);
            await DropCharTableAsync(charContext);

            // WHY: each independent assertion needs the same large cardinality;
            // one seed per engine avoids multiplying schema DDL and 2,200 inserts.
            var entities = Enumerable
                .Range(0, RowCount)
                .Select(index => new PrimitiveCollectionEntity
                {
                    Id = index + 1,
                    BinaryId = CreateGuid(index),
                    OptionalBinaryId = index == 1 ? null : CreateGuid(index),
                    OptionalCharId = index == 1 ? null : CreateGuid(index),
                    CharId = CreateGuid(index),
                    Name = index switch
                    {
                        0 => "alpha",
                        1 => "\"quoted\"",
                        2 => "\"\\q\"",
                        _ => $"value-{index}",
                    },
                    OptionalName = index == 1 ? null : index == 0 ? "alpha" : $"optional-{index}",
                    ConvertedName = index switch
                    {
                        0 => "alpha",
                        1 => "beta",
                        _ => $"converted-{index}",
                    },
                    CaseInsensitiveName = index == 0 ? "alpha" : $"case-{index}",
                    ConvertedText = new TextValue(index switch
                    {
                        0 => "\"\\q\"",
                        1 => "gr\u00FCn",
                        _ => $"text-{index}",
                    }),
                    Status = index == 0 ? CollectionStatus.Active : CollectionStatus.Inactive,
                    OptionalStatus = index == 1 ? null : index == 0
                        ? CollectionStatus.Active
                        : CollectionStatus.Inactive,
                    ConvertedPrice = index == 0 ? new PriceValue(3.1m) : new PriceValue(index),
                    ConvertedGuid = new StrongGuid(CreateGuid(index)),
                    ConvertedDuration = index == 0
                        ? new ElapsedValue(TimeSpan.FromHours(27))
                        : new ElapsedValue(TimeSpan.FromHours(index % 24)),
                    ConvertedTimestamp = new DateTimeOffset(
                        2026,
                        9,
                        19,
                        10,
                        0,
                        0,
                        TimeSpan.Zero).AddSeconds(index),
                    ConvertedPayload = new BinaryValue($"payload-{index}"),
                    Payload = BitConverter.GetBytes(index),
                    GuidValues = [CreateGuid(index), Guid.Empty],
                })
                .ToArray();

            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);
            context.Items.AddRange(entities);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();

            await charContext.Database.ExecuteSqlRawAsync(
                charContext.Database.GenerateCreateScript(),
                CancellationToken.None);
            charContext.Add(
                new CharGuidCollectionEntity
                {
                    Id = 1,
                    GuidValues = [CreateGuid(10), Guid.Empty],
                });
            await charContext.SaveChangesAsync(CancellationToken.None);
            charContext.ChangeTracker.Clear();
        }
    }
}
