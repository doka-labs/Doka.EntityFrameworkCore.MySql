namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    private static void AssertSinglePhysicalTemporalTableCreation(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());

        Assert.Equal("TemporalRecords", createTable.Name);
        Assert.True(
            createTable.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value as bool?);
    }

    private static void AssertSinglePhysicalBitemporalTableCreation(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Id", "business_from", "business_to", "recorded_from", "recorded_to"],
            columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertSinglePhysicalApplicationTimeTableCreation(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var createTable = Assert.Single(operations.OfType<CreateTableOperation>());
        var columnNames = createTable
            .Columns.Select(column => column.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Id", "business_from", "business_to"], columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName, columnNames);
        Assert.DoesNotContain(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName, columnNames);
    }

    private static void AssertExactPropertySet(
        IModel model,
        Type entityType,
        params string[] expectedProperties
    )
    {
        var properties = model
            .FindEntityType(entityType)!
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties, properties);
    }
}
