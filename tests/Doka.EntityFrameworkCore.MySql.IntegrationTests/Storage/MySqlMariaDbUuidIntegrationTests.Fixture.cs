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
        public Task InitializeAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            // WHY: Fixture teardown must release every engine dataset after a cancelled test.
            List<Exception>? failures = null;

            foreach (var key in _seeds.Keys)
            {
                try
                {
                    await using var connection = new MySqlConnection(
                        CreateNativeConnectionString(key.Target, key.DataSet.ToString()));

                    await connection
                        .OpenAsync(CancellationToken.None)
                        .ConfigureAwait(false);

                    if (key.DataSet == UuidDataSet.Native)
                    {
                        await DropModelTablesAsync(connection, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await DropVersionedTableAsync(connection, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    // WHY: Partial setup on one engine must not prevent cleanup of others.
                    (failures ??= []).Add(
                        new InvalidOperationException(
                            $"Could not clean {key.DataSet} UUID data on {key.Target}.",
                            exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Native UUID fixture cleanup failed.", failures);
            }
        }

        /// <summary>Returns a fresh context after seeding the native model once.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        internal async Task<NativeUuidContext> CreateNativeContextAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await EnsureSeededAsync(target, UuidDataSet.Native, cancellationToken)
                .ConfigureAwait(false);

            return CreateNativeContext(target, nameof(UuidDataSet.Native));
        }

        /// <summary>Returns a fresh context after seeding versioned UUID rows once.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        internal async Task<VersionedUuidContext> CreateVersionedContextAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await EnsureSeededAsync(target, UuidDataSet.Versioned, cancellationToken)
                .ConfigureAwait(false);

            return CreateVersionedContext(target);
        }

        /// <summary>Initializes the selected dataset once and retains its outcome for later cases.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="dataSet">The data set used by this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        private Task EnsureSeededAsync(
            IntegrationDatabaseTarget target,
            UuidDataSet dataSet,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: xUnit serializes this class. Retaining the task also retains a
            // failed seed without retrying partial DDL or poisoning another engine.
            if (!_seeds.TryGetValue((target, dataSet), out var seed))
            {
                seed = dataSet == UuidDataSet.Native
                    ? SeedNativeAsync(target, cancellationToken)
                    : SeedVersionedAsync(target, cancellationToken);

                _seeds.Add((target, dataSet), seed);
            }

            return seed;
        }

        /// <summary>Creates and seeds the native UUID model for the selected engine.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        private static async Task SeedNativeAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await using var context = CreateNativeContext(target, nameof(UuidDataSet.Native));
            await context
                .Database
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await DropModelTablesAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await CreateNativeModelAsync(context, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Creates the UUID-version comparison dataset for the selected engine.</summary>
        /// <param name="target">The database target used for this operation.</param>
        /// <param name="cancellationToken">The token to observe during asynchronous work.</param>
        private static async Task SeedVersionedAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            await using var context = CreateVersionedContext(target);
            await context
                .Database
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await DropVersionedTableAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await context
                .Database
                .ExecuteSqlRawAsync(
                    context.Database.GenerateCreateScript(),
                    cancellationToken)
                .ConfigureAwait(false);

            // WHY: Explicit fixture keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context
                .Set<VersionedUuidEntity>()
                .AddRange(
                    new VersionedUuidEntity
                    {
                        Id = 1,
                        NativeId = s_v1TextFirst,
                        BinaryId = s_v1TextFirst,
                        CharId = s_v1TextFirst,
                    },
                    new VersionedUuidEntity
                    {
                        Id = 2,
                        NativeId = s_v1TextLast,
                        BinaryId = s_v1TextLast,
                        CharId = s_v1TextLast,
                    },
                    new VersionedUuidEntity
                    {
                        Id = 3,
                        NativeId = s_v7First,
                        BinaryId = s_v7First,
                        CharId = s_v7First,
                    },
                    new VersionedUuidEntity
                    {
                        Id = 4,
                        NativeId = s_v7Last,
                        BinaryId = s_v7Last,
                        CharId = s_v7Last,
                    });

            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
