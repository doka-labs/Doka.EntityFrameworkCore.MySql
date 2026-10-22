namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the DDL/type-mapping and schema-validation baseline.
/// </summary>
public sealed partial class MySqlTypeMappingBaselineTests
{
    /// <summary>
    /// Verifies that the documented text, binary, enum, decimal, and temporal defaults are reflected in the model.
    /// </summary>
    [Theory]
    [InlineData(nameof(TypeMappingEntity.BoundedText), "varchar(32)")]
    [InlineData(nameof(TypeMappingEntity.FixedText), "char(8)")]
    [InlineData(nameof(TypeMappingEntity.UnboundedText), "longtext")]
    [InlineData(nameof(TypeMappingEntity.BoundedBinary), "varbinary(64)")]
    [InlineData(nameof(TypeMappingEntity.UnboundedBinary), "longblob")]
    [InlineData(nameof(TypeMappingEntity.Status), "smallint")]
    [InlineData(nameof(TypeMappingEntity.StringBackedStatus), "varchar(24)")]
    [InlineData(nameof(TypeMappingEntity.Amount), "decimal(18,2)")]
    [InlineData(nameof(TypeMappingEntity.ExplicitAmount), "decimal(18,2)")]
    [InlineData(nameof(TypeMappingEntity.CreatedAt), "datetime(6)")]
    [InlineData(nameof(TypeMappingEntity.OpenAt), "time(6)")]
    [InlineData(nameof(TypeMappingEntity.ObservedAt), "timestamp(3)")]
    public void Model_store_types_follow_the_package5_contract(
        string propertyName,
        string expected
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        // Act
        var storeType = context.Model.FindEntityType(typeof(TypeMappingEntity))!.FindProperty(propertyName)!
            .GetColumnType();

        // Assert
        Assert.Equal(expected, storeType);
    }

    /// <summary>
    /// Verifies that ANSI/unicode hints do not move the provider out of the utf8mb4 text-store family.
    /// </summary>
    [Fact]
    public void Is_unicode_false_does_not_change_the_text_store_family()
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        // Act
        var property =
            context.Model.FindEntityType(typeof(TypeMappingEntity))!.FindProperty(
                nameof(TypeMappingEntity.AnsiHintText))!;

