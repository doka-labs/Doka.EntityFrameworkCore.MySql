using Microsoft.EntityFrameworkCore.Design.Internal;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the reverse-engineering baseline and scaffolded-code contract.
/// </summary>
public sealed class MySqlReverseEngineeringBaselineTests
{
    /// <summary>
    /// Verifies that the supported reverse-engineering baseline emits modern nullable scaffolded code
    /// and explicit provider configuration.
    /// </summary>
    [Fact]
    public void Reverse_engineering_scaffolds_modern_context_code_for_the_supported_baseline()
    {
        var scaffoldedModel = ScaffoldModel(CreatePhase2DatabaseModel(), detectedServerVersionText: "8.4.6");

        Assert.Contains("#nullable enable", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("=> Set<", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("= null!;", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(".UseMySql(", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            "modelBuilder.HasCharSet(\"utf8mb4\")",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "MySqlServerVersion.MySql(new System.Version(8, 4, 6))",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains("HasCharSet(\"utf8mb4\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("UseStorageEngine(\"InnoDB\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains(
            "#nullable enable",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "HasComputedColumnSql(\"JSON_LENGTH(`Payload`)\", true)",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.Contains("HasColumnType(\"json\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that scaffolding an unsupported release line makes the
    /// compatibility opt-in visible in generated provider configuration.
    /// </summary>
    [Fact]
    public void Reverse_engineering_preserves_explicit_unsupported_version_opt_in()
    {
        var scaffoldedModel = ScaffoldModel(CreatePhase2DatabaseModel(), detectedServerVersionText: "8.0.44");

        Assert.Contains(
            "MySqlServerVersionCompatibilityMode.AllowUnsupported",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that core relational metadata survives database-model to generated-code
    /// conversion without turning views into tables or duplicating unique definitions.
    /// </summary>
    [Fact]
    public void Reverse_engineering_preserves_core_schema_metadata()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateCoreMetadataDatabaseModel(),
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;
        var recordCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class CoreRecord", StringComparison.Ordinal))
            .Code;

        var viewCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class CoreSummary", StringComparison.Ordinal))
            .Code;

        Assert.Contains("ToView(\"core_summary\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasNoKey()", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasDefaultValueSql(\"7\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"optional count\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComment(\"core table\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasCheckConstraint(\"CK_core_record_optional\"", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasComputedColumnSql(\"`OptionalCount` + 1\", true)", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasColumnOrder(2)", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasColumnOrder(3)", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasName(\"PK_core_record\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasAlternateKey(e => e.Code)", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasName(\"UQ_core_record_Code\")", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain("HasIndex(e => e.Code", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasConstraintName(\"FK_core_child_record\")", contextCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", recordCode, StringComparison.Ordinal);
        Assert.Contains("public int? OptionalCount", viewCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that provider-specific index and store metadata survives conversion to
    /// generated C# without emitting a partial representation of a functional index.
    /// </summary>
    [Fact]
    public void Reverse_engineering_preserves_index_store_type_and_sequence_metadata()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateIndexAndStoreTypeDatabaseModel(),
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;
        var entityCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class StoreTypeRecord", StringComparison.Ordinal))
            .Code;

        Assert.Contains(
            "using Doka.EntityFrameworkCore.MySql;",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasPrefixLength(32, 0)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsDescending(false, true)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsFullText()", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsUnique()", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IX_StoreType_Functional", contextCode, StringComparison.Ordinal);
        Assert.Contains("HasSequence(\"store_sequence\")", contextCode, StringComparison.Ordinal);
        Assert.Contains(".StartsAt(7L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IncrementsBy(3)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".HasMin(7L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".HasMax(700L)", contextCode, StringComparison.Ordinal);
        Assert.Contains(".IsCyclic()", contextCode, StringComparison.Ordinal);

        Assert.Contains("public byte TinyUnsigned", entityCode, StringComparison.Ordinal);
        Assert.Contains("public uint MediumUnsigned", entityCode, StringComparison.Ordinal);
        Assert.Contains("public ulong UnsignedValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public decimal Amount", entityCode, StringComparison.Ordinal);
        Assert.Contains("public DateTime Moment", entityCode, StringComparison.Ordinal);
        Assert.Contains("public TimeOnly Duration", entityCode, StringComparison.Ordinal);
        Assert.Contains("public byte[] FixedBinary", entityCode, StringComparison.Ordinal);
        Assert.Contains("public byte[] BlobValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public ulong BitValue", entityCode, StringComparison.Ordinal);
        Assert.Contains("public short YearValue", entityCode, StringComparison.Ordinal);

        foreach (var storeType in new[]
        {
            "enum('new','done')",
            "set('a','b')",
            "tinyint unsigned",
            "mediumint",
            "mediumint unsigned",
            "bigint unsigned",
            "decimal(20,6) unsigned",
            "datetime(3)",
            "time(4)",
            "binary(8)",
            "mediumblob",
            "mediumtext",
            "bit(8)",
            "year",
            "json",
        })
        {
            Assert.Contains($"HasColumnType(\"{storeType}\")", contextCode, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies that recognized temporal metadata round-trips through the public
    /// table-builder API instead of leaking provider-internal annotations.
    /// </summary>
    [Fact]
    public void Reverse_engineering_emits_temporal_table_builder_contract()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateTemporalDatabaseModel(),
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;

        Assert.Contains(
            "tableBuilder.IsTemporal(temporalTableBuilder =>",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "temporalTableBuilder.UseHistoryTable(\"audit_entries_history\", \"temporal_metadata\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasPeriodStart(\"ValidFrom\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasPeriodEnd(\"ValidTo\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"ValidFrom\")", contextCode, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"ValidTo\")", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            MySqlAnnotationNames.IsTemporal,
            contextCode,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that MariaDB period catalog metadata round-trips through public
    /// application-time and key APIs without leaking internal annotations.
    /// </summary>
    [Fact]
    public void Reverse_engineering_emits_application_time_table_builder_contract()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateApplicationTimeDatabaseModel(),
            detectedServerVersionText: "11.4.5-MariaDB");

        var contextCode = scaffoldedModel.ContextFile.Code;

        Assert.Contains(
            "tableBuilder.HasApplicationTimePeriod(applicationTimeTableBuilder =>",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "applicationTimeTableBuilder.HasPeriodName(\"BusinessValidity\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasPeriodStart(\"ValidFrom\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(
            ".HasPeriodEnd(\"ValidTo\")",
            contextCode,
            StringComparison.Ordinal);
        Assert.Contains(".UseWithoutOverlaps()", contextCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            MySqlAnnotationNames.IsApplicationTime,
            contextCode,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that textual GUID columns remain text properties unless the provider-specific
    /// reverse-engineering opt-in is enabled.
    /// </summary>
    [Fact]
    public void Reverse_engineering_without_text_guid_opt_in_keeps_char36_columns_as_string()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateTextGuidDatabaseModel("char(36)"),
            detectedServerVersionText: "8.4.6");

        Assert.Contains(
            "public string ExternalId { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Guid ExternalId",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the explicit reverse-engineering opt-in scaffolds textual GUID columns
    /// as <see cref="Guid"/> properties.
    /// </summary>
    [Fact]
    public void Reverse_engineering_with_text_guid_opt_in_scaffolds_char36_columns_as_guid()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateTextGuidDatabaseModel("char(36)"),
            detectedServerVersionText: "8.4.6",
            configure: options => options.ScaffoldTextGuidsAsGuids());

        Assert.Contains(
            "public Guid ExternalId { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.Contains(
            "HasMySqlGuidFormat(MySqlGuidFormat.Char36)",
            scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a plain signed tinyint column reverse-engineers to the numeric CLR type instead of text or bool.
    /// </summary>
    [Fact]
    public void Reverse_engineering_maps_plain_tinyint_columns_to_sbyte()
    {
        var scaffoldedModel = ScaffoldModel(
            CreateSingleColumnDatabaseModel("legacy_numeric_entry", "TinyValue", "tinyint"),
            detectedServerVersionText: "8.4.6");

        Assert.Contains(
            "public sbyte TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public bool TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public string TinyValue { get; set; }",
            scaffoldedModel.AdditionalFiles.Single()
                .Code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that reverse engineering removes only semantically irrelevant integer
    /// display widths while retaining unsigned, boolean, and ZEROFILL behavior.
    /// </summary>
    [Theory]
    [InlineData("int", "int(11)", "int")]
    [InlineData("integer", "int(11)", "int")]
    [InlineData("bigint", "bigint(20)", "bigint")]
    [InlineData("smallint", "smallint(6) unsigned", "smallint unsigned")]
    [InlineData("tinyint", "tinyint(4)", "tinyint")]
    [InlineData("tinyint", "tinyint(1)", "tinyint(1)")]
    [InlineData("int", "int(6) zerofill", "int(6) zerofill")]
    [InlineData("varchar", "varchar(11)", "varchar(11)")]
    public void Reverse_engineering_normalizes_integer_display_widths(
        string dataType,
        string storeType,
        string expected
    )
    {
        Assert.Equal(
            expected,
            ColumnLoader.NormalizeIntegerDisplayWidth(dataType, storeType));
    }

    /// <summary>
    /// Verifies that the optional four-digit YEAR width is canonicalized without
    /// changing the deprecated two-digit YEAR semantics.
    /// </summary>
    [Theory]
    [InlineData("year", "year", "year")]
    [InlineData("year", "year(4)", "year")]
    [InlineData("YEAR", "YEAR(4)", "year")]
    [InlineData("year", "year(2)", "year(2)")]
    public void Reverse_engineering_normalizes_year_display_width(
        string dataType,
        string storeType,
        string expected
    )
    {
        Assert.Equal(
            expected,
            ColumnLoader.NormalizeYearDisplayWidth(dataType, storeType));
    }

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

    private sealed class StubDatabaseModelFactory : IDatabaseModelFactory
    {
        private readonly DatabaseModel _databaseModel;
        private readonly string _detectedServerVersionText;
        private readonly MySqlScaffoldingContext _scaffoldingContext;

        public StubDatabaseModelFactory(
            DatabaseModel databaseModel,
            string detectedServerVersionText,
            MySqlScaffoldingContext scaffoldingContext
        )
        {
            _databaseModel = databaseModel ?? throw new ArgumentNullException(nameof(databaseModel));
            _detectedServerVersionText = detectedServerVersionText
                ?? throw new ArgumentNullException(nameof(detectedServerVersionText));
            _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        }

        public DatabaseModel Create(
            string connectionString,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }

        public DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }
    }
}
