namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectSpatialIntegrationTests
{
    /// <summary>Executes fixed fixture SQL on its explicitly owned native connection.</summary>
    private static async Task ExecuteNativeAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Observes the original source SRID after a rejected statement without changing it.</summary>
    private static async Task<int> ReadScalarIntAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        // WHY: A missing source row must not be mistaken for the expected numeric SRID zero.
        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                "The spatial source observation did not return a non-null scalar value.");
        }

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Owns one generated spatial schema without modifying the main INSERT SELECT fixture.</summary>
    private sealed class SpatialStore : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _database;

        private SpatialStore(
            string database,
            string connectionString,
            MySqlConnection observer,
            MySqlServerVersion version
        )
        {
            _database = database;
            _connectionString = connectionString;
            Observer = observer;
            Version = version;
        }

        internal MySqlConnection Observer { get; }
        internal MySqlServerVersion Version { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // WHY: Teardown must release this owned schema after request cancellation, retaining a failed drop and failed disposal together.
            Exception? failure = null;

            try
            {
                await ExecuteNativeAsync(Observer, $"DROP DATABASE IF EXISTS `{_database}`;", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                await Observer
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }
        }

        /// <summary>Creates only this invocation's schema and verifies the real native atomicity precondition.</summary>
        internal static async Task<SpatialStore> CreateAsync(
            string target,
            CancellationToken cancellationToken
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var database = "DokaInsertSpatial_"
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
            SpatialStore? store = null;

            try
            {
                await observer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                // WHY: A unique owned schema must not silently attach to an existing database.
                await ExecuteNativeAsync(observer, $"CREATE DATABASE `{database}`;", cancellationToken)
                    .ConfigureAwait(false);

                builder.Database = database;
                store = new SpatialStore(database,
                    builder.ConnectionString,
                    observer,
                    IntegrationTestEnvironment.GetServerVersion(selected));

                await observer
                    .ChangeDatabaseAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await using var context = store.CreateContext();
                await context
                    .Database
                    .ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken)
                    .ConfigureAwait(false);

                await using var command = observer.CreateCommand();
                command.CommandText =
                    "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME;";

                await using var reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

                var count = 0;

                while (await reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!reader
                        .GetString(0)
                        .Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Spatial statement atomicity requires actual InnoDB source and target tables.");
                    }

                    count++;
                }

                if (count != 2)
                {
                    throw new InvalidOperationException(
                        "The isolated spatial fixture must contain exactly its two mapped tables.");
                }

                return store;
            }
            catch (Exception primary)
            {
                try
                {
                    // WHY: Failed/canceled setup still owns its database and connection; cleanup cannot inherit cancellation.
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
                    }
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(primary, cleanup);
                }

                throw;
            }
        }

        /// <summary>Enables the existing NTS plugin for this store's exact server and database.</summary>
        internal SpatialContext CreateContext(
            SpatialCommands? observation = null
        )
        {
            var builder = IntegrationTestDbContextOptions
                .Create<SpatialContext>()
                .UseMySql(_connectionString, Version, options => options.UseNetTopologySuite());

            if (observation is not null)
            {
                builder.AddInterceptors(observation);
            }

            return new SpatialContext(builder.Options);
        }

        /// <summary>Creates native WKT inputs; no CLR geometry converter manufactures the expected source value.</summary>
        internal Task SeedAsync(
            bool includeInvalidSrid,
            CancellationToken cancellationToken
        ) => ExecuteNativeAsync(Observer,
            $"INSERT INTO `{SourceTable}` (`Id`,`Location`) VALUES "
            + $"(1,{PointSql("1.25 2.5", 4326)}),(2,{PointSql("-3.75 4.125", includeInvalidSrid ? 0 : 4326)});",
            cancellationToken);

        /// <summary>Uses an explicit Cartesian X/Y input contract for MySQL's geographic axis conventions.</summary>
        internal string PointSql(
            string coordinates,
            int srid
        ) => Version.IsMariaDb
            ? $"ST_GeomFromText('POINT ({coordinates})', {srid.ToString(CultureInfo.InvariantCulture)})"
            : $"ST_GeomFromText('POINT ({coordinates})', {srid.ToString(CultureInfo.InvariantCulture)}, 'axis-order=long-lat')";

        /// <summary>Reads native coordinates and WKB using the same explicit X/Y contract as NTS.</summary>
        internal async Task<PointSnapshot[]> ReadPointsAsync(
            string table,
            CancellationToken cancellationToken
        )
        {
            await using var command = Observer.CreateCommand();

            // WHY: MySQL's geographic ST_X/ST_Y follow SRS axis order. Explicit
            // longitude/latitude and WKB axis order make this oracle comparable to NTS X/Y.
            command.CommandText = Version.IsMariaDb
                ? $"SELECT `Id`,ST_X(`Location`),ST_Y(`Location`),ST_SRID(`Location`),HEX(ST_AsWKB(`Location`)) FROM `{table}` ORDER BY `Id`;"
                : $"SELECT `Id`,ST_Longitude(`Location`),ST_Latitude(`Location`),ST_SRID(`Location`),HEX(ST_AsWKB(`Location`, 'axis-order=long-lat')) FROM `{table}` ORDER BY `Id`;";

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            var rows = new List<PointSnapshot>();

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                rows.Add(new PointSnapshot(reader.GetInt32(0),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.GetInt32(3),
                    reader.GetString(4)));
            }

            return rows.ToArray();
        }
    }

    private sealed record PointSnapshot(
        int Id,
        double X,
        double Y,
        int Srid,
        string Wkb
    );

    private sealed class SpatialContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public SpatialContext(
            DbContextOptions<SpatialContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialSource>(entity =>
            {
                entity.ToTable(SourceTable);
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Location)
                    .HasColumnType("point");
            });

            modelBuilder.Entity<SpatialTarget>(entity =>
            {
                entity.ToTable(TargetTable);
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Location)
                    .HasColumnType("point")
                    .HasSrid(4326);
            });
        }
    }

    private sealed class SpatialSource
    {
        public int Id { get; set; }
        public Point Location { get; set; } = null!;
    }

    private sealed class SpatialTarget
    {
        public int Id { get; set; }
        public Point Location { get; set; } = null!;
    }

    private sealed class SpatialCommands : DbCommandInterceptor
    {
        internal List<string> Statements { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        internal int Readers { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Statements.Add(command.CommandText);
            Tokens.Add(cancellationToken);

            return new ValueTask<InterceptionResult<int>>(result);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Readers++;

            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
