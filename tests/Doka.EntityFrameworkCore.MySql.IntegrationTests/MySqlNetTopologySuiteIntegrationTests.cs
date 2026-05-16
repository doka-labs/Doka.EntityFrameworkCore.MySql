using System.Globalization;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies representative spatial runtime queries against live local servers.
/// </summary>
public sealed class MySqlNetTopologySuiteIntegrationTests
{
    private const string PointTableName = "Phase3SpatialPointEntities";
    private const string PolygonTableName = "Phase3SpatialPolygonEntities";

    /// <summary>
    /// Verifies that the MySQL 8.4 runtime can persist, query, and materialize the spatial baseline.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_spatial_queries_and_materialization_succeed()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMySqlOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PointTableName}` (
                         `Id` int NOT NULL,
                         `Location` point NOT NULL,
                         CONSTRAINT `PK_{PointTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            context.PointEntities.AddRange(
                new SpatialPointEntity
                {
                    Id = 1,
                    Location = CreatePoint(13.4050, 52.5200),
                },
                new SpatialPointEntity
                {
                    Id = 2,
                    Location = CreatePoint(2.3522, 48.8566),
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var berlin = CreatePoint(13.4050, 52.5200);
            var matchingIds = await context
                .PointEntities.Where(entity => EF.Functions.DistanceSphere(entity.Location, berlin) < 500)
                .Select(entity => entity.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var loadedBerlin = await context
                .PointEntities.SingleAsync(entity => entity.Id == 1)
                .ConfigureAwait(false);

            Assert.Equal([1], matchingIds);
            Assert.Equal(4326, loadedBerlin.Location.SRID);
            Assert.Equal(13.4050, loadedBerlin.Location.X, 3);
            Assert.Equal(52.5200, loadedBerlin.Location.Y, 3);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that the MariaDB 11.8 runtime executes the approved spatial helper baseline.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_spatial_helpers_succeed()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMariaDbOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PolygonTableName}` (
                         `Id` int NOT NULL,
                         `Region` polygon NOT NULL,
                         CONSTRAINT `PK_{PolygonTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            context.PolygonEntities.AddRange(
                new SpatialPolygonEntity
                {
                    Id = 1,
                    Region = CreatePolygon(13.4040, 52.5190, 13.4060, 52.5210),
                },
                new SpatialPolygonEntity
                {
                    Id = 2,
                    Region = CreatePolygon(2.3510, 48.8550, 2.3530, 48.8570),
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var searchPolygon = CreatePolygon(13.4045, 52.5195, 13.4055, 52.5205);

            var matchingIds = await context
                .PolygonEntities.Where(entity => EF.Functions.MbrIntersects(entity.Region, searchPolygon))
                .Select(entity => entity.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var loadedRegion = await context
                .PolygonEntities.SingleAsync(entity => entity.Id == 1)
                .ConfigureAwait(false);

            Assert.Equal([1], matchingIds);
            Assert.Equal(4326, loadedRegion.Region.SRID);
            Assert.False(loadedRegion.Region.IsEmpty);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MariaDB 11.8 WKB round-trip: persist a Point, materialize it via a
    /// post-clear fresh read, assert SRID and coordinates survive. MariaDB does
    /// not wrap spatial column reads as <see cref="MySqlGeometry"/>; the value
    /// arrives as raw <c>byte[]</c>. The MariaDB-flavored TypeMapping accepts
    /// both canonical OGC WKB and the MySQL-style SRID-prefixed layout via
    /// <see cref="MariaDbNetTopologySuiteGeometryTypeMapping{TGeometry}.ConvertFromWkbBytes"/>;
    /// this test pins both the engine-aware mapping selection and the byte-layout
    /// handling end-to-end against the live MariaDB server.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_wkb_roundtrip_preserves_srid_and_coordinates()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMariaDbOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PointTableName}` (
                         `Id` int NOT NULL,
                         `Location` point NOT NULL,
                         CONSTRAINT `PK_{PointTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            var berlin = CreatePoint(13.4050, 52.5200);
            context.PointEntities.Add(
                new SpatialPointEntity
                {
                    Id = 1,
                    Location = berlin,
                });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();
            var roundtripped = await context
                .PointEntities.SingleAsync(entity => entity.Id == 1)
                .ConfigureAwait(false);

            Assert.Equal(4326, roundtripped.Location.SRID);
            Assert.Equal(13.4050, roundtripped.Location.X, 3);
            Assert.Equal(52.5200, roundtripped.Location.Y, 3);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// MariaDB 11.8 spatial-index DDL: the provider's CREATE SPATIAL INDEX form
    /// must execute against the live MariaDB server. The test creates a table,
    /// emits a spatial index via the migration generator, runs it, then queries
    /// information_schema to confirm the index landed.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_spatial_index_ddl_creates_index_on_geometry_column()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        await ResetSpatialObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialDbContext(CreateMariaDbOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{PointTableName}` (
                         `Id` int NOT NULL,
                         `Location` point NOT NULL,
                         CONSTRAINT `PK_{PointTableName}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            var generator = context.GetService<IMigrationsSqlGenerator>();
            var indexOperation = new CreateIndexOperation
            {
                Name = "IX_Spatial_Location",
                Table = PointTableName,
                Columns = ["Location"],
            };
            indexOperation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);

            var commands = generator.Generate([indexOperation], context.Model);
            Assert.Single(commands);
            Assert.Contains("SPATIAL INDEX", commands[0].CommandText, StringComparison.OrdinalIgnoreCase);

            await context
                .Database.ExecuteSqlRawAsync(commands[0].CommandText)
                .ConfigureAwait(false);

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var verify = connection.CreateCommand();
            verify.CommandText =
                $"SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = '{PointTableName}' AND index_name = 'IX_Spatial_Location';";
            var indexCount = Convert.ToInt32(await verify.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
            Assert.True(indexCount > 0);
        }
        finally
        {
            await ResetSpatialObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// SRID-mismatch warning: when the spatial column declares an explicit SRID
    /// via HasSrid and the Distance argument carries a different SRID, the
    /// translator walks the ColumnExpression to its IProperty annotation and
    /// fires the warning at translation time. MySQL would reject the mismatch
    /// with a hard error on execution; MariaDB silently treats both inputs as
    /// Cartesian. The test runs in-process (no live server) because the warning
    /// fires before any SQL leaves the provider.
    /// </summary>
    [Fact]
    public async Task TranslateDistance_warns_when_column_and_constant_srids_differ()
    {
        var sink = new SridWarningSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new SridWarningLoggerProvider(sink)));

        var builder = new DbContextOptionsBuilder<SridWarningContext>();
        builder
            .UseLoggerFactory(loggerFactory)
            .UseMySql(
                "Server=localhost;Database=stub;User ID=stub;Password=stub;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)),
                options => options.UseNetTopologySuite());

        await using var context = new SridWarningContext(builder.Options);

        _ = context
            .Points.Select(entity => entity.Location.Distance(new Point(0, 0) { SRID = 0 }))
            .ToQueryString();

        Assert.Contains(sink.Entries, entry => entry.EventId.Id == MySqlEventId.SpatialSridMismatchDetected.Id);
    }

    [Fact]
    public async Task TranslateDistance_does_not_warn_when_column_and_constant_srids_match()
    {
        var sink = new SridWarningSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new SridWarningLoggerProvider(sink)));

        var builder = new DbContextOptionsBuilder<SridWarningContext>();
        builder
            .UseLoggerFactory(loggerFactory)
            .UseMySql(
                "Server=localhost;Database=stub;User ID=stub;Password=stub;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)),
                options => options.UseNetTopologySuite());

        await using var context = new SridWarningContext(builder.Options);

        _ = context
            .Points.Select(entity => entity.Location.Distance(new Point(0, 0) { SRID = 4326 }))
            .ToQueryString();

        Assert.DoesNotContain(sink.Entries, entry => entry.EventId.Id == MySqlEventId.SpatialSridMismatchDetected.Id);
    }

    private sealed class SridWarningContext : DbContext
    {
        public SridWarningContext(
            DbContextOptions<SridWarningContext> options
        ) : base(options) { }

        public DbSet<SpatialPointEntity> Points => Set<SpatialPointEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialPointEntity>(entity =>
            {
                entity.ToTable("SridWarningPoints");
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point")
                    .HasSrid(4326);
            });
        }
    }

    private sealed record SridWarningEntry(EventId EventId, string Category, string Message);

    private sealed class SridWarningSink
    {
        public List<SridWarningEntry> Entries { get; } = new();
    }

    private sealed class SridWarningLoggerProvider : ILoggerProvider
    {
        private readonly SridWarningSink _sink;

        public SridWarningLoggerProvider(
            SridWarningSink sink
        ) => _sink = sink;

        public ILogger CreateLogger(
            string categoryName
        ) => new SridWarningLogger(_sink, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class SridWarningLogger : ILogger
    {
        private readonly SridWarningSink _sink;
        private readonly string _category;

        public SridWarningLogger(
            SridWarningSink sink,
            string category
        )
        {
            _sink = sink;
            _category = category;
        }

        public IDisposable BeginScope<TState>(
            TState state
        ) where TState : notnull => new NullScope();

        public bool IsEnabled(
            LogLevel logLevel
        ) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            ArgumentNullException.ThrowIfNull(formatter);
            _sink.Entries.Add(new SridWarningEntry(eventId, _category, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static DbContextOptions<SpatialDbContext> CreateMySqlOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<SpatialDbContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    private static DbContextOptions<SpatialDbContext> CreateMariaDbOptions(
        string connectionString
    )
    {
        var builder = new DbContextOptionsBuilder<SpatialDbContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    private static async Task ResetSpatialObjectsAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{PointTableName}`;
                               DROP TABLE IF EXISTS `{PolygonTableName}`;
                               """;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static Point CreatePoint(
        double x,
        double y
    )
    {
        return new Point(x, y)
        {
            SRID = 4326,
        };
    }

    private static Polygon CreatePolygon(
        double minX,
        double minY,
        double maxX,
        double maxY
    )
    {
        var polygon = new Polygon(
            new LinearRing(
            [
                new Coordinate(minX, minY),
                new Coordinate(maxX, minY),
                new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY),
                new Coordinate(minX, minY),
            ]))
        {
            SRID = 4326,
        };

        return polygon;
    }

    private sealed class SpatialDbContext : DbContext
    {
        public SpatialDbContext(
            DbContextOptions<SpatialDbContext> options
        ) : base(options) { }

        public DbSet<SpatialPointEntity> PointEntities => Set<SpatialPointEntity>();

        public DbSet<SpatialPolygonEntity> PolygonEntities => Set<SpatialPolygonEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialPointEntity>(entity =>
            {
                entity.ToTable(PointTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point");
            });

            modelBuilder.Entity<SpatialPolygonEntity>(entity =>
            {
                entity.ToTable(PolygonTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Region)
                    .HasColumnType("polygon");
            });
        }
    }

    private sealed class SpatialPointEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = null!;
    }

    private sealed class SpatialPolygonEntity
    {
        public int Id { get; set; }

        public Polygon Region { get; set; } = null!;
    }
}
