using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    private static void AssertSpatialSeedModelValue(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(SpatialSridDesignRecord));
        var seed = Assert.Single(entityType!.GetSeedData());
        var location = Assert.IsType<Point>(seed[nameof(SpatialSridDesignRecord.Location)]);

        Assert.Equal(13.4050, location.X);
        Assert.Equal(52.5200, location.Y);
        Assert.Equal(4326, location.SRID);
    }

    private static void AssertSpatialSeedDataOperations(
        GeneratedInitialMigration migration
    )
    {
        var operations = migration.Operations;
        var table = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "SpatialSridDesignRecord");

        var locationColumn = Assert.Single(
            table.Columns,
            column => column.Name == nameof(SpatialSridDesignRecord.Location));

        var insert = Assert.Single(operations.OfType<InsertDataOperation>());

        Assert.Equal(typeof(Point), locationColumn.ClrType);
        var location = Assert.IsType<Point>(
            Assert.Single(GetColumnValues(insert, nameof(SpatialSridDesignRecord.Location))));

        Assert.Equal(13.4050, location.X);
        Assert.Equal(52.5200, location.Y);
        Assert.Equal(4326, location.SRID);
    }

    private static void AssertProviderJsonSeedModelValues(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(ProviderConverterDesignRecord));
        var seed = Assert.Single(entityType!.GetSeedData());

        Assert.Equal(
            """{"kind":"element","value":1}""",
            Assert.IsType<JsonElement>(seed[nameof(ProviderConverterDesignRecord.Element)]).GetRawText());
        Assert.Equal(
            """{"kind":"document","value":2}""",
            Assert.IsType<JsonDocument>(seed[nameof(ProviderConverterDesignRecord.Document)]).RootElement.GetRawText());
        Assert.Equal(
            """{"kind":"node","value":3}""",
            Assert.IsAssignableFrom<JsonNode>(seed[nameof(ProviderConverterDesignRecord.Node)]).ToJsonString());
        Assert.Equal(
            """{"kind":"object","value":4}""",
            Assert.IsType<JsonObject>(seed[nameof(ProviderConverterDesignRecord.ObjectValue)]).ToJsonString());
        Assert.Equal(
            """["array",5,true]""",
            Assert.IsType<JsonArray>(seed[nameof(ProviderConverterDesignRecord.Array)]).ToJsonString());
        Assert.Equal(
            MySqlBytesToDateTimeConverter.ToBytes(s_providerRowVersion),
            Assert.IsType<byte[]>(seed[nameof(ProviderConverterDesignRecord.Version)]));
    }

    private static void AssertProviderJsonSeedOperationValues(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var insert = Assert.Single(operations.OfType<InsertDataOperation>());

        Assert.Equal(
            """{"kind":"element","value":1}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Element)).Single()));
        Assert.Equal(
            """{"kind":"document","value":2}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Document)).Single()));
        Assert.Equal(
            """{"kind":"node","value":3}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Node)).Single()));
        Assert.Equal(
            """{"kind":"object","value":4}""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.ObjectValue)).Single()));
        Assert.Equal(
            """["array",5,true]""",
            Assert.IsType<string>(GetColumnValues(insert, nameof(ProviderConverterDesignRecord.Array)).Single()));
        Assert.DoesNotContain(nameof(ProviderConverterDesignRecord.Version), insert.Columns);
    }

    private static void AssertProviderJsonDefaultModelValue(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(ProviderConverterDesignRecord));

        Assert.Equal(
            """{"kind":"default-element"}""",
            Assert.IsType<JsonElement>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Element))?.GetDefaultValue())
                .GetRawText());
        Assert.Equal(
            """{"kind":"default-document"}""",
            Assert.IsType<JsonDocument>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Document))?.GetDefaultValue())
                .RootElement.GetRawText());
        Assert.Equal(
            """{"kind":"default-node"}""",
            Assert.IsAssignableFrom<JsonNode>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Node))?.GetDefaultValue())
                .ToJsonString());
        Assert.Equal(
            """{"kind":"default-object"}""",
            Assert.IsType<JsonObject>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.ObjectValue))?.GetDefaultValue())
                .ToJsonString());
        Assert.Equal(
            """["default-array"]""",
            Assert.IsType<JsonArray>(
                    entityType?.FindProperty(nameof(ProviderConverterDesignRecord.Array))?.GetDefaultValue())
                .ToJsonString());
    }

    private static void AssertProviderJsonDefaultOperationValues(
        Dictionary<string, AddColumnOperation> columns
    )
    {
        Assert.Equal(
            """{"kind":"default-element"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Element)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-document"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Document)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-node"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Node)].DefaultValue));
        Assert.Equal(
            """{"kind":"default-object"}""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.ObjectValue)].DefaultValue));
        Assert.Equal(
            """["default-array"]""",
            Assert.IsType<string>(columns[nameof(ProviderConverterDesignRecord.Array)].DefaultValue));
    }
}
