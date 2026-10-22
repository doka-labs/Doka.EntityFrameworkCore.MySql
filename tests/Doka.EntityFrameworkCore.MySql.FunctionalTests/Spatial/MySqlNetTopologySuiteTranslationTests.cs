namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the spatial type-mapping and translation baseline.
/// </summary>
public sealed class MySqlNetTopologySuiteTranslationTests
{
    private static readonly (int Major, int Minor)[] s_legacyMariaDbVersions = [(10, 11), (11, 4), (11, 8)];
    private static readonly string[] s_unavailableFunctions =
        ["IsValid", "BufferStrategy", "Combine", "Union", "Envelope"];

    /// <summary>
    /// Verifies that the supported spatial CLR types resolve to the approved geometry-family store types.
    /// </summary>
    [Fact]
    public void Spatial_clr_types_resolve_to_the_expected_geometry_store_types()
    {
        // Arrange
        using var context = new SpatialTranslationContext(CreateOptions());

        // Act
        var entityType = context.Model.FindEntityType(typeof(SpatialEntity))!;

        // Assert
        Assert.Equal("point", entityType.FindProperty(nameof(SpatialEntity.Location))!.GetColumnType());
        Assert.Equal("linestring", entityType.FindProperty(nameof(SpatialEntity.Route))!.GetColumnType());
        Assert.Equal("polygon", entityType.FindProperty(nameof(SpatialEntity.Region))!.GetColumnType());
        Assert.Equal(
            "geometrycollection",
            entityType.FindProperty(nameof(SpatialEntity.ShapeCollection))!.GetColumnType());
        Assert.Equal("geometry", entityType.FindProperty(nameof(SpatialEntity.Shape))!.GetColumnType());
    }

