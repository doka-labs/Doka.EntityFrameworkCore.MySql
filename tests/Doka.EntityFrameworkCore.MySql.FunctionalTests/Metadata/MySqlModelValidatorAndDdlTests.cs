namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Tests for model validation (constraint name lengths, schema qualification),
/// sequence DDL generation (Drop, Alter, Rename), and ServerCapabilities boundaries.
/// </summary>
public sealed partial class MySqlModelValidatorAndDdlTests
{
    /// <summary>
    /// Verifies that native full-text indexes can target the unbounded text types
    /// supported by MySQL and MariaDB.
    /// </summary>
    [Fact]
    public void Full_text_index_accepts_unbounded_text_property()
    {
        // Arrange
        using var context = new FullTextIndexContext(CreateOptions<FullTextIndexContext>());

        // Act
        var actual = context.Model.FindEntityType(typeof(FullTextIndexEntity));

        // Assert
        Assert.NotNull(actual);
    }

    /// <summary>
    /// Verifies that ordinary indexes still reject an unbounded text key part.
    /// </summary>
    [Fact]
    public void Ordinary_index_rejects_unbounded_text_property()
    {
        // Arrange
        using var context = new OrdinaryTextIndexContext(CreateOptions<OrdinaryTextIndexContext>());

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    /// An explicitly bounded utf8mb4 index that exceeds the largest InnoDB key
    /// budget is rejected before SQL generation.
    /// </summary>
    [Fact]
    public void Utf8Mb4_full_index_rejects_one_byte_over_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb4OverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4OverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3072-byte", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not invent a prefix", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The full-column utf8mb4 boundary remains valid and is not rewritten to a
    /// prefix index.
    /// </summary>
    [Fact]
    public void Utf8Mb4_full_index_accepts_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb4BoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4BoundaryScenario>>());

