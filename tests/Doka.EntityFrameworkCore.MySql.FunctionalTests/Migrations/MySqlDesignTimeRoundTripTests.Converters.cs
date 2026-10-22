namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>
    /// Provider-owned JSON and row-version converters retain their model CLR
    /// types instead of leaking their provider CLR types into generated models.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Provider_owned_converters_roundtrip_model_types_without_pending_operations(
        MySqlGuidFormat defaultGuidFormat
    )
    {
        // Arrange
        using var context = new ProviderConverterDesignContext(
            CreateOptions<ProviderConverterDesignContext>(
                MySqlServerVersion.MySql(new Version(8, 4, 0)),
                defaultGuidFormat));

        using var empty = new EmptyTemporalDesignContext(
            CreateOptions<EmptyTemporalDesignContext>(MySqlServerVersion.MySql(new Version(8, 4, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                empty.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var migrationCode = GenerateMigrationCode(operations);

        _ = Compile(migrationCode);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        Assert.Contains(
            ".Property<System.Text.Json.JsonElement>(\"Element\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.JsonDocument>(\"Document\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonNode>(\"Node\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonObject>(\"ObjectValue\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property<System.Text.Json.Nodes.JsonArray>(\"Array\")",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.Contains(".Property<byte[]>(\"Version\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<string>(\"Element\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Property<System.DateTime>(\"Version\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonElement.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonDocument.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("JsonNode.Parse(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Element = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Document = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Node = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectValue = \"{", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Array = \"[", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Version = new DateTime(", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Version = new DateTime(", generated.DesignerCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"{\\\"kind\\\":\\\"default",
            generated.SnapshotCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".HasDefaultValue(\"{\\\"kind\\\":\\\"default",
            generated.DesignerCode,
            StringComparison.Ordinal);
        AssertProviderJsonSeedModelValues(generated.SnapshotModel);
        AssertProviderJsonSeedModelValues(generated.DesignerModel);
        AssertProviderJsonDefaultModelValue(generated.SnapshotModel);
        AssertProviderJsonDefaultModelValue(generated.DesignerModel);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);

        var columns = Assert
            .Single(operations.OfType<CreateTableOperation>())
            .Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);

        Assert.Equal(typeof(JsonElement), columns["Element"].ClrType);
        Assert.Equal(typeof(JsonDocument), columns["Document"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonNode), columns["Node"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonObject), columns["ObjectValue"].ClrType);
        Assert.Equal(typeof(System.Text.Json.Nodes.JsonArray), columns["Array"].ClrType);
        Assert.Equal(typeof(byte[]), columns["Version"].ClrType);
        AssertProviderJsonDefaultOperationValues(columns);
        AssertProviderJsonSeedOperationValues(operations);

        Assert.Contains("table.Column<JsonElement>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonDocument>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonNode>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonObject>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<JsonArray>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<DateTime>", migrationCode, StringComparison.Ordinal);
    }
}
