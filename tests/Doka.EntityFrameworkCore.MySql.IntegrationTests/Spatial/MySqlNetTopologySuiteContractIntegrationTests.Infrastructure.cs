namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlNetTopologySuiteContractIntegrationTests
{
    /// <summary>Retains one schema owner across seed, query, and teardown partials.</summary>
    private static string GetConnectionString(
        IntegrationDatabaseTarget target,
        string purpose
    )
    {
        // WHY: Dataset ownership must not depend on which topic file resolves
        // the connection through IntegrationTestEnvironment's CallerFilePath default.
        return IntegrationTestEnvironment.GetConnectionString(
            target,
            $"{nameof(MySqlNetTopologySuiteContractIntegrationTests)}.{purpose}");
    }

    /// <summary>Enables spatial services for the selected engine and fixture-owned schema.</summary>
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

    /// <summary>Creates fresh mutable geometry values for one mixed-row roundtrip.</summary>
    private static SpatialGeometrySet CreateGeometrySet() => new(
        (Point)ReadGeometry("POINT (1 2)"),
        (LineString)ReadGeometry("LINESTRING (0 0, 1 1)"),
        (Polygon)ReadGeometry("POLYGON ((0 0, 0 2, 2 2, 0 0))"),
        (MultiPoint)ReadGeometry("MULTIPOINT ((0 0), (1 1))"),
        (MultiLineString)ReadGeometry("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))"),
        (MultiPolygon)ReadGeometry("MULTIPOLYGON (((0 0, 0 2, 2 2, 0 0)))"),
        (GeometryCollection)ReadGeometry("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))"),
        ReadGeometry("POINT (3 4)"));

    /// <summary>Includes both operand orders for each point/line/area dimension boundary.</summary>
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

    /// <summary>Reads a fixture geometry with the explicitly required SRID.</summary>
    private static Geometry ReadGeometry(
        string wkt,
        int spatialReferenceSystemId = 4326
    )
    {
        var geometry = new NetTopologySuite.IO.WKTReader().Read(wkt);
        geometry.SRID = spatialReferenceSystemId;

        return geometry;
    }

    /// <summary>Asserts all values returned by one mixed-geometry row/shaper.</summary>
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
            Assert.Equal(expectedValues[index].GetType(), actualValues[index].GetType());
            Assert.Equal(4326, actualValues[index].SRID);
            Assert.True(expectedValues[index].EqualsExact(actualValues[index]));
        }
    }

    /// <summary>Captures table DDL and row presence before the lifecycle's assertion phase.</summary>
    private static async Task<(bool HasRow, string? Sql)> ReadShowCreateTableAsync(
        string connectionString,
        string tableName
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE `{tableName}`;";
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var hasRow = await reader.ReadAsync().ConfigureAwait(false);

        return (hasRow, hasRow ? reader.GetString(1) : null);
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
}
