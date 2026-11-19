namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlWindowFunctionIntegrationTests
{
    /// <summary>Bounds a hung database operation without a CPU-dependent success threshold.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(3));

    private sealed class WindowRow
    {
        public int Id { get; set; }
        public int Group { get; set; }
        public int Sort { get; set; }
        public decimal? Amount { get; set; }
        public string? Name { get; set; }
        public WindowGroup ConvertedGroup { get; set; }
        public Guid? CharGuid { get; set; }
        public Guid? BinaryGuid { get; set; }
        public Guid? NativeGuid { get; set; }
        public DateTime? Timestamp { get; set; }
        public bool SourceVisible { get; set; } = true;
        public WindowTarget Target { get; set; } = null!;
    }

    private sealed class WindowTarget
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public int ConvertedInt { get; set; }
        public decimal ConvertedDecimal { get; set; }
    }

    private enum WindowGroup { First, Second, Third }

    private class WindowContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public WindowContext(
            DbContextOptions<WindowContext> options
        ) : base(options) { }

        public DbSet<WindowRow> Rows => Set<WindowRow>();

        /// <summary>Marks a test-owned native wait whose execution is independently observed on the server.</summary>
        /// <param name="seconds">The native wait bound; it is not a test success threshold.</param>
        /// <returns>The native SLEEP result.</returns>
        public static int NativeSleep(
            int seconds
        ) => throw new InvalidOperationException("The native sleep marker has no client implementation.");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .HasDbFunction(typeof(WindowContext).GetMethod(nameof(NativeSleep))!)
                .HasName("SLEEP")
                .IsBuiltIn()
                .IsNullable(false);

            modelBuilder.HasCharSet("utf8mb4");
            modelBuilder.Entity<WindowTarget>(entity =>
            {
                entity
                    .ToTable("Window targets")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Label)
                    .HasMaxLength(80);

                entity
                    .Property(row => row.IsVisible)
                    .HasDefaultValue(true);

                entity
                    .Property(row => row.ConvertedInt)
                    .HasConversion(value => value * 10, value => value / 10)
                    .HasDefaultValue(0);

                entity
                    .Property(row => row.ConvertedDecimal)
                    .HasConversion(value => value * 10m, value => value / 10m)
                    .HasPrecision(18, 4)
                    .HasDefaultValue(0m);

                entity.HasQueryFilter(row => row.IsVisible);
            });

            modelBuilder.Entity<WindowRow>(entity =>
            {
                entity
                    .ToTable("Window rows")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Amount)
                    .HasPrecision(18, 4);

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(80)
                    .UseCollation("utf8mb4_unicode_ci");

                entity
                    .Property(row => row.SourceVisible)
                    .HasDefaultValue(true);

                entity.HasQueryFilter(row => row.SourceVisible);
                entity
                    .HasOne(row => row.Target)
                    .WithMany()
                    .HasForeignKey(row => row.Group);

                entity
                    .Property(row => row.ConvertedGroup)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .HasDefaultValue(WindowGroup.First);

                entity
                    .Property(row => row.CharGuid)
                    .HasColumnType("char(36)");

                entity
                    .Property(row => row.BinaryGuid)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.NativeGuid)
                    .HasColumnType(Database.IsMariaDb()
                        ? "uuid"
                        : "char(36)");

                entity
                    .Property(row => row.Timestamp)
                    .HasPrecision(6);

                // WHY: The native stress fixture has fifty physical fields, including
                // shadow state that must survive an explicitly tracked entity projection.
                for (var index = 0; index < 39; index++)
                {
                    entity
                        .Property<int>("Payload" + index.ToString(CultureInfo.InvariantCulture))
                        .HasDefaultValue(0);
                }
            });
        }
    }

    /// <summary>Uses the existing schema utilities and an invocation-owned provider graph.</summary>
    private sealed class WindowStore : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly ServiceProvider _services;
        private readonly MySqlServerVersion _version;

        private string? _pooledConnectionString;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public WindowStore(
            string connectionString,
            MySqlServerVersion version,
            ServiceProvider services
        )
        {
            _connectionString = connectionString;
            _version = version;
            _services = services;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                // WHY: Only this unique fixture connection string is cleared. A cancelled or
                // partially consumed reader must not leave an idle pooled session during schema cleanup.
                if (_pooledConnectionString is { } poolConnectionString)
                {
                    await using var connection = new MySqlConnection(poolConnectionString);
                    await MySqlConnection
                        .ClearPoolAsync(connection, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // WHY: Existing cleanup is deliberately uncancellable and removes only this run-owned schema.
                await IntegrationDatabaseUtilities
                    .DropDatabaseAsync(_connectionString)
                    .ConfigureAwait(false);
            }
            finally
            {
                await _services
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Seeds peers, nullable neighbors and two independent partitions with native SQL.</summary>
        internal static async Task<WindowStore> CreateAsync(
            string target,
            CancellationToken cancellationToken
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var connectionString = IntegrationDatabaseUtilities.BuildConnectionString(
                IntegrationTestEnvironment.GetBaseConnectionString(selected),
                "DokaWindow_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture));

            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
                AutoEnlist = false,
            };

            var services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            var store = new WindowStore(builder.ConnectionString,
                IntegrationTestEnvironment.GetServerVersion(selected),
                services);

            try
            {
                await IntegrationDatabaseUtilities
                    .EnsureDatabaseExistsAsync(builder.ConnectionString, cancellationToken)
                    .ConfigureAwait(false);

                // WHY: Capture the actual pooled worker's provider-normalized identity
                // before Open can redact credentials; raw fixture settings are a different pool.
                await using (var pooled = new WindowContext(store.CreateOptions(null, true)))
                {
                    store._pooledConnectionString = pooled.Database.GetDbConnection()
                        .ConnectionString;
                }

                await using var context = store.CreateContext();
                await context
                    .Database
                    .ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken)
                    .ConfigureAwait(false);

                await context
                    .Database
                    .ExecuteSqlRawAsync(
                        "INSERT INTO `Window targets` (`Id`,`Label`) VALUES (1,'first'),(2,'second'),(3,'third'); "
                        + "INSERT INTO `Window rows` (`Id`,`Group`,`Sort`,`Amount`,`Name`) VALUES "
                        + "(1,1,1,10,'alpha'),(2,1,1,20,NULL),(3,1,2,30,'gamma'),(4,2,1,5,'delta'),(5,2,3,15,'epsilon'),(6,3,1,NULL,'zeta');",
                        cancellationToken)
                    .ConfigureAwait(false);

                await context
                    .Database
                    .ExecuteSqlRawAsync(
                        "UPDATE `Window rows` SET `ConvertedGroup`=CASE `Group` WHEN 1 THEN 'First' WHEN 2 THEN 'Second' ELSE 'Third' END, "
                        + "`CharGuid`=CASE WHEN `Id`=2 THEN NULL ELSE CONCAT('00112233-4455-6677-8899-',LPAD(`Id`,12,'0')) END, "
                        + "`BinaryGuid`=UNHEX(REPLACE(`CharGuid`,'-','')),`NativeGuid`=`CharGuid`, "
                        + "`Timestamp`=TIMESTAMPADD(MICROSECOND,`Id`,'2026-01-01 00:00:00');",
                        cancellationToken)
                    .ConfigureAwait(false);

                return store;
            }
            catch
            {
                await store
                    .DisposeAsync()
                    .ConfigureAwait(false);

                throw;
            }
        }

        /// <summary>Creates an untracked query context using only this fixture's provider graph.</summary>
        internal WindowContext CreateContext() => new(CreateOptions(null, false));

        /// <summary>Shares only the fixture's provider graph while allowing an invocation-local observer or connection pool.</summary>
        internal DbContextOptions<WindowContext> CreateOptions(
            DbCommandInterceptor? observer,
            bool pooling
        )
        {
            var connection = new MySqlConnectionStringBuilder(pooling
                ? _pooledConnectionString ?? _connectionString
                : _connectionString)
            {
                Pooling = pooling,
                MaximumPoolSize = 1,

                // WHY: Explicit fixture cleanup must drain idle workers instead of relying on background expiry.
                ConnectionIdleTimeout = 0,
            };

            var builder = IntegrationTestDbContextOptions
                .Create<WindowContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(connection.ConnectionString, _version);

            if (observer is not null)
            {
                builder.AddInterceptors(observer);
            }

            return builder.Options;
        }

        /// <summary>Creates the selected native partition distribution and distinct values in all fifty fields.</summary>
        internal async Task SeedWideRowsAsync(
            string distribution,
            CancellationToken cancellationToken
        )
        {
            var group = distribution switch
            {
                "single" => "1",
                "many-small" => "((`Id`+1) DIV 2)",
                "skewed" => "CASE WHEN `Id`<=13900 THEN 1 ELSE 2 END",
                _ => throw new ArgumentOutOfRangeException(nameof(distribution)),
            };

            await using var context = CreateContext();
            await context
                .Database
                .ExecuteSqlRawAsync(
                    "INSERT INTO `Window rows` (`Id`,`Group`,`Sort`,`Amount`,`Name`) "
                    + "SELECT 6 + ROW_NUMBER() OVER (),1,0,0,'wide' FROM `Window rows` a CROSS JOIN `Window rows` b "
                    + "CROSS JOIN `Window rows` c CROSS JOIN `Window rows` d CROSS JOIN `Window rows` e "
                    + "CROSS JOIN `Window rows` f LIMIT 13994;",
                    cancellationToken)
                .ConfigureAwait(false);

            if (distribution == "many-small")
            {
                // WHY: The existing Group foreign key remains enforced even when every pair is a separate partition.
                await context
                    .Database
                    .ExecuteSqlRawAsync(
                        "INSERT IGNORE INTO `Window targets` (`Id`,`Label`) SELECT `Id`,CONCAT('wide-group-',`Id`) "
                        + "FROM `Window rows` WHERE `Id`<=7000;",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var converted = distribution == "skewed"
                ? "CASE WHEN `Id`<=13900 THEN 'First' ELSE 'Second' END"
                : "'First'";

            var update = new StringBuilder("UPDATE `Window rows` SET `Group`=")
                .Append(group)
                .Append(",`ConvertedGroup`=")
                .Append(converted)
                .Append(",`Sort`=`Id`,`Amount`=MOD(`Id`,17),`SourceVisible`=TRUE");

            // WHY: Distinct field values expose wrong ordinals during full tracked materialization.
            for (var index = 0; index < 39; index++)
            {
                update
                    .Append(",`Payload")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("`=`Id`+")
                    .Append(index.ToString(CultureInfo.InvariantCulture));
            }

            update.Append(';');
            await context
                .Database
                .ExecuteSqlRawAsync(update.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Creates an independent nonpooled observer so a busy worker never blocks readiness inspection.</summary>
        internal MySqlConnection CreateObserverConnection() => new(_connectionString);

        /// <summary>Records the native plan without imposing a plan shape or elapsed-time gate.</summary>
        internal async Task<string> ExplainAsync(
            string querySql,
            CancellationToken cancellationToken
        )
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN FORMAT=JSON " + querySql;

            return Convert.ToString(await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)!;
        }
    }
}
