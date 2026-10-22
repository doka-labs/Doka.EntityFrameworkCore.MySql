namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
{
    /// <summary>Verifies the tracked write/read lifecycle independently of untracked reader paths.</summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MariaDb118 })]
    [MemberData(nameof(MaterializationTargetCases))]
    public async Task Geometry_types_round_trip_through_tracked_query(
        string target
    )
    {
        // Arrange
        var engine = Enum.Parse<IntegrationDatabaseTarget>(target);
        var connectionString = GetConnectionString(engine, "TrackedLifecycle");
        await DropContractTablesAsync(connectionString).ConfigureAwait(false);
        var expected = CreateGeometrySet();
        await using var context = new SpatialMaterializationContext(
            CreateOptions<SpatialMaterializationContext>(connectionString, engine));

        var entity = CreateMaterializationEntity(expected);

        try
        {
            await CreateMaterializationTablesAsync(context).ConfigureAwait(false);

            // WHY: This case intentionally retains tracking across SaveChanges
            // and readback; independent cases exercise the fresh-reader shapes.
            // Act
            // WHY: Explicit fixture keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context.Entities.Add(entity);
            await context.SaveChangesAsync().ConfigureAwait(false);
            var actual = await context.Entities.SingleAsync().ConfigureAwait(false);
            var geometry = ToGeometrySet(actual);

            // Assert
            AssertGeometrySet(expected, geometry);
        }
        finally
        {
            await DropContractTablesAsync(connectionString).ConfigureAwait(false);
        }
    }

    /// <summary>Verifies one independently materialized geometry-row shape.</summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MariaDb118 })]
    [MemberData(nameof(MaterializationCases))]
    public async Task Geometry_types_materialize_through_query_shape(
        string target,
        string shape
    )
    {
        // Arrange
        await using var context = await _fixture.CreateMaterializationContextAsync(
            Enum.Parse<IntegrationDatabaseTarget>(target));
        var expected = CreateGeometrySet();
        var query = context.Entities.AsNoTracking();

        // Act
        SpatialGeometrySet actual;
        int? childCount = null;

        if (shape == "Scalar")
        {
            actual = await query.Select(entity => new SpatialGeometrySet(
                entity.Point,
                entity.LineString,
                entity.Polygon,
                entity.MultiPoint,
                entity.MultiLineString,
                entity.MultiPolygon,
                entity.GeometryCollection,
                entity.Geometry)).SingleAsync().ConfigureAwait(false);
        }
        else
        {
            var shapedQuery = shape switch
            {
                "Untracked" => query,
                "Include" => query.Include(entity => entity.Children),
                "SplitInclude" => query.AsSplitQuery().Include(entity => entity.Children),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

            var entity = await shapedQuery.SingleAsync().ConfigureAwait(false);
            actual = ToGeometrySet(entity);
            childCount = entity.Children.Count;
        }

        // Assert
        // WHY: All eight geometry families belong to the same row/shaper;
        // checking them together preserves the mixed-column reader contract.
        AssertGeometrySet(expected, actual);

        if (shape is "Include" or "SplitInclude")
        {
            Assert.Equal(1, childCount);
        }
    }

    private static SpatialGeometrySet ToGeometrySet(
        SpatialMaterializationEntity entity
    ) => new(
        entity.Point,
        entity.LineString,
        entity.Polygon,
        entity.MultiPoint,
        entity.MultiLineString,
        entity.MultiPolygon,
        entity.GeometryCollection,
        entity.Geometry);
}
