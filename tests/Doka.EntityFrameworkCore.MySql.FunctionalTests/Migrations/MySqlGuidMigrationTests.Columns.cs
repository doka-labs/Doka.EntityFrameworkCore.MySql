namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the columns responsibility for MySqlGuidMigrationTests.</summary>
public sealed partial class MySqlGuidMigrationTests
{
    /// <summary>
    /// Adding a provider-native Char36 relationship keeps the migration operation
    /// and generated migration source typed as Guid.
    /// </summary>
    [Fact]
    public void Native_char36_added_relationship_uses_guid_migration_type()
    {
        // Arrange
        using var source = new RelationshipEvolutionSourceContext(
            CreateOptions<RelationshipEvolutionSourceContext>(MySqlGuidFormat.Char36));

        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Char36));

        // Act
        var operations = GetDifferences(source, target);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);
        var generationFailure = Record.Exception(() => target.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.GetService<IDesignTimeModel>().Model));

        // Assert
        var addColumn = Assert.Single(
            operations.OfType<AddColumnOperation>(),
            operation => operation.Name == nameof(EvolutionShipment.CustomerId));

        AssertGuidColumn(addColumn, "char(36)", isNullable: false);
        Assert.Null(addColumn.DefaultValue);
        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.Contains(
            $".Annotation(\"{MySqlAnnotationNames.RequiresExplicitBackfill}\", true)",
            migrationCode,
            StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
        AssertExplicitMigrationFailure(generationFailure, "explicit DefaultValue or DefaultValueSql");
    }

    /// <summary>
    /// Initial provider-native Char36 tables retain Guid migration types for
    /// primary and foreign-key columns.
    /// </summary>
    [Fact]
    public void Native_char36_create_table_columns_use_guid_migration_types()
    {
        // Arrange
        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Char36));

        // Act
        var operations = GetDifferences(target);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
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

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// Application-owned Guid converters retain their provider CLR type instead
    /// of being claimed by provider-native migration normalization.
    /// </summary>
    [Fact]
    public void Application_owned_guid_converter_retains_string_migration_type()
    {
        // Arrange
        using var target = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());

        // Act
        var operations = GetDifferences(target);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
        var documentTable = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "TextGuidDocuments");

        var documentId = Assert.Single(
            documentTable.Columns,
            column => column.Name == nameof(TextGuidDocument.Id));

        Assert.Equal(typeof(string), documentId.ClrType);
        Assert.Equal("varchar(36)", documentId.ColumnType);
        Assert.Null(documentId.FindAnnotation(MySqlAnnotationNames.GuidFormat));

        Assert.Contains("table.Column<string>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// Invalid provider values cannot be emitted as defaults for Guid-typed
    /// native textual migration operations.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)")]
    [InlineData(MySqlGuidFormat.Uuid, "uuid")]
    public void Native_text_guid_invalid_provider_default_fails_closed(
        MySqlGuidFormat format,
        string storeType
    )
    {
        // Arrange
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(string),
            ColumnType = storeType,
            DefaultValue = "not-a-guid",
            [MySqlAnnotationNames.GuidFormat] = format,
        };

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));

        // Act
        var failure = Record.Exception(() => differ.GetDifferences(null, null));

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(failure);

        Assert.Contains("Records.OwnerId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A canonical provider-side textual default is restored to its Guid model
    /// value before generated migration source is rendered.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)")]
    [InlineData(MySqlGuidFormat.Uuid, "uuid")]
    public void Native_text_guid_provider_default_is_restored_to_guid(
        MySqlGuidFormat format,
        string storeType
    )
    {
        // Arrange
        var expected = Guid.Parse("a78b2b53-dca4-4e94-8ae9-77118371db3a");
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(string),
            ColumnType = storeType,
            DefaultValue = expected.ToString("D", CultureInfo.InvariantCulture),
            [MySqlAnnotationNames.GuidFormat] = format,
        };

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));

        // Act
        var operations = differ.GetDifferences(null, null);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
        var addColumn = Assert.IsType<AddColumnOperation>(Assert.Single(operations));

        Assert.Equal(typeof(Guid), addColumn.ClrType);
        Assert.Equal(expected, addColumn.DefaultValue);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// Native Char36 operations preserve an existing Guid default and keep an
    /// optional column nullable when no default is present.
    /// </summary>
    [Fact]
    public void Native_char36_guid_and_null_defaults_are_preserved()
    {
        // Arrange
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

        // Act
        var normalized = differ.GetDifferences(null, null);
        var migrationCode = GenerateMigrationCode(normalized);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
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

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<string>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// Provider-native Binary16 operations remain Guid-typed without entering
    /// the Char36-specific normalization boundary.
    /// </summary>
    [Fact]
    public void Binary16_create_table_operations_remain_guid_typed()
    {
        // Arrange
        using var target = new RelationshipEvolutionTargetContext(
            CreateOptions<RelationshipEvolutionTargetContext>(MySqlGuidFormat.Binary16));

        // Act
        var operations = GetDifferences(target);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
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

        Assert.Contains("table.Column<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("table.Column<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// An explicit Binary16 property under a Char36 default remains model-typed
    /// across table creation, column addition, and column alteration.
    /// </summary>
    [Theory]
    [InlineData("create", "table.Column<Guid>", "table.Column<byte[]>")]
    [InlineData("add", "AddColumn<Guid>", "AddColumn<byte[]>")]
    [InlineData("add-default", "AddColumn<Guid>", "AddColumn<byte[]>")]
    [InlineData("alter", "AlterColumn<Guid>", "AlterColumn<byte[]>")]
    public void Binary16_override_under_char36_default_uses_guid_for_all_column_operations(
        string operationKind,
        string expectedCode,
        string rejectedCode
    )
    {
        // Arrange
        using var empty = new BinaryOverrideEmptyContext(
            CreateOptions<BinaryOverrideEmptyContext>(MySqlGuidFormat.Char36));

        using var nullable = new BinaryOverrideNullableContext(
            CreateOptions<BinaryOverrideNullableContext>(MySqlGuidFormat.Char36));

        using var required = new BinaryOverrideRequiredContext(
            CreateOptions<BinaryOverrideRequiredContext>(MySqlGuidFormat.Char36));

        using var defaulted = new BinaryOverrideDefaultedContext(
            CreateOptions<BinaryOverrideDefaultedContext>(MySqlGuidFormat.Char36));

        DbContext target = operationKind switch
        {
            "create" or "add-default" => defaulted,
            "add" => nullable,
            "alter" => required,
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind)),
        };

        // Act
        var operations = operationKind switch
        {
            "create" => GetDifferences(defaulted),
            "add" => GetDifferences(empty, nullable),
            "add-default" => GetDifferences(empty, defaulted),
            "alter" => GetDifferences(nullable, required),
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind)),
        };

        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);
        var generationFailure = Record.Exception(() => target
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.GetService<IDesignTimeModel>().Model));

        // Assert
        var column = operationKind == "create"
            ? Assert.Single(
                Assert.Single(operations.OfType<CreateTableOperation>()).Columns,
                candidate => candidate.Name == "ReferenceId")
            : Assert.Single(operations.OfType<ColumnOperation>());

        AssertGuidColumn(column, "binary(16)", operationKind == "add", MySqlGuidFormat.Binary16);
        Assert.Equal(
            operationKind is "create" or "add-default" ? Guid.Parse(BinaryOverrideDefault) : null,
            column.DefaultValue);
        Assert.Contains(expectedCode, migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedCode, migrationCode, StringComparison.Ordinal);

        if (operationKind == "alter")
        {
            var alterColumn = Assert.IsType<AlterColumnOperation>(column);

            AssertGuidColumn(alterColumn.OldColumn, "binary(16)", isNullable: true, MySqlGuidFormat.Binary16);
            Assert.Null(alterColumn.OldColumn.DefaultValue);
            Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
            Assert.Contains(
                "explicit DefaultValue or DefaultValueSql",
                Assert.IsType<InvalidOperationException>(generationFailure).Message,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Null(generationFailure);
        }

        AssertCompilationSucceeded(compilation);
    }

    /// <summary>
    /// A provider-side Binary16 default is restored to its Guid model value.
    /// </summary>
    [Fact]
    public void Native_binary16_provider_default_is_restored_to_guid()
    {
        // Arrange
        var expected = Guid.Parse(BinaryOverrideDefault);
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = expected.ToByteArray(bigEndian: true),
            [MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16,
        };

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));

        // Act
        var operations = differ.GetDifferences(null, null);
        var migrationCode = GenerateMigrationCode(operations);
        var compilation = CompileMigrationCode(migrationCode);

        // Assert
        var addColumn = Assert.IsType<AddColumnOperation>(Assert.Single(operations));

        Assert.Equal(typeof(Guid), addColumn.ClrType);
        Assert.Equal(expected, addColumn.DefaultValue);

        Assert.Contains("AddColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(compilation);
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
        // Arrange
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = new byte[byteCount],
            [MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16,
        };

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));

        // Act
        var failure = Record.Exception(() => differ.GetDifferences(null, null));

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(failure);

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
        // Arrange
        var operation = new AddColumnOperation
        {
            Name = "OwnerId",
            Table = "Records",
            ClrType = typeof(byte[]),
            ColumnType = "binary(16)",
            DefaultValue = BinaryOverrideDefault,
            [MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16,
        };

        var differ = new MySqlMigrationsModelDiffer(new FixedMigrationsModelDiffer(operation));

        // Act
        var failure = Record.Exception(() => differ.GetDifferences(null, null));

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(failure);

        Assert.Contains("Binary16", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
    }
}
