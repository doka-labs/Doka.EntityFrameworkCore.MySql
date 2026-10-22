namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMigrationDslTests
{
    /// <summary>
    /// Other provider-owned index kinds use the same physical rebuild boundary.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migrations_model_differ_rebuilds_indexes_for_full_text_transitions(
        bool enableFullText
    )
    {
        // Arrange
        using var plainContext = new PlainTextIndexContext(CreateOptions<PlainTextIndexContext>());
        using var fullTextContext = new FullTextIndexContext(CreateOptions<FullTextIndexContext>());
        DbContext sourceContext = enableFullText ? plainContext : fullTextContext;
        DbContext targetContext = enableFullText ? fullTextContext : plainContext;
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();

        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.True(hasDifferences);
        Assert.Equal(TextIndexContract.IndexName, drop.Name);
        Assert.Equal(
            enableFullText ? true : null,
            create.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        Assert.Contains(
            enableFullText
                ? $"CREATE FULLTEXT INDEX `{TextIndexContract.IndexName}`"
                : $"CREATE INDEX `{TextIndexContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Spatial index-kind transitions rebuild the same physical index identity.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migrations_model_differ_rebuilds_indexes_for_spatial_transitions(
        bool enableSpatial
    )
    {
        // Arrange
        using var plainContext = new PlainSpatialIndexContext(CreateOptions<PlainSpatialIndexContext>());
        using var spatialContext = new SpatialIndexContext(CreateOptions<SpatialIndexContext>());
        DbContext sourceContext = enableSpatial ? plainContext : spatialContext;
        DbContext targetContext = enableSpatial ? spatialContext : plainContext;
        var differ = targetContext.GetService<IMigrationsModelDiffer>();
        var sourceModel = sourceContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var targetModel = targetContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var operations = differ
            .GetDifferences(sourceModel, targetModel)
            .ToList();

        var hasDifferences = differ.HasDifferences(sourceModel, targetModel);
        var sql = GenerateMigrationSql(targetContext, operations);

        // Assert
        var drop = Assert.Single(operations.OfType<DropIndexOperation>());
        var create = Assert.Single(operations.OfType<CreateIndexOperation>());

        Assert.True(hasDifferences);
        Assert.Equal(SpatialIndexContract.IndexName, drop.Name);
        Assert.Equal(
            enableSpatial ? true : null,
            create.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?);
        Assert.True(operations.IndexOf(drop) < operations.IndexOf(create));

        Assert.Contains(
            enableSpatial
                ? $"CREATE SPATIAL INDEX `{SpatialIndexContract.IndexName}`"
                : $"CREATE INDEX `{SpatialIndexContract.IndexName}`",
            sql,
            StringComparison.Ordinal);
    }
}
