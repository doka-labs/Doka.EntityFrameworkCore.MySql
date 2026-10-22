namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Provider-native Char36 keys and foreign keys retain their Guid model CLR type
    /// through both generated design-time model surfaces.
    /// </summary>
    [Fact]
    public void Char36_snapshot_and_designer_roundtrip_guid_relationship_without_pending_operations()
    {
        // Arrange
        using var context = new Char36DesignContext(
            CreateOptions<Char36DesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceOperations = CaptureInitialOperations(context, context.GetService<IDesignTimeModel>().Model);
        var snapshotOperations = CaptureInitialOperations(context, generated.SnapshotModel);
        var designerOperations = CaptureInitialOperations(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(
            ".HasMySqlGuidFormat(MySqlGuidFormat.Char36)",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"DocumentId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"DocumentId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertChar36OperationMetadata(sourceOperations);
        AssertChar36OperationMetadata(snapshotOperations);
        AssertChar36OperationMetadata(designerOperations);
    }

    /// <summary>
    /// Native UUID metadata and model-managed values retain Guid model types in
    /// both generated model surfaces and the initial migration.
    /// </summary>
    [Fact]
    public void MariaDb_uuid_snapshot_and_designer_roundtrip_guid_seed_without_pending_operations()
    {
        // Arrange
        using var context = new MariaDbUuidDesignContext(
            CreateOptions<MariaDbUuidDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Uuid));

        // Act
        var generated = GenerateAndCompile(context);
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            null,
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        _ = Compile(GenerateMigrationCode(operations));
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Uuid)", generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains("Id = new Guid(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Uuid)", generated.DesignerCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var insert = Assert.Single(operations.OfType<InsertDataOperation>());
        Assert.All(createTable.Columns, column => Assert.Equal("uuid", column.ColumnType));
        Assert.Equal(typeof(Guid), Assert.Single(createTable.Columns, column => column.Name == "Id").ClrType);
        Assert.IsType<string>(Assert.Single(GetColumnValues(insert, "Id")));
    }

    /// <summary>
    /// A context-level Char36 default remains a Guid model contract when one
    /// property participates in multiple relationship chains.
    /// </summary>
    [Fact]
    public void Default_char36_branching_relationships_roundtrip_without_string_model_drift()
    {
        // Arrange
        using var context = new DefaultChar36RelationshipDesignContext(
            CreateOptions<DefaultChar36RelationshipDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".HasColumnType(\"varchar(36)\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid?>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"ReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"OptionalReferenceId\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>
    /// A context-level Char36 default and an explicit Binary16 override retain
    /// Guid model types and byte-order annotations through generated models.
    /// </summary>
    [Fact]
    public void Default_char36_and_binary16_override_roundtrip_through_generated_models()
    {
        // Arrange
        using var context = new MixedGuidDesignContext(
            CreateOptions<MixedGuidDesignContext>(
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                MySqlGuidFormat.Char36));

        // Act
        var generated = GenerateAndCompile(context);

        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                null,
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var migrationCode = GenerateMigrationCode(operations);

        _ = Compile(migrationCode);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceOperations = CaptureInitialOperations(context, context.GetService<IDesignTimeModel>().Model);
        var snapshotOperations = CaptureInitialOperations(context, generated.SnapshotModel);
        var designerOperations = CaptureInitialOperations(context, generated.DesignerModel);

        // Assert
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Char36)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".HasMySqlGuidFormat(MySqlGuidFormat.Binary16)", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<byte[]>(\"BinaryReference\")", generated.SnapshotCode, StringComparison.Ordinal);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
        AssertMixedGuidOperationMetadata(sourceOperations);
        AssertMixedGuidOperationMetadata(snapshotOperations);
        AssertMixedGuidOperationMetadata(designerOperations);

        var createTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "MixedGuidDesignRecords");

        var binaryReference = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.BinaryReference));

        Assert.Equal(typeof(Guid), binaryReference.ClrType);
        Assert.Equal("binary(16)", binaryReference.ColumnType);
        Assert.Equal(
            MySqlGuidFormat.Binary16,
            binaryReference.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
    }
}
