namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Explicit invisible and visible column dispositions survive both generated
    /// design-time model surfaces without falling back to raw annotations.
    /// </summary>
    [Fact]
    public void Invisible_column_snapshot_and_designer_roundtrip_without_pending_operations()
    {
        // Arrange
        using var context = new InvisibleColumnDesignContext(
            CreateOptions<InvisibleColumnDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".IsInvisible()", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible()", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".IsInvisible(false)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(MySqlAnnotationNames.Invisible, generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>
    /// Spatial SRID metadata survives generated snapshots on both the native MySQL
    /// route and the MariaDB CHECK-emulation route without a pending migration.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Spatial_srid_snapshot_and_designer_roundtrip_without_pending_operations(
        bool mariaDb
    )
    {
        // Arrange
        var serverVersion = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        using var context = new SpatialSridDesignContext(CreateSpatialOptions(serverVersion));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceMigration = CaptureInitialMigration(context, context.GetService<IDesignTimeModel>().Model);
        var snapshotMigration = CaptureInitialMigration(context, generated.SnapshotModel);
        var designerMigration = CaptureInitialMigration(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".HasSrid(4326)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasSrid(4326)", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(
            ".Property<NetTopologySuite.Geometries.Point>(\"Location\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<NetTopologySuite.Geometries.Point>(\"Location\")",
            generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<MySqlGeometry>(\"Location\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<MySqlGeometry>(\"Location\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            MySqlAnnotationNames.SpatialReferenceSystemId,
            generated.SnapshotCode,
            StringComparison.Ordinal);
        AssertSpatialSeedModelValue(generated.SnapshotModel);
        AssertSpatialSeedModelValue(generated.DesignerModel);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertSpatialSeedDataOperations(sourceMigration);
        AssertSpatialSeedDataOperations(snapshotMigration);
        AssertSpatialSeedDataOperations(designerMigration);
    }

    /// <summary>
    /// Entity-splitting snapshots retain one generated principal key without
    /// turning the secondary shared primary/foreign key into a generator.
    /// </summary>
    [Fact]
    public void Entity_splitting_snapshot_and_designer_preserve_principal_generation_ownership()
    {
        // Arrange
        var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
        using var context = new EntitySplitDesignContext(CreateOptions<EntitySplitDesignContext>(serverVersion));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceOperations = CaptureTableCreation(context, context.GetService<IDesignTimeModel>().Model, serverVersion);
        var snapshotOperations = CaptureTableCreation(context, generated.SnapshotModel, serverVersion);
        var designerOperations = CaptureTableCreation(context, generated.DesignerModel, serverVersion);

        // Assert
        Assert.Contains(".SplitToTable(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".SplitToTable(", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertEntitySplitGenerationOwnership(sourceOperations);
        AssertEntitySplitGenerationOwnership(snapshotOperations);
        AssertEntitySplitGenerationOwnership(designerOperations);
    }

    private static void AssertEntitySplitGenerationOwnership(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var principalId = Assert.Single(tables["EntitySplitDesignRecords"].Columns, column => column.Name == "Id");

        var secondaryId = Assert.Single(tables["EntitySplitDesignDetails"].Columns, column => column.Name == "Id");

        Assert.Equal(
            MySqlValueGenerationStrategy.AutoIncrement,
            principalId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)?.Value);
        Assert.Null(secondaryId.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
    }
}
