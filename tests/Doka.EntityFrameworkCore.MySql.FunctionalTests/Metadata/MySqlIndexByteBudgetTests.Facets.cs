using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Explicit facets consume their physical width without being shortened.
    /// </summary>
    [Theory]
    [InlineData("explicit-length", 68, "varchar(700)")]
    [InlineData("explicit-store", 68, "varchar(700)")]
    [InlineData("latin1", 255, "varchar(700)")]
    [InlineData("property-collation", 68, "varchar(700)")]
    [InlineData("unknown-charset", 68, "varchar(700)")]
    [InlineData("table-collation", 68, "varchar(700)")]
    [InlineData("legacy-table-collation", 255, "varchar(700)")]
    public void Mixed_explicit_and_implicit_facets_use_physical_character_widths(
        string scenario,
        int expectedLength,
        string explicitStoreType
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var explicitProperty = entity.FindProperty("A")!;
        var implicitProperty = entity.FindProperty("B")!;

        // Assert
        Assert.Equal(expectedLength, implicitProperty.GetMaxLength());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)implicitProperty).GetMaxLengthConfigurationSource());
        Assert.Equal(explicitStoreType, explicitProperty.GetRelationalTypeMapping().StoreType);
        Assert.Null(Assert.Single(entity.GetIndexes()).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// A physical binary(16) escape maps a length-16 byte array to varbinary(17).
    /// </summary>
    [Fact]
    public void Binary_mapping_discontinuity_is_measured_before_selecting_the_cap()
    {
        // Arrange
        using var context = CreateContext("binary-boundary");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var property = entity.FindProperty("B")!;

        // Assert
        Assert.Equal(15, property.GetMaxLength());
        Assert.Equal("varbinary(15)", property.GetRelationalTypeMapping().StoreType);
        Assert.Equal(3056, entity.FindProperty("A")!.GetMaxLength());
    }

    /// <summary>
    /// Converters remain owned by the application while their provider string
    /// representation participates in the same implicit allocation.
    /// </summary>
    [Fact]
    public void Converted_properties_retain_the_converter_and_share_the_provider_byte_budget()
    {
        // Arrange
        using var context = CreateContext("converter");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var property = entity.FindProperty("B")!;

        // Assert
        Assert.Equal(68, property.GetMaxLength());
        Assert.Equal(typeof(BudgetCode), property.ClrType);
        Assert.IsType<ValueConverter<BudgetCode, string>>(property.GetValueConverter());
        Assert.Equal("varchar(68)", property.GetRelationalTypeMapping().StoreType);
    }

    /// <summary>
    /// GUID normalization must precede allocation so binary and textual formats
    /// reserve their actual fixed physical width.
    /// </summary>
    [Theory]
    [InlineData("guid-binary", 164, "binary(16)")]
    [InlineData("guid-char", 132, "char(36)")]
    [InlineData("guid-uuid", 164, "uuid")]
    public void Guid_formats_are_normalized_before_implicit_string_allocation(
        string scenario,
        int expectedLength,
        string expectedGuidStoreType
    )
    {
        // Arrange
        using var context = CreateContext(
            scenario,
            MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var property = entity.FindProperty("B")!;

        // Assert
        Assert.Equal(expectedLength, property.GetMaxLength());
        Assert.Equal(expectedGuidStoreType, entity.FindProperty("G")!.GetRelationalTypeMapping().StoreType);
        Assert.Equal(600, entity.FindProperty("A")!.GetMaxLength());
    }

    /// <summary>
    /// A deliberate prefix remains exact and does not replace the full property
    /// length used by another index.
    /// </summary>
    [Fact]
    public void Explicit_prefixes_retain_their_minimum_column_length()
    {
        // Arrange
        using var context = CreateContext("prefix");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var property = entity.FindProperty("B")!;
        var prefixed = entity.GetIndexes().Single(index => index.GetMySqlIndexPrefixLengths() is not null);

        // Assert
        Assert.Equal(200, property.GetMaxLength());
        Assert.Equal([0, 200], prefixed.GetMySqlIndexPrefixLengths());
        Assert.Equal(568, entity.FindProperty("A")!.GetMaxLength());
        Assert.Null(entity.GetIndexes().Single(index => index != prefixed).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// A prefix-only occurrence reserves its own bytes without reducing the
    /// full capacity of an otherwise unconstrained implicit column.
    /// </summary>
    [Fact]
    public void Prefix_only_implicit_column_keeps_its_full_conventional_length()
    {
        // Arrange
        using var context = CreateContext("prefix-only-implicit");

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var index = Assert.Single(entity.GetIndexes());
        var ddl = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(255, entity.FindProperty("A")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("B")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("C")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("D")!.GetMaxLength());
        Assert.Equal([50, 0, 0, 0], index.GetMySqlIndexPrefixLengths());
        Assert.Contains("`A` varchar(255)", ddl, StringComparison.Ordinal);
        Assert.Contains("(`A`(50), `B`, `C`, `D`)", ddl, StringComparison.Ordinal);
    }

    /// <summary>
    /// A complete overlapping index constrains the same implicit property
    /// independently of the declaration order of its prefixed occurrence.
    /// </summary>
    [Theory]
    [InlineData("prefix-overlap")]
    [InlineData("prefix-overlap-reverse")]
    public void Complete_overlapping_index_constrains_prefixed_implicit_column(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;
        var prefixed = entity.GetIndexes().Single(index => index.GetMySqlIndexPrefixLengths() is not null);

        // Assert
        Assert.Equal(68, entity.FindProperty("A")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("B")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("C")!.GetMaxLength());
        Assert.Equal(239, entity.FindProperty("D")!.GetMaxLength());
        Assert.Equal(700, entity.FindProperty("E")!.GetMaxLength());
        Assert.Equal([50, 0, 0, 0], prefixed.GetMySqlIndexPrefixLengths());
        Assert.Null(entity.GetIndexes().Single(index => index != prefixed).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// Unsatisfiable explicit widths and prefix minima fail without silently
    /// shortening application data or changing complete-index semantics.
    /// </summary>
    [Theory]
    [InlineData("impossible-explicit")]
    [InlineData("impossible-prefix")]
    [InlineData("prefix-over-ceiling")]
    [InlineData("prefix-overlap-impossible")]
    public void Impossible_implicit_allocation_fails_closed(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        // Assert
        Assert.Contains("implicit", exception.Message, StringComparison.Ordinal);
        Assert.Contains("prefix", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fresh model instances and generated DDL retain the same complete physical
    /// definition instead of relying on an in-process cached model.
    /// </summary>
    [Fact]
    public void Rebuilt_models_and_migration_ddl_keep_safe_lengths_without_prefixes()
    {
        // Arrange
        using var first = CreateContext("forward");
        using var second = CreateContext("repeat");

        // Act
        var firstModel = first.GetService<IDesignTimeModel>().Model;
        var secondModel = second.GetService<IDesignTimeModel>().Model;
        var firstDdl = first.Database.GenerateCreateScript();

        var secondDdl = second.Database.GenerateCreateScript();

        // Assert
        Assert.NotSame(firstModel, secondModel);
        Assert.Equal(firstDdl, secondDdl);
        Assert.Contains("`Id` varchar(254)", firstDdl, StringComparison.Ordinal);
        Assert.Contains("(`Scope`, `Flag`, `Tree`, `R`, `L`, `Id`)", firstDdl, StringComparison.Ordinal);
        Assert.DoesNotContain("`Id`(", firstDdl, StringComparison.Ordinal);
    }

    private readonly record struct BudgetCode(string Value);
}
