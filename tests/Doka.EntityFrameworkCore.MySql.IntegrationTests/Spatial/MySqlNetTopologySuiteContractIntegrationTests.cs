namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the spatial contracts that differ by driver shape or engine version
/// against live supported servers.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlNetTopologySuiteContractIntegrationTests
{
    private const string MaterializationTable = "DokaSpatialMaterialization";
    private const string MaterializationChildTable = "DokaSpatialMaterializationChild";
    private const string CrossesTable = "DokaSpatialCrosses";
    private const string SridTable = "DokaSpatialSrid";

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_materializes_every_geometry_type_across_query_shapes() =>
        AssertMaterializationContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_materializes_every_geometry_type_across_query_shapes() =>
        AssertMaterializationContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_crosses_matches_nts_for_every_dimension_order() =>
        AssertMariaDbCrossesContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_crosses_matches_nts_for_every_dimension_order() =>
        AssertMariaDbCrossesContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_crosses_matches_nts_for_every_dimension_order() =>
        AssertMariaDbCrossesContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_crosses_matches_nts_for_every_dimension_order() =>
        AssertMariaDbCrossesContractAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_executes_the_supported_spatial_function_contract() =>
        AssertSupportedFunctionContractAsync(IntegrationDatabaseTarget.MySql84);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_executes_the_supported_spatial_function_contract() =>
        AssertSupportedFunctionContractAsync(IntegrationDatabaseTarget.MySql97);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_executes_the_supported_spatial_function_contract() =>
        AssertSupportedFunctionContractAsync(IntegrationDatabaseTarget.MariaDb123);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_srid_check_migrates_enforces_and_scaffolds() =>
        AssertMariaDbSridContractAsync(IntegrationDatabaseTarget.MariaDb1011);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_srid_check_migrates_enforces_and_scaffolds() =>
        AssertMariaDbSridContractAsync(IntegrationDatabaseTarget.MariaDb114);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_srid_check_migrates_enforces_and_scaffolds() =>
        AssertMariaDbSridContractAsync(IntegrationDatabaseTarget.MariaDb118);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_srid_check_migrates_enforces_and_scaffolds() =>
        AssertMariaDbSridContractAsync(IntegrationDatabaseTarget.MariaDb123);

    private static async Task AssertMariaDbSridContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await DropContractTablesAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var source = new EmptySpatialContractContext(
                CreateOptions<EmptySpatialContractContext>(connectionString, target));

            await using var targetContext = new SpatialSridContractContext(
                CreateOptions<SpatialSridContractContext>(connectionString, target));

            var operations = targetContext
                .GetService<IMigrationsModelDiffer>()
                .GetDifferences(
                    source
                        .GetService<IDesignTimeModel>()
                        .Model
                        .GetRelationalModel(),
                    targetContext
                        .GetService<IDesignTimeModel>()
                        .Model
                        .GetRelationalModel());

            var commands = targetContext
                .GetService<IMigrationsSqlGenerator>()
                .Generate(operations, targetContext.Model);

            var sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));

            Assert.Contains("CHECK (ST_SRID(`Location`) = 4326)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("point SRID 4326", sql, StringComparison.OrdinalIgnoreCase);

            foreach (var command in commands)
            {
                await targetContext
                    .Database
                    .ExecuteSqlRawAsync(command.CommandText)
                    .ConfigureAwait(false);
            }

            var showCreate = await ReadShowCreateTableAsync(connectionString, SridTable)
                .ConfigureAwait(false);

            Assert.Contains("CHECK (srid(`Location`) = 4326)", showCreate, StringComparison.OrdinalIgnoreCase);

            var exception = await Assert.ThrowsAsync<MySqlException>(() => targetContext.Database.ExecuteSqlRawAsync(
                $"INSERT INTO `{SridTable}` (`Id`, `Location`) VALUES (1, ST_GeomFromText('POINT (1 2)', 0));"));

            Assert.Equal(4025, exception.Number);

            await using var serviceProvider = ScaffoldingTestServices.CreateDesignTimeServiceProvider(
                includeSpatialServices: true);

            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            var databaseOptions = new DatabaseModelFactoryOptions([SridTable], Array.Empty<string>());
            var databaseModel = services
                .GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, databaseOptions);

            var table = Assert.Single(databaseModel.Tables);
            var location = Assert.Single(table.Columns, column => column.Name == "Location");

            Assert.Equal(
                4326,
                location.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                    ?.Value);
            Assert.Null(table.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints));

            var scaffoldedModel = services
                .GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    databaseOptions,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));

            Assert.Contains(".HasSrid(4326)", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
            Assert.DoesNotContain("HasCheckConstraint", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        }
        finally
        {
            await DropContractTablesAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertMaterializationContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await DropContractTablesAsync(connectionString).ConfigureAwait(false);
        var expected = CreateGeometrySet();

        try
        {
            await using var context = new SpatialMaterializationContext(
                CreateOptions<SpatialMaterializationContext>(connectionString, target));

            await context
                .Database
                .ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{MaterializationTable}` (
                         `Id` int NOT NULL,
                         `Point` point NOT NULL,
                         `LineString` linestring NOT NULL,
                         `Polygon` polygon NOT NULL,
                         `MultiPoint` multipoint NOT NULL,
                         `MultiLineString` multilinestring NOT NULL,
                         `MultiPolygon` multipolygon NOT NULL,
                         `GeometryCollection` geometrycollection NOT NULL,
                         `Geometry` geometry NOT NULL,
                         CONSTRAINT `PK_{MaterializationTable}` PRIMARY KEY (`Id`)
                     );
                     CREATE TABLE `{MaterializationChildTable}` (
                         `Id` int NOT NULL,
                         `ParentId` int NOT NULL,
                         CONSTRAINT `PK_{MaterializationChildTable}` PRIMARY KEY (`Id`),
                         CONSTRAINT `FK_{MaterializationChildTable}` FOREIGN KEY (`ParentId`)
                             REFERENCES `{MaterializationTable}` (`Id`) ON DELETE CASCADE
                     );
                     """)
                .ConfigureAwait(false);

            context.Entities.Add(
                new SpatialMaterializationEntity
                {
                    Id = 1,
                    Point = expected.Point,
                    LineString = expected.LineString,
                    Polygon = expected.Polygon,
                    MultiPoint = expected.MultiPoint,
                    MultiLineString = expected.MultiLineString,
                    MultiPolygon = expected.MultiPolygon,
                    GeometryCollection = expected.GeometryCollection,
                    Geometry = expected.Geometry,
                    Children = [new SpatialMaterializationChild { Id = 1 }],
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var tracked = await context
                .Entities
                .SingleAsync()
                .ConfigureAwait(false);

            AssertGeometrySet(expected, tracked);

            context.ChangeTracker.Clear();
            var untracked = await context
                .Entities
                .AsNoTracking()
                .SingleAsync()
                .ConfigureAwait(false);

            AssertGeometrySet(expected, untracked);

            var scalar = await context
                .Entities
                .AsNoTracking()
                .Select(entity => new SpatialGeometrySet(
                    entity.Point,
                    entity.LineString,
                    entity.Polygon,
                    entity.MultiPoint,
                    entity.MultiLineString,
                    entity.MultiPolygon,
                    entity.GeometryCollection,
                    entity.Geometry))
                .SingleAsync()
                .ConfigureAwait(false);

            AssertGeometrySet(expected, scalar);

            var included = await context
                .Entities
                .AsNoTracking()
                .Include(entity => entity.Children)
                .SingleAsync()
                .ConfigureAwait(false);

            AssertGeometrySet(expected, included);

            Assert.Single(included.Children);

            var split = await context
                .Entities
                .AsNoTracking()
                .AsSplitQuery()
                .Include(entity => entity.Children)
                .SingleAsync()
                .ConfigureAwait(false);

            AssertGeometrySet(expected, split);

            Assert.Single(split.Children);
        }
        finally
        {
            await DropContractTablesAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertMariaDbCrossesContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await DropContractTablesAsync(connectionString)
            .ConfigureAwait(false);
        var cases = CreateCrossesCases();

        try
        {
            await using var context = new SpatialCrossesContext(
                CreateOptions<SpatialCrossesContext>(connectionString, target));

            await context
                .Database
                .ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{CrossesTable}` (
                         `Id` int NOT NULL,
                         `LeftGeometry` geometry NOT NULL,
                         `RightGeometry` geometry NOT NULL,
                         CONSTRAINT `PK_{CrossesTable}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);

            context.Rows.AddRange(
                cases.Select((
                    pair,
                    index
                ) => new SpatialCrossesRow
                {
                    Id = index + 1,
                    LeftGeometry = pair.Left,
                    RightGeometry = pair.Right,
                }));
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var actual = await context
                .Rows
                .OrderBy(row => row.Id)
                .Select(row => row.LeftGeometry.Crosses(row.RightGeometry))
                .ToListAsync()
                .ConfigureAwait(false);

            var expected = cases
                .Select(pair => pair.Left.Crosses(pair.Right))
                .ToArray();

            Assert.Contains(true, expected);
            Assert.Contains(false, expected);
            Assert.Equal(expected, actual);
        }
        finally
        {
            await DropContractTablesAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertSupportedFunctionContractAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        await DropContractTablesAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new SpatialCrossesContext(
                CreateOptions<SpatialCrossesContext>(connectionString, target));

            await context
                .Database
                .ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{CrossesTable}` (
                         `Id` int NOT NULL,
                         `LeftGeometry` geometry NOT NULL,
                         `RightGeometry` geometry NOT NULL,
                         CONSTRAINT `PK_{CrossesTable}` PRIMARY KEY (`Id`)
                     );
                     """)
                .ConfigureAwait(false);
            var geometry = ReadGeometry("LINESTRING (0 0, 2 0)", spatialReferenceSystemId: 0);
            context.Rows.Add(
                new SpatialCrossesRow
                {
                    Id = 1,
                    LeftGeometry = geometry,
                    RightGeometry = ReadGeometry("LINESTRING (1 -1, 1 1)", spatialReferenceSystemId: 0),
                });
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            Assert.True(
                await context
                    .Rows
                    .Select(row => row.LeftGeometry.IsValid)
                    .SingleAsync()
                    .ConfigureAwait(false));

            var buffered = target == IntegrationDatabaseTarget.MariaDb123
                ? await context
                    .Rows
                    .Select(row => row.LeftGeometry.Buffer(1))
                    .SingleAsync()
                    .ConfigureAwait(false)
                : await context
                    .Rows
                    .Select(row => row.LeftGeometry.Buffer(1, 4))
                    .SingleAsync()
                    .ConfigureAwait(false);

            Assert.False(buffered.IsEmpty);

            var combined = await context
                .Rows
                .GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Geometries.Utilities.GeometryCombiner.Combine(
                    group.Select(row => row.LeftGeometry)))
                .SingleAsync()
                .ConfigureAwait(false);

            var union = await context
                .Rows
                .GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(
                    group.Select(row => row.LeftGeometry)))
                .SingleAsync()
                .ConfigureAwait(false);

            var envelope = await context
                .Rows
                .GroupBy(_ => 1)
                .Select(group => NetTopologySuite.Geometries.Utilities.EnvelopeCombiner.CombineAsGeometry(
                    group.Select(row => row.LeftGeometry)))
                .SingleAsync()
                .ConfigureAwait(false);

            Assert.False(combined.IsEmpty);
            Assert.False(union.IsEmpty);
            Assert.False(envelope.IsEmpty);
        }
        finally
        {
            await DropContractTablesAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        IntegrationDatabaseTarget target
    )
        where TContext : DbContext
    {
        var builder = IntegrationTestDbContextOptions.Create<TContext>();
        builder.UseMySql(
            connectionString,
            IntegrationTestEnvironment.GetServerVersion(target),
            options => options.UseNetTopologySuite());
        return builder.Options;
    }

    private static SpatialGeometrySet CreateGeometrySet() => new(
        (Point)ReadGeometry("POINT (1 2)"),
        (LineString)ReadGeometry("LINESTRING (0 0, 1 1)"),
        (Polygon)ReadGeometry("POLYGON ((0 0, 0 2, 2 2, 0 0))"),
        (MultiPoint)ReadGeometry("MULTIPOINT ((0 0), (1 1))"),
        (MultiLineString)ReadGeometry("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))"),
        (MultiPolygon)ReadGeometry("MULTIPOLYGON (((0 0, 0 2, 2 2, 0 0)))"),
        (GeometryCollection)ReadGeometry("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))"),
        ReadGeometry("POINT (3 4)"));

    private static (Geometry Left, Geometry Right)[] CreateCrossesCases()
    {
        var point = ReadGeometry("POINT (1 0)");
        var multiPoint = ReadGeometry("MULTIPOINT ((1 0), (5 5))");
        var line = ReadGeometry("LINESTRING (0 0, 2 0)");
        var multiLine = ReadGeometry("MULTILINESTRING ((0 0, 2 0), (5 5, 6 6))");
        var crossingLine = ReadGeometry("LINESTRING (1 -1, 1 3)");
        var polygon = ReadGeometry("POLYGON ((0 0, 0 2, 2 2, 2 0, 0 0))");
        var multiPolygon = ReadGeometry("MULTIPOLYGON (((0 0, 0 2, 2 2, 2 0, 0 0)), ((5 5, 5 6, 6 6, 6 5, 5 5)))");

        return
        [
            (point, line),
            (line, point),
            (multiPoint, multiLine),
            (multiLine, multiPoint),
            (point, polygon),
            (polygon, point),
            (line, crossingLine),
            (crossingLine, line),
            (crossingLine, polygon),
            (polygon, crossingLine),
            (multiLine, multiPolygon),
            (multiPolygon, multiLine),
            (polygon, multiPolygon),
            (multiPolygon, polygon),
        ];
    }

    private static Geometry ReadGeometry(
        string wkt,
        int spatialReferenceSystemId = 4326
    )
    {
        var geometry = new NetTopologySuite.IO.WKTReader().Read(wkt);
        geometry.SRID = spatialReferenceSystemId;
        return geometry;
    }

    private static void AssertGeometrySet(
        SpatialGeometrySet expected,
        SpatialMaterializationEntity actual
    ) => AssertGeometrySet(
        expected,
        new SpatialGeometrySet(
            actual.Point,
            actual.LineString,
            actual.Polygon,
            actual.MultiPoint,
            actual.MultiLineString,
            actual.MultiPolygon,
            actual.GeometryCollection,
            actual.Geometry));

    private static void AssertGeometrySet(
        SpatialGeometrySet expected,
        SpatialGeometrySet actual
    )
    {
        var expectedValues = expected.Values;
        var actualValues = actual.Values;

        Assert.Equal(expectedValues.Length, actualValues.Length);

        for (var index = 0; index < expectedValues.Length; index++)
        {
            Assert.Equal(
                expectedValues[index]
                    .GetType(),
                actualValues[index]
                    .GetType());
            Assert.Equal(4326, actualValues[index].SRID);
            Assert.True(
                expectedValues[index]
                    .EqualsExact(actualValues[index]));
        }
    }

    private static async Task<string> ReadShowCreateTableAsync(
        string connectionString,
        string tableName
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE `{tableName}`;";
        await using var reader = await command
            .ExecuteReaderAsync()
            .ConfigureAwait(false);

        Assert.True(
            await reader
                .ReadAsync()
                .ConfigureAwait(false));
        return reader.GetString(1);
    }

    private static async Task DropContractTablesAsync(
        string connectionString
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{MaterializationChildTable}`;
                               DROP TABLE IF EXISTS `{MaterializationTable}`;
                               DROP TABLE IF EXISTS `{CrossesTable}`;
                               DROP TABLE IF EXISTS `{SridTable}`;
                               """;
        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private sealed record SpatialGeometrySet(
        Point Point,
        LineString LineString,
        Polygon Polygon,
        MultiPoint MultiPoint,
        MultiLineString MultiLineString,
        MultiPolygon MultiPolygon,
        GeometryCollection GeometryCollection,
        Geometry Geometry
    )
    {
        public Geometry[] Values =>
        [
            Point,
            LineString,
            Polygon,
            MultiPoint,
            MultiLineString,
            MultiPolygon,
            GeometryCollection,
            Geometry,
        ];
    }

    private sealed class SpatialMaterializationContext : DbContext
    {
        public SpatialMaterializationContext(
            DbContextOptions<SpatialMaterializationContext> options
        ) : base(options) { }

        public DbSet<SpatialMaterializationEntity> Entities => Set<SpatialMaterializationEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialMaterializationEntity>(entity =>
            {
                entity.ToTable(MaterializationTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Point)
                    .HasColumnType("point");
                entity
                    .Property(item => item.LineString)
                    .HasColumnType("linestring");
                entity
                    .Property(item => item.Polygon)
                    .HasColumnType("polygon");
                entity
                    .Property(item => item.MultiPoint)
                    .HasColumnType("multipoint");
                entity
                    .Property(item => item.MultiLineString)
                    .HasColumnType("multilinestring");
                entity
                    .Property(item => item.MultiPolygon)
                    .HasColumnType("multipolygon");
                entity
                    .Property(item => item.GeometryCollection)
                    .HasColumnType("geometrycollection");
                entity
                    .Property(item => item.Geometry)
                    .HasColumnType("geometry");
                entity
                    .HasMany(item => item.Children)
                    .WithOne()
                    .HasForeignKey(child => child.ParentId);
            });
            modelBuilder.Entity<SpatialMaterializationChild>(entity =>
            {
                entity.ToTable(MaterializationChildTable);
                entity.HasKey(item => item.Id);
            });
        }
    }

    private sealed class SpatialMaterializationEntity
    {
        public int Id { get; set; }
        public Point Point { get; set; } = null!;
        public LineString LineString { get; set; } = null!;
        public Polygon Polygon { get; set; } = null!;
        public MultiPoint MultiPoint { get; set; } = null!;
        public MultiLineString MultiLineString { get; set; } = null!;
        public MultiPolygon MultiPolygon { get; set; } = null!;
        public GeometryCollection GeometryCollection { get; set; } = null!;
        public Geometry Geometry { get; set; } = null!;
        public List<SpatialMaterializationChild> Children { get; set; } = [];
    }

    private sealed class SpatialMaterializationChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private sealed class SpatialCrossesContext : DbContext
    {
        public SpatialCrossesContext(
            DbContextOptions<SpatialCrossesContext> options
        ) : base(options) { }

        public DbSet<SpatialCrossesRow> Rows => Set<SpatialCrossesRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialCrossesRow>(entity =>
            {
                entity.ToTable(CrossesTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.LeftGeometry)
                    .HasColumnType("geometry");
                entity
                    .Property(item => item.RightGeometry)
                    .HasColumnType("geometry");
            });
        }
    }

    private sealed class SpatialCrossesRow
    {
        public int Id { get; set; }
        public Geometry LeftGeometry { get; set; } = null!;
        public Geometry RightGeometry { get; set; } = null!;
    }

    private sealed class EmptySpatialContractContext : DbContext
    {
        public EmptySpatialContractContext(
            DbContextOptions<EmptySpatialContractContext> options
        ) : base(options) { }
    }

    private sealed class SpatialSridContractContext : DbContext
    {
        public SpatialSridContractContext(
            DbContextOptions<SpatialSridContractContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialSridRow>(entity =>
            {
                entity.ToTable(SridTable);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Location)
                    .HasColumnType("point")
                    .HasSrid(4326);
            });
        }
    }

    private sealed class SpatialSridRow
    {
        public int Id { get; set; }
        public Point Location { get; set; } = null!;
    }
}
