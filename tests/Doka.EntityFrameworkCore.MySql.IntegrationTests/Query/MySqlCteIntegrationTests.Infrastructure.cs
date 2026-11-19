namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    /// <summary>Bounds hangs and teardown without imposing a hardware-dependent performance expectation.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(3));

    private sealed class CteRow
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    private sealed class CteContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public CteContext(
            DbContextOptions<CteContext> options
        ) : base(options) { }

        public DbSet<CteRow> Rows => Set<CteRow>();
        public DbSet<CteTarget> Targets => Set<CteTarget>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.Entity<CteTarget>(entity =>
            {
                entity
                    .ToTable("Cte targets")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();
            });

            modelBuilder.Entity<CteRow>(entity =>
            {
                entity
                    .ToTable("Cte rows")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Label)
                    .HasMaxLength(80)
                    .UseCollation("utf8mb4_unicode_ci");

                // WHY: Fifty physical fields expose accidental preservation of the entire
                // bound schema even when separate references need only two payload fields.
                for (var index = 0; index < 48; index++)
                {
                    entity
                        .Property<int>("Payload" + index.ToString(CultureInfo.InvariantCulture))
                        .HasDefaultValue(0);
                }
            });
        }
    }

    private sealed class CteTarget
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }

    /// <summary>Owns only its unique schema, observer and isolated provider graph.</summary>
    private sealed class CteStore : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _database;
        private readonly MySqlConnection _observer;
        private readonly ServiceProvider _services;
        private readonly MySqlServerVersion _version;

        private ServiceProvider? _bufferedServices;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public CteStore(
            string database,
            string connectionString,
            MySqlServerVersion version,
            MySqlConnection observer,
            ServiceProvider services
        )
        {
            _database = database;
            _connectionString = connectionString;
            _version = version;
            _observer = observer;
            _services = services;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // WHY: Teardown cannot inherit request cancellation; it removes only the uniquely owned schema.
            try
            {
                await using var command = _observer.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`;";
                await command
                    .ExecuteNonQueryAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _observer
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (_bufferedServices is not null)
                        {
                            await _bufferedServices
                                .DisposeAsync()
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await _services
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>Creates exact native seed values without exercising the feature under test during setup.</summary>
        internal static async Task<CteStore> CreateAsync(
            string target,
            CancellationToken cancellationToken
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var database = "DokaCte_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var builder = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(selected))
            {
                Database = string.Empty,
                Pooling = false,
                AutoEnlist = false,
            };

            var observer = new MySqlConnection(builder.ConnectionString);
            var services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            CteStore? store = null;

            try
            {
                await observer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await using var command = observer.CreateCommand();
                command.CommandText = $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                builder.Database = database;
                store = new CteStore(database,
                    builder.ConnectionString,
                    IntegrationTestEnvironment.GetServerVersion(selected),
                    observer,
                    services);

                await observer
                    .ChangeDatabaseAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await using var context = store.CreateContext();
                command.CommandText = context.Database.GenerateCreateScript();
                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                command.CommandText =
                    "INSERT INTO `Cte rows` (`Id`,`Label`) VALUES (1,'alpha'),(2,'beta'),(3,'gamma'),(4,'delta'); "
                    + "INSERT INTO `Cte targets` (`Id`,`Value`) VALUES(1,1),(2,2),(3,3),(4,4);";

                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                return store;
            }
            catch
            {
                // WHY: Forward cancellation must not abandon this fixture's schema or resources.
                if (store is not null)
                {
                    await store
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    await observer
                        .DisposeAsync()
                        .ConfigureAwait(false);

                    await services
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>Shares only the fixture-owned immutable provider graph and connection identity.</summary>
        internal CteContext CreateContext(
            bool buffered = false
        )
        {
            // WHY: Retry settings belong to the provider's validated singleton contract.
            // The buffering control needs its own owned graph rather than mutating the
            // nonretrying graph used to create and seed this fixture's schema.
            var provider = buffered
                ? _bufferedServices ??= new ServiceCollection()
                    .AddEntityFrameworkDokaMySql()
                    .BuildServiceProvider()
                : _services;

            var options = IntegrationTestDbContextOptions
                .Create<CteContext>()
                .UseInternalServiceProvider(provider);

            if (buffered)
            {
                // WHY: EF's retrying read strategy requests its real buffering path;
                // the recursive guard must precede that buffer, even for zero-column DTOs.
                options.UseMySql(_connectionString, _version, mysql => mysql.EnableRetryOnFailure());
            }
            else
            {
                options.UseMySql(_connectionString, _version);
            }

            return new CteContext(options.Options);
        }

        /// <summary>Seeds boundary text as a native parameter, independently of recursive translation.</summary>
        internal async Task SetAnchorLabelAsync(
            string label,
            CancellationToken cancellationToken
        )
        {
            await using var command = _observer.CreateCommand();
            command.CommandText = "UPDATE `Cte rows` SET `Label`=@label WHERE `Id`=1;";
            command.Parameters.AddWithValue("@label", label);
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Builds fourteen thousand fifty-column rows without constructing tracked CLR entities.</summary>
        internal async Task SeedWideRowsAsync(
            CancellationToken cancellationToken
        )
        {
            await using var command = _observer.CreateCommand();

            // WHY: Four seed rows crossed seven times provide enough native candidates
            // on both families; this fixture operation does not use the CTE API under test.
            command.CommandText = "INSERT INTO `Cte rows` (`Id`,`Label`) "
                + "SELECT 4 + ROW_NUMBER() OVER (), 'wide' FROM `Cte rows` a CROSS JOIN `Cte rows` b "
                + "CROSS JOIN `Cte rows` c CROSS JOIN `Cte rows` d CROSS JOIN `Cte rows` e "
                + "CROSS JOIN `Cte rows` f CROSS JOIN `Cte rows` g LIMIT 13996; "
                + "UPDATE `Cte rows` SET `Payload0`=`Id`*2, `Payload47`=`Id`*3;";

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Reads fixture-session native limits independently of the provider's guard SQL.</summary>
        internal async Task<(long PacketBytes, int RecursionDepth)> ReadNativeBudgetsAsync(
            CancellationToken cancellationToken
        )
        {
            await using var command = _observer.CreateCommand();
            command.CommandText = _version.IsMariaDb
                ? "SELECT @@max_allowed_packet, @@max_recursive_iterations;"
                : "SELECT @@max_allowed_packet, @@cte_max_recursion_depth;";

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            return (Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
        }
    }
}