        // Assert
        Assert.Equal("varchar(20)", property.GetColumnType());
        Assert.False(property.IsUnicode() ?? true);
    }

    /// <summary>
    /// Verifies that explicit tinyint store types preserve the signed-byte versus boolean distinction.
    /// </summary>
    [Theory]
    [InlineData("tinyint", typeof(sbyte))]
    [InlineData("tinyint(1)", typeof(bool))]
    public void Tinyint_store_types_distinguish_sbyte_from_bool(
        string storeType,
        Type expectedType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var typeMappingSource = context.GetService<IRelationalTypeMappingSource>();

        // Act
        var mapping = typeMappingSource.FindMapping(storeType);

        // Assert
        Assert.NotNull(mapping);
        Assert.Equal(expectedType, mapping.ClrType);
        Assert.Equal(storeType, mapping.StoreType);
    }

    /// <summary>
    /// Verifies that CLR floating-point constants retain approximate-value semantics
    /// when MySQL parses their SQL literals.
    /// </summary>
    [Theory]
    [InlineData(typeof(double), 1.0, "1.0E0")]
    [InlineData(typeof(double), 1.5, "1.5E0")]
    [InlineData(typeof(float), 1.0f, "1.0E0")]
    [InlineData(typeof(float), 1.5f, "1.5E0")]
    public void Floating_point_literals_use_approximate_value_notation(
        Type clrType,
        object value,
        string expectedLiteral
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(clrType)!;

        // Act
        var literal = typeMapping.GenerateSqlLiteral(value);

        // Assert
        Assert.Equal(expectedLiteral, literal);
    }

    /// <summary>
    /// Verifies that every explicit MySQL-family text type retains the
    /// provider's SQL-mode-independent literal generator.
    /// </summary>
    [Theory]
    [InlineData("char")]
    [InlineData("varchar")]
    [InlineData("tinytext")]
    [InlineData("text")]
    [InlineData("mediumtext")]
    [InlineData("longtext")]
    [InlineData("enum('one','two')")]
    [InlineData("set('one','two')")]
    public void Explicit_text_type_literals_are_sql_mode_independent(
        string storeType
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(storeType)!;

        // Act
        var literal = typeMapping.GenerateSqlLiteral("path\\segment");

        // Assert
        Assert.Equal("_utf8mb4 X'706174685C7365676D656E74'", literal);
    }

    /// <summary>
    /// Verifies that CLR char literals use the same SQL-mode-independent
    /// encoding policy as provider text mappings.
    /// </summary>
    [Theory]
    [InlineData('A', "'A'")]
    [InlineData('\'', "''''")]
    [InlineData('\\', "_utf8mb4 X'5C'")]
    [InlineData('\0', "_utf8mb4 X'00'")]
    public void Char_literals_are_sql_mode_independent(
        char value,
        string expectedLiteral
    )
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(char))!;

        // Act
        var literal = typeMapping.GenerateSqlLiteral(value);

        // Assert
        Assert.Equal("char(1)", typeMapping.StoreType);
        Assert.Equal(expectedLiteral, literal);
    }

    /// <summary>
    /// Verifies that the CLR char mapping exposes EF Core's compiled-model
    /// default and that the mapping source uses that canonical instance.
    /// </summary>
    [Fact]
    public void Char_mapping_exposes_the_compiled_model_default()
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());

        // Act
        var typeMapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(char));

        // Assert
        Assert.Same(MySqlCharTypeMapping.Default, typeMapping);
        Assert.Equal("char(1)", MySqlCharTypeMapping.Default.StoreType);
    }

    /// <summary>
    /// Verifies the connector runtime shapes used for translated string
    /// FirstOrDefault and LastOrDefault projections.
    /// </summary>
    [Theory]
    [InlineData("A", 'A')]
    [InlineData("AB", 'A')]
    [InlineData("", '\0')]
    [InlineData('Z', 'Z')]
    public void Char_reader_materializes_connector_text_values(
        object providerValue,
        char expected
    )
    {
        // Arrange
        var mapping = MySqlCharTypeMapping.Default;

        // Act
        var actual = ReadCharProviderValue(mapping, providerValue);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that an incompatible connector value fails loudly instead of
    /// being converted through culture-sensitive fallback behavior.
    /// </summary>
    [Fact]
    public void Char_reader_rejects_an_unsupported_provider_value()
    {
        // Arrange
        const int unsupported = 65;

        // Act
        var exception = Record.Exception(() => ReadCharProviderValue(MySqlCharTypeMapping.Default, unsupported));

        // Assert
        Assert.Contains("System.Int32", Assert.IsType<InvalidOperationException>(exception).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that explicit model/property collation metadata remains intact through model building.
    /// </summary>
    [Fact]
    public void Explicit_collation_metadata_is_preserved_on_the_model()
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var designTimeModel = context.GetService<IDesignTimeModel>()
            .Model;

        // Act
        var entityType = designTimeModel.FindEntityType(typeof(TypeMappingEntity))!;
        var property = entityType.FindProperty(nameof(TypeMappingEntity.CollatedText))!;

        // Assert
        Assert.Equal("utf8mb4_bin", designTimeModel.GetCollation());
        Assert.Equal("utf8mb4_0900_as_cs", property.GetCollation());
    }

    /// <summary>
    /// Verifies that generated migration SQL uses the documented enum and decimal store types.
    /// </summary>
    [Fact]
    public void Migrations_sql_generator_uses_numeric_enum_and_explicit_decimal_store_types()
    {
        // Arrange
        using var context = new TypeMappingContext(CreateOptions<TypeMappingContext>());
        var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var commands = migrationsSqlGenerator.Generate(
            new MigrationOperation[]
            {
                new CreateTableOperation
                {
                    Name = "Phase2TypeMappingProbe",
                    Columns =
                    {
                        new AddColumnOperation
                        {
                            Name = "Status",
                            ClrType = typeof(StatusCode),
                            IsNullable = false,
                        },
                        new AddColumnOperation
                        {
                            Name = "Amount",
                            ClrType = typeof(decimal),
                            Precision = 18,
                            Scale = 2,
                            IsNullable = false,
                        },
                    },
                },
            },
            context.Model);

        var command = Assert.Single(commands);

        // Assert
        Assert.Contains("`Status` smallint NOT NULL", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`Amount` decimal(18,2) NOT NULL", command.CommandText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that keyed strings without an explicit length receive the
    /// provider's index-safe bounded default.
    /// </summary>
    [Fact]
    public void Keyed_text_without_explicit_max_length_uses_bounded_default()
    {
        // Arrange
        using var context = new ImplicitKeyedTextContext(
            CreateOptions<ImplicitKeyedTextContext>());

        // Act
        var property = context.Model
            .FindEntityType(typeof(ImplicitKeyedTextEntity))!
            .FindProperty(nameof(ImplicitKeyedTextEntity.Code))!;

        // Assert
        Assert.Equal("varchar(255)", property.GetColumnType());
    }

    /// <summary>
    /// Verifies that indexed binary properties without an explicit length
    /// receive the same index-safe bounded default.
    /// </summary>
    [Fact]
    public void Indexed_binary_without_explicit_max_length_uses_bounded_default()
    {
        // Arrange
        using var context = new ImplicitIndexedBinaryContext(
            CreateOptions<ImplicitIndexedBinaryContext>());

        // Act
        var property = context.Model
            .FindEntityType(typeof(ImplicitIndexedBinaryEntity))!
            .FindProperty(nameof(ImplicitIndexedBinaryEntity.Token))!;

        // Assert
        Assert.Equal("varbinary(255)", property.GetColumnType());
    }
}
