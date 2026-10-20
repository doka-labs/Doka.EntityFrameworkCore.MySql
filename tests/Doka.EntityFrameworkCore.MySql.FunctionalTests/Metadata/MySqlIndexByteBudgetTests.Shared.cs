namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Physical column aliases on a shared table retain one compatible length
    /// when their separate model indexes impose different budgets.
    /// </summary>
    [Fact]
    public void Shared_table_column_aliases_share_the_strictest_implicit_length()
    {
        using var context = CreateContext("shared-table");

        var model = context.GetService<IDesignTimeModel>().Model;
        var ownerProperty = model.FindEntityType(typeof(BudgetOwner))!.FindProperty(nameof(BudgetOwner.Code))!;
        var detailProperty = model.FindEntityType(typeof(BudgetDetail))!.FindProperty(nameof(BudgetDetail.Code))!;

        Assert.Equal(68, ownerProperty.GetMaxLength());
        Assert.Equal(68, detailProperty.GetMaxLength());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)ownerProperty).GetMaxLengthConfigurationSource());
        Assert.Equal(
            ConfigurationSource.Convention,
            ((IConventionProperty)detailProperty).GetMaxLengthConfigurationSource());
    }

    /// <summary>
    /// A deliberate TEXT or BLOB prefix is a bounded key part even when the
    /// stored value itself is unbounded.
    /// </summary>
    [Theory]
    [InlineData("text-prefix", 68)]
    [InlineData("blob-prefix", 255)]
    public void Explicit_large_object_prefix_reserves_only_its_deliberate_width(
        string scenario,
        int expectedLength
    )
    {
        using var context = CreateContext(scenario);

        var entity = context.Model.FindEntityType("Budget")!;

        Assert.Equal(expectedLength, entity.FindProperty("B")!.GetMaxLength());
        Assert.Equal([700, 0], Assert.Single(entity.GetIndexes()).GetMySqlIndexPrefixLengths());
    }

    /// <summary>
    /// An oversized explicit large-object prefix cannot be hidden by shortening
    /// the other implicit key part.
    /// </summary>
    [Fact]
    public void Oversized_large_object_prefix_fails_closed()
    {
        using var context = CreateContext("text-prefix-overlong");

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("minimum implicit lengths", exception.Message, StringComparison.Ordinal);
    }

    private sealed class BudgetOwner
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Spare { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public BudgetDetail Detail { get; set; } = new();
    }

    private sealed class BudgetDetail
    {
        public string Code { get; set; } = string.Empty;

        public string Wide { get; set; } = string.Empty;
    }
}
