namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// An application's converter size hint is a preexisting storage contract;
    /// introducing the allocator must not widen or replace it.
    /// </summary>
    [Fact]
    public void Converter_size_hints_are_preserved_without_introducing_a_core_length_facet()
    {
        using var context = CreateContext("converter-hint");

        var property = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!.FindProperty("B")!;

        Assert.Null(property.GetMaxLength());
        Assert.Equal(32, property.GetValueConverter()!.MappingHints!.Size);
        Assert.Equal("varchar(32)", property.GetRelationalTypeMapping().StoreType);
    }

    /// <summary>
    /// A second execution of the same convention remains monotonic and retains
    /// ownership of the exact original report model's length facets.
    /// </summary>
    [Fact]
    public void Repeated_finalization_keeps_the_same_safe_convention_lengths()
    {
        using var context = CreateContext("twice");

        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("Budget")!;

        Assert.Equal(254, entity.FindProperty("Id")!.GetMaxLength());
        Assert.Equal(254, entity.FindProperty("Scope")!.GetMaxLength());
        Assert.Equal(254, entity.FindProperty("Tree")!.GetMaxLength());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)entity.FindProperty("Id")!).GetMaxLengthConfigurationSource());
    }

    /// <summary>
    /// Each concrete TPC table contributes its actual charset to inherited key
    /// and index allocation instead of using only the abstract declaring type.
    /// </summary>
    [Fact]
    public void Tpc_indexes_use_the_concrete_physical_table_character_set()
    {
        using var context = CreateContext("tpc");

        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(BudgetTpcRow))!;

        Assert.Equal(254, entity.FindProperty(nameof(BudgetTpcBase.Code))!.GetMaxLength());
        Assert.Equal(254, entity.FindProperty(nameof(BudgetTpcBase.Scope))!.GetMaxLength());
        Assert.Equal(254, entity.FindProperty(nameof(BudgetTpcBase.Tree))!.GetMaxLength());
        Assert.Contains("varchar(254)", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An application-defined length or store type on a physical column remains
    /// fixed while its implicit shared-table alias receives compatible metadata.
    /// </summary>
    [Theory]
    [InlineData("shared-explicit-length", 700, "varchar(700)", 68)]
    [InlineData("shared-explicit-store", 200, "char(200)", 255)]
    public void Shared_column_aliases_preserve_the_explicit_physical_contract(
        string scenario,
        int length,
        string storeType,
        int spareLength
    )
    {
        using var context = CreateContext(scenario);

        var model = context.GetService<IDesignTimeModel>().Model;
        var owner = model.FindEntityType(typeof(BudgetOwner))!;
        var detail = model.FindEntityType(typeof(BudgetDetail))!;

        Assert.Equal(length, owner.FindProperty(nameof(BudgetOwner.Code))!.GetMaxLength());
        Assert.Equal(storeType, owner.FindProperty(nameof(BudgetOwner.Code))!.GetRelationalTypeMapping().StoreType);
        Assert.Equal(storeType, detail.FindProperty(nameof(BudgetDetail.Code))!.GetRelationalTypeMapping().StoreType);
        Assert.Equal(
            scenario == "shared-explicit-store" ? storeType : null,
            detail.FindProperty(nameof(BudgetDetail.Code))!.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
        Assert.Equal(spareLength, owner.FindProperty(nameof(BudgetOwner.Spare))!.GetMaxLength());
    }

    /// <summary>
    /// A column shared with a secondary entity-splitting fragment receives the
    /// strictest budget of that actual physical table.
    /// </summary>
    [Fact]
    public void Split_table_fragments_participate_in_physical_alias_allocation()
    {
        using var context = CreateContext("shared-split");

        var model = context.GetService<IDesignTimeModel>().Model;
        var owner = model.FindEntityType(typeof(BudgetOwner))!;
        var detail = model.FindEntityType(typeof(BudgetDetail))!;

        Assert.Equal(68, owner.FindProperty(nameof(BudgetOwner.Code))!.GetMaxLength());
        Assert.Equal(68, detail.FindProperty(nameof(BudgetDetail.Code))!.GetMaxLength());
    }

    /// <summary>
    /// Explicit ENUM/SET storage consumes a conservative bounded scalar reserve
    /// without rejecting an otherwise valid mixed implicit string index.
    /// </summary>
    [Theory]
    [InlineData("enum", 63)]
    [InlineData("set", 62)]
    public void Native_enumerated_store_types_have_a_safe_bounded_reserve(
        string scenario,
        int length
    )
    {
        using var context = CreateContext(scenario);

        var property = context.Model.FindEntityType("Budget")!.FindProperty("B")!;

        Assert.Equal(length, property.GetMaxLength());
    }

    /// <summary>
    /// The binary reader escape changes physical store width without changing
    /// an application's conceptual length on shared column aliases.
    /// </summary>
    [Fact]
    public void Shared_binary_aliases_retain_the_explicit_conceptual_length()
    {
        using var context = CreateContext("shared-binary");

        var model = context.GetService<IDesignTimeModel>().Model;
        var owner = model.FindEntityType(typeof(BudgetBinaryOwner))!;
        var detail = model.FindEntityType(typeof(BudgetBinaryDetail))!;

        Assert.Equal(16, owner.FindProperty(nameof(BudgetBinaryOwner.Code))!.GetMaxLength());
        Assert.Equal(16, detail.FindProperty(nameof(BudgetBinaryDetail.Code))!.GetMaxLength());
        Assert.Equal(
            "varbinary(17)",
            owner.FindProperty(nameof(BudgetBinaryOwner.Code))!.GetRelationalTypeMapping().StoreType);
    }

    /// <summary>
    /// A physical store-type fallback must not replace a deliberate conceptual
    /// byte-array length merely because both share varbinary(17) storage.
    /// </summary>
    [Fact]
    public void Explicit_conceptual_shared_length_takes_priority_over_physical_store_length()
    {
        using var context = CreateContext("shared-binary-mixed");

        var model = context.GetService<IDesignTimeModel>().Model;
        var owner = model.FindEntityType(typeof(BudgetBinaryOwner))!;
        var detail = model.FindEntityType(typeof(BudgetBinaryDetail))!;
        var storeDetail = model.FindEntityType(typeof(BudgetBinaryStoreDetail))!;

        Assert.Equal(16, owner.FindProperty(nameof(BudgetBinaryOwner.Code))!.GetMaxLength());
        Assert.Equal(16, detail.FindProperty(nameof(BudgetBinaryDetail.Code))!.GetMaxLength());
        Assert.Null(storeDetail.FindProperty(nameof(BudgetBinaryStoreDetail.Code))!.GetMaxLength());
        Assert.Equal(
            "varbinary(17)",
            owner.FindProperty(nameof(BudgetBinaryOwner.Code))!.GetRelationalTypeMapping().StoreType);
    }

    private sealed class BudgetBinaryStoreDetail
    {
        public byte[] Code { get; set; } = [];
    }

    private sealed class BudgetBinaryOwner
    {
        public int Id { get; set; }

        public byte[] Code { get; set; } = [];

        public BudgetBinaryDetail Detail { get; set; } = new();

        public BudgetBinaryStoreDetail StoreDetail { get; set; } = new();
    }

    private sealed class BudgetBinaryDetail
    {
        public byte[] Code { get; set; } = [];
    }

    /// <summary>
    /// An owned entity's index uses its shared physical table's configured
    /// charset even when only the owner declares that table default.
    /// </summary>
    [Fact]
    public void Owned_indexes_use_the_owner_configured_physical_table_character_set()
    {
        using var context = CreateContext("owned-table-charset");

        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(BudgetDefaultDetail))!;
        var length = entity.FindProperty(nameof(BudgetDefaultDetail.B))!.GetMaxLength();
        var ddl = context.Database.GenerateCreateScript();

        Assert.True(length == 68, $"Expected B length68; actual={length}. Physical DDL: {ddl}");
        Assert.Contains("CHARACTER SET utf8mb4", ddl, StringComparison.Ordinal);
        Assert.Equal(700, entity.FindProperty(nameof(BudgetDefaultDetail.A))!.GetMaxLength());
        Assert.Null(Assert.Single(entity.GetIndexes()).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// A narrower physical table charset and an explicit property collation
    /// retain their own widths instead of inheriting a global four-byte reserve.
    /// </summary>
    [Theory]
    [InlineData("owned-table-narrow", "latin1")]
    [InlineData("owned-table-property-collation", "utf8mb4")]
    public void Owned_table_and_property_encoding_overrides_remain_precise(
        string scenario,
        string tableCharSet
    )
    {
        using var context = CreateContext(scenario);

        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(BudgetDefaultDetail))!;
        var length = entity.FindProperty(nameof(BudgetDefaultDetail.B))!.GetMaxLength();
        var ddl = context.Database.GenerateCreateScript();

        Assert.Equal(255, length);
        Assert.Contains("CHARACTER SET " + tableCharSet, ddl, StringComparison.Ordinal);
        Assert.Equal(700, entity.FindProperty(nameof(BudgetDefaultDetail.A))!.GetMaxLength());
    }

    /// <summary>
    /// Entirely explicit owned indexes are validated against the same physical
    /// table default before their overlong CREATE INDEX can reach the database.
    /// </summary>
    [Fact]
    public void Owned_explicit_indexes_reject_the_owner_table_byte_overflow()
    {
        using var context = CreateContext("owned-table-overlong");

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("3200 bytes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3072-byte", exception.Message, StringComparison.Ordinal);
    }

    private sealed class BudgetDefaultOwner
    {
        public int Id { get; set; }

        public BudgetDefaultDetail Detail { get; set; } = new();
    }

    private sealed class BudgetDefaultDetail
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;
    }

    private abstract class BudgetTpcBase
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        public string Tree { get; set; } = string.Empty;

        public long Left { get; set; }

        public long Right { get; set; }

        public bool Flag { get; set; }
    }

    private sealed class BudgetTpcRow : BudgetTpcBase { }
}
