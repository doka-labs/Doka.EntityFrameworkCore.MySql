using Microsoft.EntityFrameworkCore.Design.Internal;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure cases and shared support.</summary>
public sealed partial class MySqlReverseEngineeringBaselineTests
{
    private static ScaffoldedModel ScaffoldModel(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        Action<MySqlReverseEngineeringOptionsBuilder>? configure = null
    )
    {
        using var serviceProvider =
            CreateDesignTimeServiceProvider(databaseModel, detectedServerVersionText, configure);

        using var scope = serviceProvider.CreateScope();
        var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

        return scaffolder.ScaffoldModel(
            "Server=localhost;Database=phase2;User ID=root;Password=secret;",
            new DatabaseModelFactoryOptions(Array.Empty<string>(), Array.Empty<string>()),
            new ModelReverseEngineerOptions(),
            new ModelCodeGenerationOptions
            {
                ContextName = "ReverseDbContext",
                ContextNamespace = "Phase2.Scaffolding",
                ModelNamespace = "Phase2.Scaffolding.Models",
                RootNamespace = "Phase2.Scaffolding",
                Language = "C#",
                ContextDir = "Generated",
                ProjectDir = "Generated",
                ConnectionString = "Server=localhost;Database=phase2;User ID=root;Password=secret;",
                SuppressConnectionStringWarning = true,
                UseNullableReferenceTypes = true,
            });
    }

    private static ServiceProvider CreateDesignTimeServiceProvider(
        DatabaseModel databaseModel,
        string detectedServerVersionText,
        Action<MySqlReverseEngineeringOptionsBuilder>? configure
    )
    {
        var services = new ServiceCollection();
#pragma warning disable EF1001
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
#pragma warning restore EF1001

        services.AddEntityFrameworkDesignTimeServices(reporter, () => new ServiceCollection().BuildServiceProvider());
        services.AddEntityFrameworkDokaMySqlDesignTime(configure);
        services.AddSingleton<IDatabaseModelFactory>(serviceProvider => new StubDatabaseModelFactory(
            databaseModel,
            detectedServerVersionText,
            serviceProvider.GetRequiredService<MySqlScaffoldingContext>()));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DatabaseModel CreatePhase2DatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "phase2",
            Collation = "utf8mb4_0900_ai_ci",
        };