    /// <summary>
    /// Verifies that the approved spatial members, methods, and helper functions translate server-side.
    /// </summary>
    [Fact]
    public void Spatial_members_methods_and_helpers_translate_server_side()
    {
        // Arrange
        using var context = new SpatialTranslationContext(CreateOptions());
        var referencePoint = CreatePoint(13.4050, 52.5200);
        var searchPolygon = CreatePolygon(13.4040, 52.5190, 13.4060, 52.5210);

        // Act
        var sql = context
            .Entities.Where(entity =>
                entity.Region.Contains(searchPolygon)
                && EF.Functions.MbrIntersects(entity.Region, searchPolygon)
                && EF.Functions.DistanceSphere(entity.Location, referencePoint) < 1000)
            .Select(entity => new
            {
                entity.Location.X,
                entity.Location.Y,
                entity.Location.SRID,
                entity.Region.Area,
                RouteText = entity.Route.AsText(),
                RouteBinary = entity.Route.AsBinary(),
                StartPointText = entity.Route.StartPoint.AsText(),
                EndPointText = entity.Route.EndPoint.AsText(),
                PointCount = entity.Route.NumPoints,
                ExteriorRingText = entity.Region.ExteriorRing.AsText(),
                InteriorCount = entity.Region.NumInteriorRings,
                FirstGeometryText = entity
                    .ShapeCollection.GetGeometryN(0)
                    .AsText(),
                FirstPointText = entity
                    .Route.GetPointN(0)
                    .AsText(),
            })
            .ToQueryString();

        // Assert
        Assert.Contains("ST_Contains(", sql, StringComparison.Ordinal);
        Assert.Contains("MBRIntersects(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Distance_Sphere(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_X(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Y(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_SRID(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Area(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_AsText(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_AsBinary(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_StartPoint(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_EndPoint(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_NumPoints(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_ExteriorRing(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_NumInteriorRing(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_GeometryN(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_PointN(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unsupported spatial operations still fail translation explicitly.
    /// </summary>
    [Fact]
    public void Unsupported_spatial_operations_fail_explicitly()
    {
        // Arrange
        using var context = new SpatialTranslationContext(CreateOptions());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Entities.Where(entity => entity.Location.Reverse()
                .IsEmpty)
            .ToQueryString());

        // Assert
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Preserves MySQL's native IsValid, buffer-strategy, and Crosses translations.
    /// </summary>
    [Fact]
    public void MySql_spatial_function_capabilities_translate_at_supported_versions()
    {
        // Arrange
        using var context =
            new SpatialTranslationContext(CreateOptions(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        var query = context.Entities.Select(entity => new
        {
            entity.Shape.IsValid,
            Buffer = entity.Shape.Buffer(1, 4),
            Crosses = entity.Shape.Crosses(entity.Region),
        });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ST_IsValid(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Buffer_Strategy(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Crosses(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ST_Relate(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ELSE 0", sql, StringComparison.Ordinal);
    }

    /// <summary>Identifies every unavailable function for each configured MariaDB version.</summary>
    public static IEnumerable<object[]> UnavailableMariaDbCases
    {
        get
        {
            foreach (var version in s_legacyMariaDbVersions)
            {
                foreach (var operation in s_unavailableFunctions)
                {
                    yield return [version.Major, version.Minor, operation];
                }
            }

            // WHY: MariaDB 12 enables validity and collection aggregates, while
            // the optional Buffer strategy overload remains unsupported.
            yield return [12, 3, "BufferStrategy"];
        }
    }

    /// <summary>Rejects one unavailable function before it can reach a live server.</summary>
    [Theory]
    [MemberData(nameof(UnavailableMariaDbCases))]
    public void MariaDb_rejects_unavailable_spatial_function_at_translation(
        int major,
        int minor,
        string operation
    )
    {
        // Arrange
        using var context =
            new SpatialTranslationContext(CreateOptions(MySqlServerVersion.MariaDb(new Version(major, minor, 0))));

        var query = operation switch
        {
            "IsValid" => () => context.Entities.Where(entity => entity.Shape.IsValid).ToQueryString(),
            "BufferStrategy" => () => context.Entities
                .Where(entity => entity.Shape.Buffer(1, 4).IsEmpty)
                .ToQueryString(),
            _ => CreateAggregateQueryFactory(context, operation),
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(query);

        // Assert
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies one aggregate translator independently on each supporting engine family.</summary>
    [Theory]
    [InlineData(false, "Combine")]
    [InlineData(false, "Union")]
    [InlineData(false, "Envelope")]
    [InlineData(true, "Combine")]
    [InlineData(true, "Union")]
    [InlineData(true, "Envelope")]
    public void Spatial_aggregate_translates_at_supported_version(
        bool mariaDb,
        string operation
    )
    {
        // Arrange
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 2))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        using var context = new SpatialTranslationContext(CreateOptions(version));
        var query = CreateAggregateQueryFactory(context, operation);

        // Act
        var sql = query();

        // Assert
        Assert.Contains("ST_Collect(", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Preserves MariaDB 12 validity and two-argument Buffer translations and
    /// replaces nullable Crosses results with the NetTopologySuite DE-9IM definition.
    /// </summary>
    [Fact]
    public void MariaDb12_uses_its_exact_spatial_function_contract()
    {
        // Arrange
        using var context = new SpatialTranslationContext(
            CreateOptions(MySqlServerVersion.MariaDb(new Version(12, 3, 2))));

        var query = context.Entities.Select(entity => new
        {
            entity.Shape.IsValid,
            Buffer = entity.Shape.Buffer(1),
            Crosses = entity.Shape.Crosses(entity.Region),
        });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("ST_IsValid(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Buffer(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ST_Buffer_Strategy(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ST_Crosses(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Dimension(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Relate(", sql, StringComparison.Ordinal);
        Assert.Contains("T*T******", sql, StringComparison.Ordinal);
        Assert.Contains("T*****T**", sql, StringComparison.Ordinal);
        Assert.Contains("0********", sql, StringComparison.Ordinal);
        Assert.Contains("ELSE 0", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Preserves SQL null propagation before MariaDB's dimension-based Crosses dispatch.
    /// </summary>
    [Fact]
    public void MariaDb_crosses_preserves_null_operands_before_dimension_dispatch()
    {
        // Arrange
        using var context = new SpatialTranslationContext(
            CreateOptions(MySqlServerVersion.MariaDb(new Version(12, 3, 2))));

        // Act
        var sql = context
            .Entities
            .Select(entity => (bool?)entity.OptionalShape!.Crosses(entity.OptionalRegion!))
            .ToQueryString();

        // Assert
        Assert.Contains("`OptionalShape` IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`OptionalRegion` IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("THEN NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Dimension(", sql, StringComparison.Ordinal);
        Assert.Contains("ST_Relate(", sql, StringComparison.Ordinal);
        Assert.Contains("ELSE 0", sql, StringComparison.Ordinal);
    }

    private static DbContextOptions<SpatialTranslationContext> CreateOptions(
        MySqlServerVersion? serverVersion = null
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<SpatialTranslationContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.UseNetTopologySuite());

        return builder.Options;
    }

    /// <summary>Defers execution of exactly one aggregate query until the test's Act phase.</summary>
    private static Func<string> CreateAggregateQueryFactory(
        SpatialTranslationContext context,
        string operation
    ) => operation switch
    {
        "Combine" => () => context.Entities
            .GroupBy(_ => 1)
            .Select(group => NetTopologySuite.Geometries.Utilities.GeometryCombiner.Combine(
                group.Select(entity => entity.Shape)))
            .Where(geometry => geometry.IsEmpty)
            .ToQueryString(),
        "Union" => () => context.Entities
            .GroupBy(_ => 1)
            .Select(group => NetTopologySuite.Operation.Union.UnaryUnionOp.Union(group.Select(entity => entity.Shape)))
            .Where(geometry => geometry.IsEmpty)
            .ToQueryString(),
        "Envelope" => () => context.Entities
            .GroupBy(_ => 1)
            .Select(group => NetTopologySuite.Geometries.Utilities.EnvelopeCombiner.CombineAsGeometry(
                group.Select(entity => entity.Shape)))
            .Where(geometry => geometry.IsEmpty)
            .ToQueryString(),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

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

    private sealed class SpatialTranslationContext : DbContext
    {
        public SpatialTranslationContext(
            DbContextOptions<SpatialTranslationContext> options
        ) : base(options) { }

        public DbSet<SpatialEntity> Entities => Set<SpatialEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SpatialEntity>(entity =>
            {
                entity.ToTable("Phase3SpatialEntities");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Location);
                entity.Property(item => item.Route);
                entity.Property(item => item.Region);
                entity.Property(item => item.ShapeCollection);
                entity.Property(item => item.Shape);
                entity.Property(item => item.OptionalShape);
                entity.Property(item => item.OptionalRegion);
            });
        }
    }

    private sealed class SpatialEntity
    {
        public int Id { get; set; }

        public Point Location { get; set; } = default!;

        public LineString Route { get; set; } = default!;

        public Polygon Region { get; set; } = default!;

        public GeometryCollection ShapeCollection { get; set; } = default!;

        public Geometry Shape { get; set; } = default!;

        public Geometry? OptionalShape { get; set; }

        public Geometry? OptionalRegion { get; set; }
    }
}
