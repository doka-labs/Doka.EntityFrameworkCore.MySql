namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>Provides independent storage contracts for default-bearing Guid keys.</summary>
    public static IEnumerable<object[]> GuidSqlDefaultDesignCases()
    {
        yield return ["SqlDefaultChar36DesignRecord", MySqlGuidFormat.Char36, "char(36)", "UUID()"];
        yield return ["SqlDefaultNativeUuidDesignRecord", MySqlGuidFormat.Uuid, "uuid", "UUID()"];
        yield return
        [
            "SqlDefaultBinary16DesignRecord", MySqlGuidFormat.Binary16, "binary(16)",
            "UNHEX(REPLACE(UUID(), '-', ''))",
        ];
    }

    /// <summary>SQL defaults retain Guid types and EF OnAdd metadata without acquiring client generation.</summary>
    [Theory]
    [MemberData(nameof(GuidSqlDefaultDesignCases))]
    public void Guid_sql_defaults_roundtrip_without_implicit_client_generation(
        string entityName,
        MySqlGuidFormat format,
        string storeType,
        string defaultSql
    )
    {
        // Arrange
        using var context = new GuidSqlDefaultDesignContext(
            CreateOptions<GuidSqlDefaultDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(context);
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);
        var sourceMigration = CaptureInitialMigration(context, sourceModel);
        var snapshotMigration = CaptureInitialMigration(context, generated.SnapshotModel);
        var designerMigration = CaptureInitialMigration(context, generated.DesignerModel);

        // Assert
        AssertSqlDefaultGuidProperty(sourceModel, entityName, format, storeType, defaultSql, explicitClient: false);
        AssertSqlDefaultGuidProperty(
            generated.SnapshotModel, entityName, format, storeType, defaultSql, explicitClient: false);
        AssertSqlDefaultGuidProperty(
            generated.DesignerModel, entityName, format, storeType, defaultSql, explicitClient: false);
        Assert.DoesNotContain("ClientGuid", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientGuid", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains(".Property<System.Guid>(\"Id\")", generated.DesignerCode, StringComparison.Ordinal);
        Assert.Contains($".HasDefaultValueSql(\"{defaultSql}\")", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains($".HasDefaultValueSql(\"{defaultSql}\")", generated.DesignerCode, StringComparison.Ordinal);
        AssertSqlDefaultInitialMigration(sourceMigration, entityName, format, storeType, defaultSql);
        AssertSqlDefaultInitialMigration(snapshotMigration, entityName, format, storeType, defaultSql);
        AssertSqlDefaultInitialMigration(designerMigration, entityName, format, storeType, defaultSql);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>Explicit legacy annotations survive replay without causing a schema change to the implicit model.</summary>
    [Theory]
    [MemberData(nameof(GuidSqlDefaultDesignCases))]
    public void Legacy_client_guid_sql_default_snapshot_has_no_physical_diff_to_new_implicit_model(
        string entityName,
        MySqlGuidFormat format,
        string storeType,
        string defaultSql
    )
    {
        // Arrange
        using var legacy = new LegacyGuidSqlDefaultDesignContext(
            CreateOptions<LegacyGuidSqlDefaultDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        using var target = new GuidSqlDefaultDesignContext(
            CreateOptions<GuidSqlDefaultDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        // Act
        var generated = GenerateAndCompile(legacy);
        var snapshotRoundTrip = CaptureRoundTrip(target, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(target, generated.DesignerModel);
        var targetModel = target.GetService<IDesignTimeModel>().Model;

        // Assert
        Assert.Contains("ClientGuid", generated.SnapshotCode, StringComparison.Ordinal);
        Assert.Contains("ClientGuid", generated.DesignerCode, StringComparison.Ordinal);
        AssertSqlDefaultGuidProperty(
            generated.SnapshotModel, entityName, format, storeType, defaultSql, explicitClient: true);
        AssertSqlDefaultGuidProperty(
            generated.DesignerModel, entityName, format, storeType, defaultSql, explicitClient: true);
        AssertSqlDefaultGuidProperty(targetModel, entityName, format, storeType, defaultSql, explicitClient: false);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>Checks both relational storage and the raw strategy annotation on a reconstructed property.</summary>
    private static void AssertSqlDefaultGuidProperty(
        IModel model,
        string entityName,
        MySqlGuidFormat format,
        string storeType,
        string defaultSql,
        bool explicitClient
    )
    {
        var property = model.FindEntityType(entityName)?.FindProperty("Id");

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property.ClrType);
        Assert.Equal(format, property.GetMySqlGuidFormat());
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Equal(defaultSql, property.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);

        if (explicitClient)
        {
            Assert.Equal(
                MySqlValueGenerationStrategy.ClientGuid,
                property.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)?.Value);
        }
        else
        {
            Assert.Null(property.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy));
        }
    }

    /// <summary>Checks the selected table in a successfully compiled initial migration.</summary>
    private static void AssertSqlDefaultInitialMigration(
        GeneratedInitialMigration migration,
        string entityName,
        MySqlGuidFormat format,
        string storeType,
        string defaultSql
    )
    {
        var table = Assert.Single(
            migration.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == entityName);

        var column = Assert.Single(table.Columns);

        Assert.Equal(typeof(Guid), column.ClrType);
        Assert.Equal(storeType, column.ColumnType);
        Assert.Equal(defaultSql, column.DefaultValueSql);
        Assert.Null(column.DefaultValue);
        Assert.Equal(format, column.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);
        Assert.Contains("table.Column<Guid>", migration.Code, StringComparison.Ordinal);
        Assert.Contains($"defaultValueSql: \"{defaultSql}\"", migration.Code, StringComparison.Ordinal);
    }
}
