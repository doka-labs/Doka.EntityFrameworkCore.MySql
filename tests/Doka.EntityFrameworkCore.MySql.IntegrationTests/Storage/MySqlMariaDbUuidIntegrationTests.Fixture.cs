namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMariaDbUuidIntegrationTests
{
    private enum UuidDataSet
    {
        Native,
        Versioned,
    }

    /// <summary>Owns lazily seeded, read-only UUID datasets independently for each selected engine.</summary>
    public sealed class NativeUuidFixture : IAsyncLifetime
    {
        private readonly Dictionary<(IntegrationDatabaseTarget Target, UuidDataSet DataSet), Task> _seeds = [];

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <summary>Returns a fresh context after seeding the native model once.</summary>
        internal async Task<NativeUuidContext> CreateNativeContextAsync(
            IntegrationDatabaseTarget target
        )
        {
            await EnsureSeededAsync(target, UuidDataSet.Native).ConfigureAwait(false);

            return CreateNativeContext(target, nameof(UuidDataSet.Native));
        }

        /// <summary>Returns a fresh context after seeding versioned UUID rows once.</summary>
        internal async Task<VersionedUuidContext> CreateVersionedContextAsync(
            IntegrationDatabaseTarget target
        )
        {
            await EnsureSeededAsync(target, UuidDataSet.Versioned).ConfigureAwait(false);

            return CreateVersionedContext(target);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var key in _seeds.Keys)
            {
                try
                {
                    await using var connection = new MySqlConnection(
                        CreateNativeConnectionString(key.Target, key.DataSet.ToString()));
                    await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                    if (key.DataSet == UuidDataSet.Native)
                    {
                        await DropModelTablesAsync(connection).ConfigureAwait(false);
                    }
                    else
                    {
                        await DropVersionedTableAsync(connection).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    // WHY: Partial setup on one engine must not prevent cleanup of others.
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Could not clean {key.DataSet} UUID data on {key.Target}.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Native UUID fixture cleanup failed.", failures);
            }
        }

        private Task EnsureSeededAsync(
            IntegrationDatabaseTarget target,
            UuidDataSet dataSet
        )
        {
            // WHY: xUnit serializes this class. Retaining the task also retains a
            // failed seed without retrying partial DDL or poisoning another engine.
            if (!_seeds.TryGetValue((target, dataSet), out var seed))
            {
                seed = dataSet == UuidDataSet.Native
                    ? SeedNativeAsync(target)
                    : SeedVersionedAsync(target);
                _seeds.Add((target, dataSet), seed);
            }

            return seed;
        }

        private static async Task SeedNativeAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateNativeContext(target, nameof(UuidDataSet.Native));
            await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await DropModelTablesAsync(connection).ConfigureAwait(false);
            await CreateNativeModelAsync(context).ConfigureAwait(false);
        }

        private static async Task SeedVersionedAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateVersionedContext(target);
            await context.Database.OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await DropVersionedTableAsync(connection).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);

            // WHY: Explicit fixture keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context.Set<VersionedUuidEntity>().AddRange(
                new VersionedUuidEntity
                {
                    Id = 1, NativeId = s_v1TextFirst, BinaryId = s_v1TextFirst, CharId = s_v1TextFirst,
                },
                new VersionedUuidEntity
                {
                    Id = 2, NativeId = s_v1TextLast, BinaryId = s_v1TextLast, CharId = s_v1TextLast,
                },
                new VersionedUuidEntity
                {
                    Id = 3, NativeId = s_v7First, BinaryId = s_v7First, CharId = s_v7First,
                },
                new VersionedUuidEntity
                {
                    Id = 4, NativeId = s_v7Last, BinaryId = s_v7Last, CharId = s_v7Last,
                });

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
