namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Legacy multibyte charsets use the server's actual maximum encoded width.
    /// </summary>
    [Theory]
    [InlineData("eucjpms", 24)]
    [InlineData("ujis", 24)]
    [InlineData("euckr", 255)]
    public void Legacy_character_sets_use_their_native_maximum_byte_width(
        string charSet,
        int expectedLength
    )
    {
        // Arrange
        using var context = CreateContext("charset-" + charSet);

        // Act
        var property = context.Model.FindEntityType("Budget")!.FindProperty("B")!;

        // Assert
        Assert.Equal(expectedLength, property.GetMaxLength());
    }

    /// <summary>
    /// Allocation uses the resolved physical store type that EF emits, while
    /// retaining the application's original column-type annotation unchanged.
    /// </summary>
    [Theory]
    [InlineData("inline-charset", 255, "varchar(750) CHARACTER SET utf8mb4")]
    [InlineData("inline-collation", 255, "varchar(750) COLLATE utf8mb4_bin")]
    [InlineData("binary-modifier", 255, "varchar(750) BINARY")]
    public void Character_width_uses_the_resolved_type_without_rewriting_application_annotations(
        string scenario,
        int expectedLength,
        string configuredStoreType
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var ddl = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(expectedLength, entity.FindProperty("B")!.GetMaxLength());
        Assert.Equal(
            configuredStoreType,
            entity.FindProperty("A")!.FindAnnotation(RelationalAnnotationNames.ColumnType)!.Value);
        Assert.Equal("varchar(750)", entity.FindProperty("A")!.GetColumnType());
        Assert.Contains("`A` varchar(750)", ddl, StringComparison.Ordinal);
    }

    /// <summary>
    /// The physical byte estimator follows the resolved FLOAT precision syntax,
    /// including mappings that preserve a native precision-only store type.
    /// </summary>
    [Theory]
    [InlineData("float(24)", 4)]
    [InlineData("float(25)", 8)]
    [InlineData("float(53)", 8)]
    [InlineData("float(24,2)", 4)]
    public void Resolved_floating_point_mappings_use_the_native_storage_width(
        string storeType,
        long expectedBytes
    )
    {
        // Arrange
        using var context = CreateContext("float-metadata");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var property = entity.FindProperty("B")!;
        var mapping = new MySqlFloatTypeMapping(storeType);

        var bytes = MySqlIndexByteBudget.GetIndexedBytes(property, 0, null, bytesPerCharacter: null, mapping: mapping);

        // Assert
        Assert.Equal(expectedBytes, bytes);
    }

    /// <summary>
    /// DECIMAL packs integer and fractional digit groups separately, including
    /// the seven-digit remainder in the provider's default decimal(18,2).
    /// </summary>
    [Theory]
    [InlineData("decimal-boundary-pass", "decimal(18,2)", 9)]
    [InlineData("decimal-width-seven", "decimal(7,0)", 4)]
    public void Decimal_index_parts_use_the_native_packed_digit_width(
        string scenario,
        string storeType,
        long expectedBytes
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var property = context.Model.FindEntityType("Budget")!.FindProperty("D")!;
        var mapping = property.GetRelationalTypeMapping();

        var bytes = MySqlIndexByteBudget.GetIndexedBytes(property, 0, null, null);

        // Assert
        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(expectedBytes, bytes);
    }

    /// <summary>
    /// A key with 3056 string bytes and a nine-byte DECIMAL leaves room for
    /// exactly one four-byte implicit character, without a server-side prefix.
    /// </summary>
    [Fact]
    public void Decimal_budget_allocates_only_the_remaining_full_character()
    {
        // Arrange
        using var context = CreateContext("decimal-boundary-pass");

        // Act
        var entity = context.Model.FindEntityType("Budget")!;
        var ddl = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(1, entity.FindProperty("E")!.GetMaxLength());
        Assert.Contains("`E` varchar(1)", ddl, StringComparison.Ordinal);
        Assert.Null(Assert.Single(entity.GetIndexes()).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// At 3060 string bytes, the same DECIMAL plus even one implicit character
    /// exceeds the complete InnoDB key budget and must fail before DDL emission.
    /// </summary>
    [Fact]
    public void Decimal_budget_rejects_a_key_one_byte_past_the_limit()
    {
        // Arrange
        using var context = CreateContext("decimal-boundary-fail");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3072-byte", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The final validator rejects an over-budget index when every length is
    /// application-owned and the convention has no candidate to allocate.
    /// </summary>
    [Fact]
    public void Explicit_decimal_key_overflow_is_rejected_by_model_validation()
    {
        // Arrange
        using var context = CreateContext("decimal-explicit-overflow");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3073 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3072-byte", exception.Message, StringComparison.Ordinal);
    }
}
