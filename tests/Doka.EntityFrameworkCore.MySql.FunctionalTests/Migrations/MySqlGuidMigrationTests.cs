using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies migration contracts for provider-native Guid storage transitions.
/// </summary>
public sealed class MySqlGuidMigrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";
    private const string BinaryOverrideDefault = "5100da31-7b88-45e7-9606-08edfe8f7dab";

    /// <summary>
    /// A store-type transition on both sides of an existing foreign key owns a
    /// symmetric drop/alter/recreate lifecycle in both migration directions.
    /// </summary>
    [Fact]
    public void Char36_transition_orders_foreign_key_around_both_column_alters()
    {
        using var converted = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());
        using var native = new NativeChar36GuidContext(CreateOptions<NativeChar36GuidContext>());

        AssertTransition(
            converted,
            native,
            "varchar(36)",
            "char(36)",
            typeof(string),
            typeof(Guid),
            "AlterColumn<Guid>");

        AssertTransition(
            native,
            converted,
            "char(36)",
            "varchar(36)",
            typeof(Guid),
            typeof(string),
            "AlterColumn<string>");
    }

    /// <summary>
    /// Application-owned textual Guid storage and provider-native Binary16
    /// storage cannot be changed in place in either direction.
    /// </summary>
    [Fact]
    public void Application_char36_and_native_binary16_transitions_require_explicit_data_migration()
    {
        using var converted = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());
        using var binary = new NativeBinary16GuidContext(CreateOptions<NativeBinary16GuidContext>());

        AssertApplicationNativeBinaryTransitionRejected(converted, binary);
        AssertApplicationNativeBinaryTransitionRejected(binary, converted);
    }

    /// <summary>
    /// Matching binary store types do not prove matching byte order. A custom
    /// Guid byte converter and provider-native Binary16 therefore require an
    /// explicit data migration in both directions.
    /// </summary>
    [Fact]
    public void Application_binary_and_native_binary16_transitions_require_explicit_data_migration()
    {
        using var converted = new ConvertedBinaryGuidContext(CreateOptions<ConvertedBinaryGuidContext>());
        using var binary = new NativeBinary16GuidContext(CreateOptions<NativeBinary16GuidContext>());

        AssertApplicationNativeBinaryTransitionRejected(converted, binary);
        AssertApplicationNativeBinaryTransitionRejected(binary, converted);
    }

    /// <summary>
    /// Adding a provider-native Char36 relationship keeps the migration operation
    /// and generated migration source typed as Guid.
    /// </summary>
    [Fact]
    public void Native_char36_added_relationship_uses_guid_migration_type()
    {
        using var source = new RelationshipEvolutionSourceContext(
            CreateOptions<RelationshipEvolutionSourceContext>(MySqlGuidFormat.Char36));

        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Char36));

        var operations = GetDifferences(source, target);
        var addColumn = Assert.Single(
            operations.OfType<AddColumnOperation>(),
            operation => operation.Name == nameof(EvolutionShipment.CustomerId));

        AssertGuidColumn(addColumn, "char(36)", isNullable: false);
        Assert.Null(addColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.Contains(
            $".Annotation(\"{MySqlAnnotationNames.RequiresExplicitBackfill}\", true)",
            migrationCode,
            StringComparison.Ordinal);
        AssertCompiles(migrationCode);
        AssertImplicitDataMigrationRejected(
            target,
            operations,
            "explicit DefaultValue or DefaultValueSql");
    }

    /// <summary>
    /// Provider-native Guid foreign keys keep Guid operation types, nullability,
    /// and defaults in both nullability directions and storage formats.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)")]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)")]
    public void Native_guid_nullability_transitions_use_guid_migration_types_in_both_directions(
        MySqlGuidFormat guidFormat,
        string storeType
    )
    {
        using var nullable = new RequiredBackfillSourceContext(
            CreateOptions<RequiredBackfillSourceContext>(guidFormat));

        using var required = new RequiredBackfillTargetContext(
            CreateOptions<RequiredBackfillTargetContext>(guidFormat));

        AssertNullabilityTransition(
            nullable,
            required,
            guidFormat,
            storeType,
            isNullable: false,
            oldIsNullable: true,
            expectedDefault: null);

        AssertNullabilityTransition(
            required,
            nullable,
            guidFormat,
            storeType,
            isNullable: true,
            oldIsNullable: false,
            expectedDefault: null);
    }

    /// <summary>
    /// Native Char36 and Binary16 relationships keep Guid operation types in
    /// both storage directions for required and nullable foreign keys.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)")]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)")]
    public void Native_guid_storage_format_transitions_use_guid_migration_types(
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType
    )
    {
        using var requiredSource = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(sourceFormat));

        using var requiredTarget = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(targetFormat));

        AssertNativeGuidFormatTransition(
            requiredSource,
            requiredTarget,
            sourceFormat,
            sourceStoreType,
            targetFormat,
            targetStoreType,
            containsNullableColumn: false);

        using var nullableSource = new RequiredBackfillSourceContext(
            CreateOptions<RequiredBackfillSourceContext>(sourceFormat));

        using var nullableTarget = new RequiredBackfillSourceContext(
            CreateOptions<RequiredBackfillSourceContext>(targetFormat));

        AssertNativeGuidFormatTransition(
            nullableSource,
            nullableTarget,
            sourceFormat,
            sourceStoreType,
            targetFormat,
            targetStoreType,
            containsNullableColumn: true);
    }

    /// <summary>
    /// A simultaneous native Guid storage and nullability transition keeps both
    /// sides of the migration operation model-typed in every direction.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", true)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", false)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", false)]
    public void Native_guid_combined_storage_and_nullability_transitions_use_guid_migration_types(
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool sourceIsNullable
    )
    {
        using DbContext source = sourceIsNullable
            ? new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(sourceFormat))
            : new RequiredBackfillTargetContext(CreateOptions<RequiredBackfillTargetContext>(sourceFormat));

        using DbContext target = sourceIsNullable
            ? new RequiredBackfillTargetContext(CreateOptions<RequiredBackfillTargetContext>(targetFormat))
            : new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(targetFormat));

        AssertNativeGuidFormatAndNullabilityTransition(
            source,
            target,
            sourceFormat,
            sourceStoreType,
            targetFormat,
            targetStoreType,
            sourceIsNullable);
    }

    /// <summary>
    /// Initial provider-native Char36 tables retain Guid migration types for
    /// primary and foreign-key columns.
    /// </summary>
    [Fact]
    public void Native_char36_create_table_columns_use_guid_migration_types()
    {
        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Char36));

        var operations = GetDifferences(target);
        var customerTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "GuidEvolutionCustomers");

        var shipmentTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "GuidEvolutionShipments");

        AssertGuidColumn(
            Assert.Single(customerTable.Columns, column => column.Name == nameof(EvolutionCustomer.Id)),
            "char(36)",
            isNullable: false);

        AssertGuidColumn(
            Assert.Single(shipmentTable.Columns, column => column.Name == nameof(EvolutionShipment.CustomerId)),
            "char(36)",
            isNullable: false);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// Application-owned Guid converters retain their provider CLR type instead
    /// of being claimed by provider-native migration normalization.
    /// </summary>
    [Fact]
    public void Application_owned_guid_converter_retains_string_migration_type()
    {
        using var target = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());

        var operations = GetDifferences(target);
        var documentTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "TextGuidDocuments");

        var documentId = Assert.Single(
            documentTable.Columns,
            column => column.Name == nameof(TextGuidDocument.Id));

        Assert.Equal(typeof(string), documentId.ClrType);
        Assert.Equal("varchar(36)", documentId.ColumnType);
        Assert.Null(documentId.FindAnnotation(MySqlAnnotationNames.GuidFormat));

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("table.Column<string>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// Invalid provider values cannot be emitted as defaults for Guid-typed
    /// native Char36 migration operations.
    /// </summary>
    [Fact]
    public void Native_char36_invalid_provider_default_fails_closed()
    {
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(string),
            ColumnType = "char(36)",
            DefaultValue = "not-a-guid",
        };

        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));
        var exception = Assert.Throws<InvalidOperationException>(() => differ.GetDifferences(null, null));

        Assert.Contains("Records.OwnerId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A canonical provider-side Char36 default is restored to its Guid model
    /// value before generated migration source is rendered.
    /// </summary>
    [Fact]
    public void Native_char36_provider_default_is_restored_to_guid()
    {
        var expected = Guid.Parse("a78b2b53-dca4-4e94-8ae9-77118371db3a");
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(string),
            ColumnType = "char(36)",
            DefaultValue = expected.ToString("D", CultureInfo.InvariantCulture),
        };

        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));
        var normalized = Assert.Single(differ.GetDifferences(null, null));

        var addColumn = Assert.IsType<AddColumnOperation>(normalized);
        Assert.Equal(typeof(Guid), addColumn.ClrType);
        Assert.Equal(expected, addColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode([addColumn]);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// Native Char36 operations preserve an existing Guid default and keep an
    /// optional column nullable when no default is present.
    /// </summary>
    [Fact]
    public void Native_char36_guid_and_null_defaults_are_preserved()
    {
        var expected = Guid.Parse("44c76118-9ac5-4708-a4df-6effd1d735c6");
        var required = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "RequiredRecords",
            ClrType = typeof(string),
            ColumnType = "char(36)",
            DefaultValue = expected,
        };

        var optional = new AddColumnOperation
        {
            Name = "ReviewerId",
            Table = "OptionalRecords",
            ClrType = typeof(string),
            ColumnType = "char(36)",
            IsNullable = true,
        };

        required[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;
        optional[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(required, optional));
        var normalized = differ.GetDifferences(null, null);

        Assert.Collection(
            normalized,
            operation =>
            {
                var addColumn = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal(typeof(Guid), addColumn.ClrType);
                Assert.False(addColumn.IsNullable);
                Assert.Equal(expected, addColumn.DefaultValue);
            },
            operation =>
            {
                var addColumn = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal(typeof(Guid), addColumn.ClrType);
                Assert.True(addColumn.IsNullable);
                Assert.Null(addColumn.DefaultValue);
            });

        var migrationCode = GenerateMigrationCode(normalized);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<string>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// Provider-native Binary16 operations remain Guid-typed without entering
    /// the Char36-specific normalization boundary.
    /// </summary>
    [Fact]
    public void Binary16_create_table_operations_remain_guid_typed()
    {
        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Binary16));

        var operations = GetDifferences(target);
        var customerTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "GuidEvolutionCustomers");

        var customerId = Assert.Single(
            customerTable.Columns,
            column => column.Name == nameof(EvolutionCustomer.Id));

        Assert.Equal(typeof(Guid), customerId.ClrType);
        Assert.Equal("binary(16)", customerId.ColumnType);
        Assert.Equal(
            MySqlGuidFormat.Binary16,
            customerId.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// An explicit Binary16 property under a Char36 default remains model-typed
    /// across table creation, column addition, and column alteration.
    /// </summary>
    [Fact]
    public void Binary16_override_under_char36_default_uses_guid_for_all_column_operations()
    {
        using var empty = new BinaryOverrideEmptyContext(
            CreateOptions<BinaryOverrideEmptyContext>(MySqlGuidFormat.Char36));

        using var nullable = new BinaryOverrideNullableContext(
            CreateOptions<BinaryOverrideNullableContext>(MySqlGuidFormat.Char36));

        using var required = new BinaryOverrideRequiredContext(
            CreateOptions<BinaryOverrideRequiredContext>(MySqlGuidFormat.Char36));

        using var defaulted = new BinaryOverrideDefaultedContext(
            CreateOptions<BinaryOverrideDefaultedContext>(MySqlGuidFormat.Char36));

        var createOperations = GetDifferences(defaulted);
        var createColumn = Assert.Single(
            Assert.Single(
                    createOperations.OfType<CreateTableOperation>(),
                    operation => operation.Name == "BinaryOverrideRecords")
                .Columns,
            column => column.Name == "ReferenceId");

        AssertGuidColumn(createColumn, "binary(16)", isNullable: false, MySqlGuidFormat.Binary16);
        Assert.Equal(Guid.Parse(BinaryOverrideDefault), createColumn.DefaultValue);

        var addOperations = GetDifferences(empty, nullable);
        var addColumn = Assert.Single(addOperations.OfType<AddColumnOperation>());

        AssertGuidColumn(addColumn, "binary(16)", isNullable: true, MySqlGuidFormat.Binary16);
        Assert.Null(addColumn.DefaultValue);

        var defaultedAddOperations = GetDifferences(empty, defaulted);
        var defaultedAddColumn = Assert.Single(defaultedAddOperations.OfType<AddColumnOperation>());

        AssertGuidColumn(defaultedAddColumn, "binary(16)", isNullable: false, MySqlGuidFormat.Binary16);
        Assert.Equal(Guid.Parse(BinaryOverrideDefault), defaultedAddColumn.DefaultValue);

        var alterOperations = GetDifferences(nullable, required);
        var alterColumn = Assert.Single(alterOperations.OfType<AlterColumnOperation>());

        AssertGuidColumn(alterColumn, "binary(16)", isNullable: false, MySqlGuidFormat.Binary16);
        AssertGuidColumn(alterColumn.OldColumn, "binary(16)", isNullable: true, MySqlGuidFormat.Binary16);
        Assert.Null(alterColumn.DefaultValue);
        Assert.Null(alterColumn.OldColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode(
            createOperations
                .Concat(addOperations)
                .Concat(defaultedAddOperations)
                .Concat(alterOperations)
                .ToArray());

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);

        _ = nullable
            .GetService<IMigrationsSqlGenerator>()
            .Generate(addOperations, nullable.GetService<IDesignTimeModel>().Model);

        _ = defaulted
            .GetService<IMigrationsSqlGenerator>()
            .Generate(defaultedAddOperations, defaulted.GetService<IDesignTimeModel>().Model);

        AssertImplicitDataMigrationRejected(required, alterOperations, "explicit DefaultValue or DefaultValueSql");
    }

    /// <summary>
    /// A provider-side Binary16 default is restored to its Guid model value.
    /// </summary>
    [Fact]
    public void Native_binary16_provider_default_is_restored_to_guid()
    {
        var expected = Guid.Parse(BinaryOverrideDefault);
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = expected.ToByteArray(bigEndian: true),
        };

        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));
        var addColumn = Assert.IsType<AddColumnOperation>(Assert.Single(differ.GetDifferences(null, null)));

        Assert.Equal(typeof(Guid), addColumn.ClrType);
        Assert.Equal(expected, addColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode([addColumn]);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
    }

    /// <summary>
    /// Binary16 defaults with an invalid physical width fail before migration
    /// source can encode an ambiguous Guid value.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void Native_binary16_invalid_provider_default_width_fails_closed(
        int byteCount
    )
    {
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = new byte[byteCount],
        };

        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));
        var exception = Assert.Throws<InvalidOperationException>(() => differ.GetDifferences(null, null));

        Assert.Contains("Binary16", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.Byte[]", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Binary16 operation rejects a default represented by the wrong provider
    /// CLR type even when its textual value is a valid Guid.
    /// </summary>
    [Fact]
    public void Native_binary16_invalid_provider_default_type_fails_closed()
    {
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = BinaryOverrideDefault,
        };

        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16;

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));
        var exception = Assert.Throws<InvalidOperationException>(() => differ.GetDifferences(null, null));

        Assert.Contains("Binary16", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A store-type change outside a foreign-key shape must not rebuild an unrelated
    /// relationship merely because both operations affect the same table.
    /// </summary>
    [Fact]
    public void Unrelated_store_type_change_does_not_rebuild_foreign_key()
    {
        using var source = new ShortDescriptionGuidContext(CreateOptions<ShortDescriptionGuidContext>());
        using var target = new LongDescriptionGuidContext(CreateOptions<LongDescriptionGuidContext>());

        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());
        Assert.Equal(nameof(TextGuidDocumentRevision.Description), alterColumn.Name);
        Assert.Empty(operations.OfType<DropForeignKeyOperation>());
        Assert.Empty(operations.OfType<AddForeignKeyOperation>());
    }

    private static void AssertTransition(
        DbContext source,
        DbContext target,
        string oldStoreType,
        string newStoreType,
        Type oldClrType,
        Type newClrType,
        string generatedType
    )
    {
        var operations = GetDifferences(source, target);

        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(ForeignKeyName, dropForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", dropForeignKey.Table);
        Assert.Collection(
            alterColumns.OrderBy(operation => operation.Table, StringComparer.Ordinal),
            operation => AssertColumnTransition(
                operation,
                "TextGuidDocumentRevisions",
                "DocumentId",
                oldStoreType,
                newStoreType,
                oldClrType,
                newClrType),
            operation => AssertColumnTransition(
                operation,
                "TextGuidDocuments",
                "Id",
                oldStoreType,
                newStoreType,
                oldClrType,
                newClrType));
        Assert.Equal(ForeignKeyName, addForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", addForeignKey.Table);
        Assert.Equal(["DocumentId"], addForeignKey.Columns);
        Assert.Equal("TextGuidDocuments", addForeignKey.PrincipalTable);
        Assert.NotNull(addForeignKey.PrincipalColumns);
        Assert.Equal(["Id"], addForeignKey.PrincipalColumns);
        Assert.Equal(ReferentialAction.Cascade, addForeignKey.OnDelete);
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[1]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[1]));
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains(generatedType, migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);

        _ = target
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.GetService<IDesignTimeModel>().Model);
    }

    private static void AssertApplicationNativeBinaryTransitionRejected(
        DbContext source,
        DbContext target
    )
    {
        var operations = GetDifferences(source, target);
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            var sourceFormat = operation.OldColumn.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value;
            var targetFormat = operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value;

            Assert.True(sourceFormat is null ^ targetFormat is null);
            Assert.True(
                Equals(sourceFormat, MySqlGuidFormat.Binary16)
                || Equals(targetFormat, MySqlGuidFormat.Binary16));
        });

        AssertImplicitDataMigrationRejected(target, operations, "explicit data migration");
    }

    private static void AssertColumnTransition(
        AlterColumnOperation operation,
        string table,
        string column,
        string oldStoreType,
        string newStoreType,
        Type oldClrType,
        Type newClrType
    )
    {
        Assert.Equal(table, operation.Table);
        Assert.Equal(column, operation.Name);
        Assert.Equal(newStoreType, operation.ColumnType);
        Assert.Equal(oldStoreType, operation.OldColumn.ColumnType);
        Assert.Equal(newClrType, operation.ClrType);
        Assert.Equal(oldClrType, operation.OldColumn.ClrType);
    }

    private static void AssertNullabilityTransition(
        DbContext source,
        DbContext target,
        MySqlGuidFormat guidFormat,
        string storeType,
        bool isNullable,
        bool oldIsNullable,
        object? expectedDefault
    )
    {
        var operations = GetDifferences(source, target);
        var alterColumn = Assert.Single(
            operations.OfType<AlterColumnOperation>(),
            operation => operation.Name == "AssigneeId");

        AssertGuidColumn(alterColumn, storeType, isNullable, guidFormat);
        AssertGuidColumn(alterColumn.OldColumn, storeType, oldIsNullable, guidFormat);
        Assert.Equal(expectedDefault, alterColumn.DefaultValue);
        Assert.Null(alterColumn.OldColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);

        if (oldIsNullable && !isNullable)
        {
            AssertImplicitDataMigrationRejected(
                target,
                operations,
                "explicit DefaultValue or DefaultValueSql");
        }
    }

    private static void AssertNativeGuidFormatTransition(
        DbContext source,
        DbContext target,
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool containsNullableColumn
    )
    {
        var operations = GetDifferences(source, target);
        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            AssertGuidColumn(operation, targetStoreType, operation.IsNullable, targetFormat);
            AssertGuidColumn(
                operation.OldColumn,
                sourceStoreType,
                operation.OldColumn.IsNullable,
                sourceFormat);
            Assert.Equal(operation.IsNullable, operation.OldColumn.IsNullable);
            Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(operation));
            Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(operation));
        });

        if (containsNullableColumn)
        {
            Assert.Contains(alterColumns, operation => operation.IsNullable);
            Assert.Contains(alterColumns, operation => !operation.IsNullable);
        }
        else
        {
            Assert.All(alterColumns, operation => Assert.False(operation.IsNullable));
        }

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
        AssertImplicitDataMigrationRejected(target, operations, "explicit data migration");
    }

    private static void AssertNativeGuidFormatAndNullabilityTransition(
        DbContext source,
        DbContext target,
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool sourceIsNullable
    )
    {
        var operations = GetDifferences(source, target);
        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            AssertGuidColumn(operation, targetStoreType, operation.IsNullable, targetFormat);
            AssertGuidColumn(
                operation.OldColumn,
                sourceStoreType,
                operation.OldColumn.IsNullable,
                sourceFormat);
            Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(operation));
            Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(operation));
        });

        var foreignKeyColumn = Assert.Single(
            alterColumns,
            operation => operation.Name == "AssigneeId");

        Assert.Equal(!sourceIsNullable, foreignKeyColumn.IsNullable);
        Assert.Equal(sourceIsNullable, foreignKeyColumn.OldColumn.IsNullable);
        Assert.Null(foreignKeyColumn.DefaultValue);
        Assert.Null(foreignKeyColumn.OldColumn.DefaultValue);

        var migrationCode = GenerateMigrationCode(operations);

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompiles(migrationCode);
        AssertImplicitDataMigrationRejected(target, operations, "explicit data migration");
    }

    private static void AssertImplicitDataMigrationRejected(
        DbContext target,
        IReadOnlyList<MigrationOperation> operations,
        string expectedContract
    )
    {
        var generator = target.GetService<IMigrationsSqlGenerator>();
        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(
            operations,
            target.GetService<IDesignTimeModel>().Model));

        Assert.Contains(expectedContract, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GuidEvolution", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("GuidWork", exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext => CreateOptions<TContext>(MySqlGuidFormat.Binary16);

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlGuidFormat defaultGuidFormat
    )
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    private static List<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    private static List<MigrationOperation> GetDifferences(
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            null,
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel())
        .ToList();

    private static void AssertGuidColumn(
        ColumnOperation operation,
        string storeType,
        bool isNullable,
        MySqlGuidFormat guidFormat = MySqlGuidFormat.Char36
    )
    {
        Assert.Equal(typeof(Guid), operation.ClrType);
        Assert.Equal(storeType, operation.ColumnType);
        Assert.Equal(isNullable, operation.IsNullable);
        Assert.Equal(
            guidFormat,
            operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);
    }

    private static string GenerateMigrationCode(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddEntityFrameworkDokaMySqlDesignTime();

        using var serviceProvider = services.BuildServiceProvider();
        var generator = serviceProvider
            .GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select("C#");

        return generator.GenerateMigration(
            "Doka.GeneratedGuidMigrations",
            "GuidMigration",
            operations,
            []);
    }

    private static void AssertCompiles(
        string source
    )
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trustedPlatformAssemblies
            .Concat(
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"Doka.GeneratedGuidMigrations.{Guid.NewGuid():N}",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result
            .Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(result.Success, string.Join(Environment.NewLine, errors));
    }

    private abstract class GuidMigrationContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract MySqlGuidFormat? NativeFormat { get; }

        protected virtual bool UseApplicationBinaryConverter => false;

        protected virtual int DescriptionLength => 64;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TextGuidDocument>(entity =>
            {
                entity.ToTable("TextGuidDocuments");
                entity.HasKey(document => document.Id);
                ConfigureGuid(entity.Property(document => document.Id));
            });

            modelBuilder.Entity<TextGuidDocumentRevision>(entity =>
            {
                entity.ToTable("TextGuidDocumentRevisions");
                entity.HasKey(revision => revision.Id);
                ConfigureGuid(entity.Property(revision => revision.DocumentId));
                entity
                    .Property(revision => revision.Description)
                    .HasMaxLength(DescriptionLength);
                entity.HasIndex(revision => revision.DocumentId);
                entity
                    .HasOne(revision => revision.Document)
                    .WithMany(document => document.Revisions)
                    .HasForeignKey(revision => revision.DocumentId)
                    .HasConstraintName(ForeignKeyName)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private void ConfigureGuid(
            PropertyBuilder<Guid> property
        )
        {
            if (NativeFormat is { } format)
            {
                property.HasMySqlGuidFormat(format);
                return;
            }

            if (UseApplicationBinaryConverter)
            {
                property
                    .HasConversion(
                        guid => guid.ToByteArray(),
                        bytes => new Guid(bytes))
                    .HasColumnType("binary(16)")
                    .HasMaxLength(16)
                    .IsFixedLength();

                return;
            }

            property
                .HasConversion<string>()
                .HasColumnType("varchar(36)")
                .HasMaxLength(36)
                .IsUnicode(false);
        }
    }

    private sealed class ConvertedGuidContext(DbContextOptions<ConvertedGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => null;
    }

    private sealed class ConvertedBinaryGuidContext(DbContextOptions<ConvertedBinaryGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => null;

        protected override bool UseApplicationBinaryConverter => true;
    }

    private sealed class NativeChar36GuidContext(DbContextOptions<NativeChar36GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;
    }

    private sealed class NativeBinary16GuidContext(DbContextOptions<NativeBinary16GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Binary16;
    }

    private sealed class ShortDescriptionGuidContext(DbContextOptions<ShortDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;
    }

    private sealed class LongDescriptionGuidContext(DbContextOptions<LongDescriptionGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override MySqlGuidFormat? NativeFormat => MySqlGuidFormat.Char36;

        protected override int DescriptionLength => 128;
    }

    private abstract class RelationshipEvolutionContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IncludesRelationship { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<EvolutionCustomer>(entity =>
            {
                entity.ToTable("GuidEvolutionCustomers");
                entity.HasKey(item => item.Id);

                if (!IncludesRelationship)
                {
                    entity.Ignore(item => item.Shipments);
                }
            });

            modelBuilder.Entity<EvolutionShipment>(entity =>
            {
                entity.ToTable("GuidEvolutionShipments");
                entity.HasKey(item => item.Id);

                if (!IncludesRelationship)
                {
                    entity.Ignore(item => item.CustomerId);
                    entity.Ignore(item => item.Customer);
                    return;
                }

                entity
                    .HasOne(item => item.Customer)
                    .WithMany(item => item.Shipments)
                    .HasForeignKey(item => item.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class RelationshipEvolutionSourceContext(
        DbContextOptions<RelationshipEvolutionSourceContext> options
    ) : RelationshipEvolutionContext(options)
    {
        protected override bool IncludesRelationship => false;
    }

    private sealed class RelationshipEvolutionTargetContext(
        DbContextOptions<RelationshipEvolutionTargetContext> options
    ) : RelationshipEvolutionContext(options)
    {
        protected override bool IncludesRelationship => true;
    }

    private abstract class RequiredBackfillContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool AssigneeRequired { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("GuidAssignee", entity =>
            {
                entity.ToTable("GuidAssignees");
                entity.IndexerProperty<Guid>("Id");
                entity.HasKey("Id");
            });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("GuidWorkItem", entity =>
            {
                entity.ToTable("GuidWorkItems");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (AssigneeRequired)
                {
                    entity.IndexerProperty<Guid>("AssigneeId");
                }
                else
                {
                    entity.IndexerProperty<Guid?>("AssigneeId");
                }

                entity
                    .HasOne("GuidAssignee", null)
                    .WithMany()
                    .HasForeignKey("AssigneeId")
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    private sealed class RequiredBackfillSourceContext(
        DbContextOptions<RequiredBackfillSourceContext> options
    ) : RequiredBackfillContext(options)
    {
        protected override bool AssigneeRequired => false;
    }

    private sealed class RequiredBackfillTargetContext(
        DbContextOptions<RequiredBackfillTargetContext> options
    ) : RequiredBackfillContext(options)
    {
        protected override bool AssigneeRequired => true;
    }

    private abstract class BinaryOverrideContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IncludesReference { get; }

        protected virtual bool ReferenceRequired => false;

        protected virtual bool HasReferenceDefault => false;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("BinaryOverrideRecord", entity =>
            {
                entity.ToTable("BinaryOverrideRecords");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (!IncludesReference)
                {
                    return;
                }

                PropertyBuilder property = ReferenceRequired
                    ? entity.IndexerProperty<Guid>("ReferenceId")
                    : entity.IndexerProperty<Guid?>("ReferenceId");

                property.HasMySqlGuidFormat(MySqlGuidFormat.Binary16);

                if (HasReferenceDefault)
                {
                    property.HasDefaultValue(Guid.Parse(BinaryOverrideDefault));
                }
            });
        }
    }

    private sealed class BinaryOverrideEmptyContext(
        DbContextOptions<BinaryOverrideEmptyContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => false;
    }

    private sealed class BinaryOverrideNullableContext(
        DbContextOptions<BinaryOverrideNullableContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;
    }

    private sealed class BinaryOverrideRequiredContext(
        DbContextOptions<BinaryOverrideRequiredContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;

        protected override bool ReferenceRequired => true;
    }

    private sealed class BinaryOverrideDefaultedContext(
        DbContextOptions<BinaryOverrideDefaultedContext> options
    ) : BinaryOverrideContext(options)
    {
        protected override bool IncludesReference => true;

        protected override bool ReferenceRequired => true;

        protected override bool HasReferenceDefault => true;
    }

    private sealed class EvolutionCustomer
    {
        public Guid Id { get; set; }

        public ICollection<EvolutionShipment> Shipments { get; } = [];
    }

    private sealed class EvolutionShipment
    {
        public int Id { get; set; }

        public Guid CustomerId { get; set; }

        public EvolutionCustomer Customer { get; set; } = null!;
    }

    private sealed class FixedMigrationsModelDiffer(
        params MigrationOperation[] operations
    ) : IMigrationsModelDiffer
    {
        public bool HasDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => operations.Length > 0;

        public IReadOnlyList<MigrationOperation> GetDifferences(
            IRelationalModel? source,
            IRelationalModel? target
        ) => operations;
    }

    private sealed class TextGuidDocument
    {
        public Guid Id { get; set; }

        public ICollection<TextGuidDocumentRevision> Revisions { get; } = [];
    }

    private sealed class TextGuidDocumentRevision
    {
        public int Id { get; set; }

        public Guid DocumentId { get; set; }

        public string Description { get; set; } = string.Empty;

        public TextGuidDocument Document { get; set; } = null!;
    }
}