        // Act
        var index = Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());

        // Assert
        Assert.Null(index.GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// A deliberate prefix is measured instead of the complete property.
    /// </summary>
    [Fact]
    public void Utf8Mb4_explicit_prefix_accepts_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb4PrefixBoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4PrefixBoundaryScenario>>());

        // Act
        var index = Assert.Single(context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes());

        // Assert
        Assert.Equal([768], index.GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// Prefixes are subject to the same byte budget as complete key parts.
    /// </summary>
    [Fact]
    public void Utf8Mb4_explicit_prefix_rejects_one_character_over_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb4PrefixOverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb4PrefixOverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3076 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prefix cannot claim more characters than the bounded column stores.
    /// </summary>
    [Fact]
    public void Prefix_longer_than_column_is_rejected()
    {
        // Arrange
        using var context = new IndexWidthContext<PrefixBeyondColumnScenario>(
            CreateOptions<IndexWidthContext<PrefixBeyondColumnScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("exceeds its store length 32", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite definitions include fixed-width key parts in their aggregate
    /// byte budget.
    /// </summary>
    [Fact]
    public void Composite_index_rejects_aggregate_width_over_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<CompositeOverlongScenario>(
            CreateOptions<IndexWidthContext<CompositeOverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3076 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite definitions exactly at the byte boundary remain valid.
    /// </summary>
    [Fact]
    public void Composite_index_accepts_aggregate_width_at_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<CompositeBoundaryScenario>(
            CreateOptions<IndexWidthContext<CompositeBoundaryScenario>>());

        // Act
        var actual = context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes();

        // Assert
        Assert.Single(actual);
    }

    /// <summary>
    /// Character-set width is part of validation; a single-byte charset does
    /// not inherit utf8mb4's four-byte multiplier.
    /// </summary>
    [Fact]
    public void Latin1_full_index_accepts_length_that_utf8mb4_rejects()
    {
        // Arrange
        using var context = new IndexWidthContext<Latin1Scenario>(
            CreateOptions<IndexWidthContext<Latin1Scenario>>());

        // Act
        var actual = context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes();

        // Assert
        Assert.Single(actual);
    }

    /// <summary>
    /// Three-byte character sets retain their own full-key boundary instead of
    /// inheriting either the latin1 or utf8mb4 calculation.
    /// </summary>
    [Fact]
    public void Utf8Mb3_full_index_accepts_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb3BoundaryScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb3BoundaryScenario>>());

        // Act
        var actual = context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes();

        // Assert
        Assert.Single(actual);
    }

    /// <summary>
    /// A three-byte character-set definition one character beyond the absolute
    /// key budget is rejected.
    /// </summary>
    [Fact]
    public void Utf8Mb3_full_index_rejects_one_character_over_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<Utf8Mb3OverlongScenario>(
            CreateOptions<IndexWidthContext<Utf8Mb3OverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3075 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit property collation determines the encoded width before a
    /// broader model character-set default.
    /// </summary>
    [Fact]
    public void Property_collation_overrides_model_character_set_for_index_width()
    {
        // Arrange
        using var context = new IndexWidthContext<CollationOverrideScenario>(
            CreateOptions<IndexWidthContext<CollationOverrideScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binary key parts are measured in bytes without a character-set multiplier.
    /// </summary>
    [Fact]
    public void Binary_full_index_rejects_one_byte_over_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<BinaryOverlongScenario>(
            CreateOptions<IndexWidthContext<BinaryOverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("3073 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binary key lengths are byte-exact at the absolute boundary.
    /// </summary>
    [Fact]
    public void Binary_full_index_accepts_absolute_limit()
    {
        // Arrange
        using var context = new IndexWidthContext<BinaryBoundaryScenario>(
            CreateOptions<IndexWidthContext<BinaryBoundaryScenario>>());

        // Act
        var actual = context.Model.FindEntityType(typeof(IndexWidthEntity))!.GetIndexes();

        // Assert
        Assert.Single(actual);
    }

    /// <summary>
    /// Unique and key definitions share the same fail-closed byte contract.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unique_and_alternate_key_definitions_reject_overlong_utf8mb4_values(
        bool uniqueIndex
    )
    {
        // Arrange
        using DbContext context = uniqueIndex
            ? new IndexWidthContext<UniqueOverlongScenario>(
                CreateOptions<IndexWidthContext<UniqueOverlongScenario>>())
            : new IndexWidthContext<AlternateKeyOverlongScenario>(
                CreateOptions<IndexWidthContext<AlternateKeyOverlongScenario>>());

        // Act
        var failure = Record.Exception(() => _ = context.Model);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Primary keys are evaluated by the same complete-definition contract as
    /// alternate keys and indexes.
    /// </summary>
    [Fact]
    public void Primary_key_rejects_overlong_utf8mb4_value()
    {
        // Arrange
        using var context = new IndexWidthContext<PrimaryKeyOverlongScenario>(
            CreateOptions<IndexWidthContext<PrimaryKeyOverlongScenario>>());

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("key 'PK_IndexWidthEntity'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The convention-created supporting index for a foreign key is validated
    /// without requiring an explicit HasIndex call.
    /// </summary>
    [Fact]
    public void Foreign_key_supporting_index_accepts_absolute_limit()
    {
        // Arrange
        using var context = new ForeignKeyIndexWidthContext(
            CreateOptions<ForeignKeyIndexWidthContext>());

        var dependent = context.Model.FindEntityType(typeof(ForeignKeyIndexWidthDependent));

        // Act
        var index = Assert.Single(dependent!.GetIndexes());

        // Assert
        Assert.True(index.Properties.Single().IsForeignKey());
    }

    [Fact]
    public void Default_table_and_view_schemas_are_preserved_as_database_qualifiers()
    {
        // Arrange
        using var context = CreateSchemaContext();

        // Act
        var defaultSchema = context.Model.GetDefaultSchema();
        var tableEntity = context.Model.FindEntityType(typeof(DdlTestEntity));
        var viewEntity = context.Model.FindEntityType(typeof(DdlTestView));

        // Assert
        Assert.Equal("default_database", defaultSchema);
        Assert.NotNull(tableEntity);
        Assert.Equal("table_database", tableEntity.GetSchema());
        Assert.NotNull(viewEntity);
        Assert.Equal("view_database", viewEntity.GetViewSchema());
    }
}