        databaseModel.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "phase_two_record",
        };

        table.SetAnnotation(RelationalAnnotationNames.Collation, "utf8mb4_bin");
        table.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");
        table.SetAnnotation(MySqlAnnotationNames.StorageEngine, "InnoDB");
        databaseModel.Tables.Add(table);

        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };

        var payloadColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Payload",
            StoreType = "json",
            IsNullable = false,
        };

        var storedCountColumn = new DatabaseColumn
        {
            Table = table,
            Name = "StoredCount",
            StoreType = "int",
            IsNullable = false,
            ComputedColumnSql = "JSON_LENGTH(`Payload`)",
            IsStored = true,
        };

        table.Columns.Add(idColumn);
        table.Columns.Add(payloadColumn);
        table.Columns.Add(storedCountColumn);

        var primaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_phase_two_record",
        };

        primaryKey.Columns.Add(idColumn);
        table.PrimaryKey = primaryKey;

        return databaseModel;
    }

    private static DatabaseModel CreateTextGuidDatabaseModel(
        string storeType
    ) => CreateSingleColumnDatabaseModel("legacy_guid_entry", "ExternalId", storeType);

    private static DatabaseModel CreateCoreMetadataDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "core_metadata",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "core_record",
            Comment = "core table",
        };

        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };

        var codeColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Code",
            StoreType = "varchar(32)",
            IsNullable = false,
        };

        var optionalCountColumn = new DatabaseColumn
        {
            Table = table,
            Name = "OptionalCount",
            StoreType = "int",
            IsNullable = true,
            DefaultValueSql = "7",
            Comment = "optional count",
        };

        var computedCountColumn = new DatabaseColumn
        {
            Table = table,
            Name = "ComputedCount",
            StoreType = "int",
            IsNullable = true,
            ComputedColumnSql = "`OptionalCount` + 1",
            IsStored = true,
        };

        table.Columns.Add(idColumn);
        table.Columns.Add(codeColumn);
        table.Columns.Add(optionalCountColumn);
        table.Columns.Add(computedCountColumn);
        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_core_record",
            Columns = { idColumn },
        };

        var uniqueConstraint = new DatabaseUniqueConstraint
        {
            Table = table,
            Name = "UQ_core_record_Code",
            Columns = { codeColumn },
        };

        var duplicateUniqueIndex = new DatabaseIndex
        {
            Table = table,
            Name = uniqueConstraint.Name,
            IsUnique = true,
            Columns = { codeColumn },
        };

        table.UniqueConstraints.Add(uniqueConstraint);
        table.Indexes.Add(duplicateUniqueIndex);
        table.SetAnnotation(
            MySqlAnnotationNames.ScaffoldingCheckConstraints,
            new MySqlScaffoldedCheckConstraint[]
            {
                new("CK_core_record_optional", "`OptionalCount` >= 0"),
            });

        var childTable = new DatabaseTable
        {
            Database = databaseModel,
            Name = "core_child",
        };

        var childIdColumn = new DatabaseColumn
        {
            Table = childTable,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
        };

        var parentIdColumn = new DatabaseColumn
        {
            Table = childTable,
            Name = "RecordId",
            StoreType = "int",
            IsNullable = false,
        };

        childTable.Columns.Add(childIdColumn);
        childTable.Columns.Add(parentIdColumn);
        childTable.PrimaryKey = new DatabasePrimaryKey
        {
            Table = childTable,
            Name = "PK_core_child",
            Columns = { childIdColumn },
        };

        var foreignKey = new DatabaseForeignKey
        {
            Table = childTable,
            PrincipalTable = table,
            Name = "FK_core_child_record",
            OnDelete = ReferentialAction.Cascade,
            Columns = { parentIdColumn },
            PrincipalColumns = { idColumn },
        };

        childTable.ForeignKeys.Add(foreignKey);

        var view = new DatabaseView
        {
            Database = databaseModel,
            Name = "core_summary",
        };

        var viewOptionalCountColumn = new DatabaseColumn
        {
            Table = view,
            Name = "OptionalCount",
            StoreType = "int",
            IsNullable = true,
        };

        view.Columns.Add(viewOptionalCountColumn);
        databaseModel.Tables.Add(table);
        databaseModel.Tables.Add(childTable);
        databaseModel.Tables.Add(view);

        return databaseModel;
    }

    private static DatabaseModel CreateIndexAndStoreTypeDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "index_store_metadata",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "store_type_record",
        };

        databaseModel.Tables.Add(table);

        var idColumn = AddColumn(table, "Id", "int");
        var nameColumn = AddColumn(table, "Name", "varchar(191)");
        var codeColumn = AddColumn(table, "Code", "varchar(64)");
        var bodyColumn = AddColumn(table, "Body", "text");

        AddColumn(table, "EnumValue", "enum('new','done')");
        AddColumn(table, "SetValue", "set('a','b')");
        AddColumn(table, "TinyUnsigned", "tinyint unsigned");
        AddColumn(table, "MediumSigned", "mediumint");
        AddColumn(table, "MediumUnsigned", "mediumint unsigned");
        AddColumn(table, "UnsignedValue", "bigint unsigned");
        AddColumn(table, "Amount", "decimal(20,6) unsigned");
        AddColumn(table, "Moment", "datetime(3)");
        AddColumn(table, "Duration", "time(4)");
        AddColumn(table, "FixedBinary", "binary(8)");
        AddColumn(table, "BlobValue", "mediumblob");
        AddColumn(table, "TextValue", "mediumtext");
        AddColumn(table, "BitValue", "bit(8)");
        AddColumn(table, "YearValue", "year");
        AddColumn(table, "JsonValue", "json");

        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_StoreTypeRecord",
            Columns = { idColumn },
        };

        var prefixLengths = new[] { 32, 0 };
        var prefixIndex = new DatabaseIndex
        {
            Table = table,
            Name = "IX_StoreType_Name_Code",
            Columns = { nameColumn, codeColumn },
            IsDescending = { false, true },
        };

        prefixIndex.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, prefixLengths);

        var fullTextIndex = new DatabaseIndex
        {
            Table = table,
            Name = "IX_StoreType_Body",
            Columns = { bodyColumn },
        };

        fullTextIndex.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);

        var uniqueIndex = new DatabaseIndex
        {
            Table = table,
            Name = "IX_StoreType_Code",
            Columns = { codeColumn },
            IsUnique = true,
        };

        var functionalIndex = new DatabaseIndex
        {
            Table = table,
            Name = "IX_StoreType_Functional",
        };

        functionalIndex.SetAnnotation(
            MySqlAnnotationNames.ScaffoldingIndexParts,
            new MySqlScaffoldedIndexPart[]
            {
                new(null, "lower(`Name`)", false, null),
            });

        table.Indexes.Add(prefixIndex);
        table.Indexes.Add(fullTextIndex);
        table.Indexes.Add(uniqueIndex);
        table.Indexes.Add(functionalIndex);
        databaseModel.Sequences.Add(
            new DatabaseSequence
            {
                Database = databaseModel,
                Name = "store_sequence",
                StoreType = "bigint",
                StartValue = 7,
                IncrementBy = 3,
                MinValue = 7,
                MaxValue = 700,
                IsCyclic = true,
            });

        return databaseModel;
    }

    private static DatabaseModel CreateTemporalDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "temporal_metadata",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "audit_entries",
        };

        var idColumn = AddColumn(table, "Id", "int");

        AddColumn(table, "Payload", "varchar(255)");
        AddColumn(table, "ValidFrom", "datetime(6)");
        AddColumn(table, "ValidTo", "datetime(6)");

        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_audit_entries",
            Columns = { idColumn },
        };

        table.SetAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal, true);
        table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourceHistoryTable,
            "audit_entries_history");
        table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourceHistorySchema,
            databaseModel.DatabaseName);
        table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourcePeriodStartColumn,
            "ValidFrom");
        table.SetAnnotation(
            MySqlAnnotationNames.TemporalSourcePeriodEndColumn,
            "ValidTo");
        databaseModel.Tables.Add(table);

        return databaseModel;
    }

    private static DatabaseModel CreateApplicationTimeDatabaseModel()
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "temporal_metadata",
            Collation = "utf8mb4_uca1400_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = "business_records",
        };

        var idColumn = AddColumn(table, "Id", "int");

        AddColumn(table, "Payload", "varchar(255)");
        AddColumn(table, "ValidFrom", "datetime(6)");
        AddColumn(table, "ValidTo", "datetime(6)");

        table.PrimaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PRIMARY",
            Columns = { idColumn },
        };
        table.PrimaryKey.SetAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, true);
        table.SetAnnotation(MySqlAnnotationNames.IsApplicationTime, true);
        table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodName, "BusinessValidity");
        table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodStartColumn, "ValidFrom");
        table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodEndColumn, "ValidTo");
        databaseModel.Tables.Add(table);

        return databaseModel;
    }

    private static DatabaseColumn AddColumn(
        DatabaseTable table,
        string name,
        string storeType
    )
    {
        var column = new DatabaseColumn
        {
            Table = table,
            Name = name,
            StoreType = storeType,
            IsNullable = false,
        };

        table.Columns.Add(column);

        return column;
    }

    private static DatabaseModel CreateSingleColumnDatabaseModel(
        string tableName,
        string columnName,
        string storeType
    )
    {
        var databaseModel = new DatabaseModel
        {
            DatabaseName = "phase2",
            Collation = "utf8mb4_0900_ai_ci",
        };

        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = tableName,
        };

        databaseModel.Tables.Add(table);

        var idColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "int",
            IsNullable = false,
            ValueGenerated = ValueGenerated.OnAdd,
        };

        var externalIdColumn = new DatabaseColumn
        {
            Table = table,
            Name = columnName,
            StoreType = storeType,
            IsNullable = false,
        };

        table.Columns.Add(idColumn);
        table.Columns.Add(externalIdColumn);

        var primaryKey = new DatabasePrimaryKey
        {
            Table = table,
            Name = "PK_legacy_guid_entry",
        };

        primaryKey.Columns.Add(idColumn);
        table.PrimaryKey = primaryKey;

        return databaseModel;
    }
}
