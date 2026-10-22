namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the reverse-engineering baseline and scaffolded-code contract.
/// </summary>
public sealed partial class MySqlReverseEngineeringBaselineTests
{
    /// <summary>
    /// Verifies that the supported reverse-engineering baseline emits modern nullable scaffolded code
    /// and explicit provider configuration.
    /// </summary>
    [Fact]
    public void Reverse_engineering_scaffolds_modern_context_code_for_the_supported_baseline()
    {
        // Arrange
        var databaseModel = CreatePhase2DatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(databaseModel, detectedServerVersionText: "8.4.6");

        // Assert
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
        // Arrange
        var databaseModel = CreatePhase2DatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(databaseModel, detectedServerVersionText: "8.0.44");

        // Assert
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
        // Arrange
        var databaseModel = CreateCoreMetadataDatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;
        var recordCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class CoreRecord", StringComparison.Ordinal))
            .Code;

        var viewCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class CoreSummary", StringComparison.Ordinal))
            .Code;

        // Assert
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
        // Arrange
        var databaseModel = CreateIndexAndStoreTypeDatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;

        var entityCode = scaffoldedModel
            .AdditionalFiles.Single(file => file.Code.Contains("class StoreTypeRecord", StringComparison.Ordinal))
            .Code;

        // Assert
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
        // Arrange
        var databaseModel = CreateTemporalDatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6");

        var contextCode = scaffoldedModel.ContextFile.Code;

        // Assert
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
        // Arrange
        var databaseModel = CreateApplicationTimeDatabaseModel();

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "11.4.5-MariaDB");

        var contextCode = scaffoldedModel.ContextFile.Code;

        // Assert
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
        // Arrange
        var databaseModel = CreateTextGuidDatabaseModel("char(36)");

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6");

        // Assert
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
        // Arrange
        var databaseModel = CreateTextGuidDatabaseModel("char(36)");

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6",
            configure: options => options.ScaffoldTextGuidsAsGuids());

        // Assert
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
    /// MariaDB's native UUID is unambiguously a Guid, unlike an arbitrary char(36) column.
    /// </summary>
    [Fact]
    public void Reverse_engineering_native_uuid_scaffolds_guid_without_text_guid_opt_in()
    {
        // Arrange
        var databaseModel = CreateSingleColumnDatabaseModel("native_uuid_entry", "ExternalId", "uuid");

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "11.8.3-MariaDB");

        var entityCode = scaffoldedModel.AdditionalFiles.Single().Code;

        // Assert
        Assert.Contains("public Guid ExternalId { get; set; }", entityCode, StringComparison.Ordinal);
        Assert.Contains("HasColumnType(\"uuid\")", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.Contains("HasMySqlGuidFormat(MySqlGuidFormat.Uuid)", scaffoldedModel.ContextFile.Code,
            StringComparison.Ordinal);
        Assert.DoesNotContain("HasMaxLength(36)", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFixedLength()", scaffoldedModel.ContextFile.Code, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stubbed UUID column must not make MySQL emit a model it cannot create.
    /// </summary>
    [Fact]
    public void Reverse_engineering_native_uuid_rejects_non_mariadb_server()
    {
        // Arrange
        var databaseModel = CreateSingleColumnDatabaseModel("native_uuid_entry", "ExternalId", "uuid");

        // Act
        var exception = Assert.Throws<NotSupportedException>(() => ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6"));

        // Assert
        Assert.Contains("MariaDB 10.7", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a plain signed tinyint column reverse-engineers to the numeric CLR type instead of text or bool.
    /// </summary>
    [Fact]
    public void Reverse_engineering_maps_plain_tinyint_columns_to_sbyte()
    {
        // Arrange
        var databaseModel = CreateSingleColumnDatabaseModel("legacy_numeric_entry", "TinyValue", "tinyint");

        // Act
        var scaffoldedModel = ScaffoldModel(
            databaseModel,
            detectedServerVersionText: "8.4.6");

        // Assert
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
        // Arrange
        var expectedValue = expected;

        // Act
        var actual = ColumnLoader.NormalizeIntegerDisplayWidth(dataType, storeType);

        // Assert
        Assert.Equal(expectedValue, actual);
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
        // Arrange
        var expectedValue = expected;

        // Act
        var actual = ColumnLoader.NormalizeYearDisplayWidth(dataType, storeType);

        // Assert
        Assert.Equal(expectedValue, actual);
    }
}
