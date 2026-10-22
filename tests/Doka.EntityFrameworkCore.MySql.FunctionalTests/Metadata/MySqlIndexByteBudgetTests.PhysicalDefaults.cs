namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    /// <summary>
    /// Captured pre-finalization defaults must match the annotations that the
    /// provider actually emits for every physical table mapping.
    /// </summary>
    [Theory]
    [InlineData("owned-table-charset")]
    [InlineData("owned-table-narrow")]
    [InlineData("owned-table-first-wins")]
    [InlineData("owned-table-collation-first-wins")]
    [InlineData("shared-split")]
    [InlineData("tpc")]
    [InlineData("defaults-tph")]
    [InlineData("defaults-tpt")]
    [InlineData("table-collation")]
    [InlineData("legacy-table-collation")]
    public void Captured_physical_defaults_match_emitted_table_annotations(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;

        var captured = MySqlIndexByteBudget.GetPhysicalTableDefaults(model);
        var tables = model.GetRelationalModel().Tables.ToArray();

        // Assert
        Assert.NotEmpty(tables);
        Assert.All(tables, table =>
        {
            var defaults = captured[StoreObjectIdentifier.Table(table.Name, table.Schema)];

            Assert.Equal(table.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string, defaults.CharSet);
            Assert.Equal(
                table.FindAnnotation(RelationalAnnotationNames.Collation)?.Value as string,
                defaults.Collation);
        });
    }

    /// <summary>
    /// Conflicting entity annotations retain the provider's existing first-wins
    /// physical table contract instead of imposing an unrelated four-byte cap.
    /// </summary>
    [Theory]
    [InlineData("owned-table-first-wins")]
    [InlineData("owned-table-collation-first-wins")]
    public void First_wins_table_defaults_do_not_reject_valid_narrow_indexes(
        string scenario
    )
    {
        // Arrange
        using var context = CreateContext(scenario);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var detail = model.FindEntityType(typeof(BudgetDefaultDetail))!;
        var ddl = context.Database.GenerateCreateScript();

        // Assert
        Assert.Equal(1200, detail.FindProperty(nameof(BudgetDefaultDetail.A))!.GetMaxLength());
        Assert.Equal(255, detail.FindProperty(nameof(BudgetDefaultDetail.B))!.GetMaxLength());
        Assert.Contains("CHARACTER SET latin1", ddl, StringComparison.Ordinal);
        Assert.DoesNotContain("CHARACTER SET utf8mb4", ddl, StringComparison.Ordinal);
    }

    private class BudgetDefaultBase
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class BudgetDefaultDerived : BudgetDefaultBase
    {
        public string Payload { get; set; } = string.Empty;
    }
}
