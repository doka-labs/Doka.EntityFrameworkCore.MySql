namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the exact MariaDB column-CHECK shape owned by spatial SRID scaffolding.
/// </summary>
public sealed class MariaDbSpatialSridCheckConstraintParserTests
{
    /// <summary>
    /// Verifies provider owned srid checks roundtrip.
    /// </summary>
    [Theory]
    [InlineData("srid(`Location`) = 4326", "Location", 4326)]
    [InlineData("ST_SRID ( `Route``point` )=0", "Route`point", 0)]
    public void Provider_owned_srid_checks_roundtrip(
        string sql,
        string expectedColumn,
        int expectedSrid
    )
    {
        // Arrange: The theory supplies sql, expectedColumn, expectedSrid.

        // Act
        var parsed = MariaDbSpatialSridCheckConstraintParser.TryParse(
            sql,
            out var columnName,
            out var spatialReferenceSystemId);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectedColumn, columnName);
        Assert.Equal(expectedSrid, spatialReferenceSystemId);
    }

    /// <summary>
    /// Verifies user or malformed checks are not consumed as spatial metadata.
    /// </summary>
    [Theory]
    [InlineData("srid(Location) = 4326")]
    [InlineData("srid(`Location`) <> 4326")]
    [InlineData("srid(`Location`) = -1")]
    [InlineData("srid(`Location`) = 04326")]
    [InlineData("srid(`Location`) = 4326 AND 1 = 1")]
    [InlineData("json_valid(`Location`)")]
    public void User_or_malformed_checks_are_not_consumed_as_spatial_metadata(
        string sql
    )
    {
        // Arrange: The theory supplies sql.

        // Act
        var parsed = MariaDbSpatialSridCheckConstraintParser.TryParse(
            sql,
            out var columnName,
            out var spatialReferenceSystemId);

        // Assert
        Assert.False(parsed);
        Assert.Null(columnName);
        Assert.Equal(0, spatialReferenceSystemId);
    }
}
