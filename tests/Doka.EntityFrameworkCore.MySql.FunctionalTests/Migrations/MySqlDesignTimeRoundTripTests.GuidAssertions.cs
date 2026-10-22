namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    private static void AssertMixedGuidOperationMetadata(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var createTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "MixedGuidDesignRecords");

        var id = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.Id));

        var binaryReference = Assert.Single(
            createTable.Columns,
            column => column.Name == nameof(MixedGuidDesignRecord.BinaryReference));

        var index = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "IX_MixedGuidDesignRecords_Name_BinaryReference");

        Assert.Equal(MySqlGuidFormat.Char36, id.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(MySqlGuidFormat.Binary16, binaryReference.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal([16, 0], index.GetMySqlMigrationMetadata().IndexPrefixLengths);
    }

    private static void AssertChar36OperationMetadata(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var tables = operations
            .OfType<CreateTableOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        var id = Assert.Single(
            tables["Char36DesignDocuments"].Columns,
            column => column.Name == nameof(Char36DesignDocument.Id));

        var documentId = Assert.Single(
            tables["Char36DesignRevisions"].Columns,
            column => column.Name == nameof(Char36DesignRevision.DocumentId));

        Assert.Equal(MySqlGuidFormat.Char36, id.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            id.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(MySqlGuidFormat.Char36, documentId.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(
            MySqlValueGenerationStrategy.None,
            documentId.GetMySqlMigrationMetadata().ValueGenerationStrategy);
    }

    private static void AssertGuidSeedDataOperations(
        GeneratedInitialMigration migration
    )
    {
        var operations = migration.Operations;
        var migrationCode = migration.Code;

        var inserts = operations
            .OfType<InsertDataOperation>()
            .ToLookup(operation => operation.Table, StringComparer.Ordinal);

        var principals = inserts["GuidSeedDesignRecords"];

        Assert.Equal(2, principals.Sum(operation => operation.Values.GetLength(0)));
        Assert.All(GetColumnValues(principals, nameof(GuidSeedDesignRecord.Id)), value => Assert.IsType<string>(value));
        Assert.All(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.ExplicitChar36)),
            value => Assert.IsType<string>(value));
        Assert.All(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.Binary16)),
            value => Assert.IsType<Guid>(value));
        AssertNullableProviderGuidValues(
            GetColumnValues(principals, nameof(GuidSeedDesignRecord.OptionalChar36)));

        var principalTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "GuidSeedDesignRecords");

        Assert.Equal(
            new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.DefaultChar36))
                .DefaultValue);
        Assert.Equal(
            new Guid("5c855603-622d-4bee-b232-f74f63026caf"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.DefaultBinary16))
                .DefaultValue);
        Assert.Equal(
            new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"),
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.OptionalDefaultChar36))
                .DefaultValue);
        Assert.Equal(
            Guid.Empty,
            Assert.Single(
                    principalTable.Columns,
                    column => column.Name == nameof(GuidSeedDesignRecord.EmptyDefaultChar36))
                .DefaultValue);

        var dependents = inserts["GuidSeedDependentRecords"];
        Assert.Equal(2, dependents.Sum(operation => operation.Values.GetLength(0)));
        Assert.All(
            GetColumnValues(dependents, nameof(GuidSeedDependentRecord.PrincipalId)),
            value => Assert.IsType<string>(value));
        AssertNullableProviderGuidValues(
            GetColumnValues(dependents, nameof(GuidSeedDependentRecord.OptionalPrincipalId)));

        Assert.Contains("migrationBuilder.InsertData(", migrationCode, StringComparison.Ordinal);
        Assert.Contains("table: \"GuidSeedDependentRecords\"", migrationCode, StringComparison.Ordinal);
        Assert.Contains(
            "defaultValue: new Guid(\"27caab1e-a588-4dcc-bace-fef7cf47e1fd\")",
            migrationCode,
            StringComparison.Ordinal);
    }

    private static void AssertProviderGuidDefaultModelValues(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(GuidSeedDesignRecord));

        Assert.Equal(
            new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.DefaultChar36))?.GetDefaultValue());
        Assert.Equal(
            new Guid("5c855603-622d-4bee-b232-f74f63026caf"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.DefaultBinary16))?.GetDefaultValue());
        Assert.Equal(
            new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"),
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.OptionalDefaultChar36))?.GetDefaultValue());
        Assert.Equal(
            Guid.Empty,
            entityType?.FindProperty(nameof(GuidSeedDesignRecord.EmptyDefaultChar36))?.GetDefaultValue());
    }

    private static object?[] GetColumnValues(
        InsertDataOperation operation,
        string column
    )
    {
        var columnIndex = Array.IndexOf(operation.Columns, column);

        Assert.True(columnIndex >= 0, $"Seed operation for '{operation.Table}' has no '{column}' column.");

        return Enumerable
            .Range(0, operation.Values.GetLength(0))
            .Select(row => operation.Values[row, columnIndex])
            .ToArray();
    }

    private static object?[] GetColumnValues(
        IEnumerable<InsertDataOperation> operations,
        string column
    ) => operations
        .Where(operation => Array.IndexOf(operation.Columns, column) >= 0)
        .SelectMany(operation => GetColumnValues(operation, column))
        .ToArray();

    private static void AssertNullableProviderGuidValues(
        object?[] values
    )
    {
        Assert.Single(values, value => value is null);
        Assert.Single(values, value => value is string);
    }

    private static void AssertComplexGuidMetadata(
        IModel model
    )
    {
        var entityType = model.FindEntityType(typeof(Char36ComplexOnlyDesignRecord))
            ?? model.FindEntityType(typeof(Char36ComplexOnlyDesignRecord).FullName!);

        var complexType = entityType
            ?.FindComplexProperty(nameof(Char36ComplexOnlyDesignRecord.Details))
            ?.ComplexType;

        var externalId = complexType?.FindProperty(nameof(Char36ComplexOnlyDesignDetails.ExternalId));
        var applicationId = complexType?.FindProperty(nameof(Char36ComplexOnlyDesignDetails.ApplicationId));

        Assert.Equal(typeof(Guid), externalId?.ClrType);
        Assert.Equal(MySqlGuidFormat.Char36, externalId?.GetMySqlGuidFormat());
        Assert.Equal(
            new Guid("0517e651-62e5-4a93-845d-fd87b14e4363"),
            Assert.IsType<Guid>(externalId?.GetDefaultValue()));
        Assert.Equal(
            typeof(string),
            applicationId?.ClrType == typeof(string)
                ? applicationId.ClrType
                : applicationId?.GetProviderClrType() ?? applicationId?.GetValueConverter()?.ProviderClrType);
        Assert.Null(applicationId?.GetMySqlGuidFormat());
    }
}
