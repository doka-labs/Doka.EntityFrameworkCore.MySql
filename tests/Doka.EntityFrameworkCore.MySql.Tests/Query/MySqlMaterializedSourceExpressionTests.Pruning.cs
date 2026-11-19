namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>Every occurrence contributes before a shared fifty-column relation is narrowed.</summary>
    [Fact]
    public void Ordinary_cte_pruning_unions_demands_from_all_reference_aliases()
    {
        // Arrange
        var first = CreateWideReference("first", 91);
        var second = first.WithAlias("second");
        var root = CreateSource("root",
            [first, second],
            [WideProjection("first", "Column01"), WideProjection("second", "Column50")]);

        // Act
        var pruned = PruneCteRoot(root);

        // Assert
        var left = Assert.IsType<MySqlMaterializedSourceExpression>(pruned.Tables[0]);
        var right = Assert.IsType<MySqlMaterializedSourceExpression>(pruned.Tables[1]);
        Assert.Same(left.Definition, right.Definition);
        Assert.Equal(["Column01", "Column50"], left.Source.Projection.Select(projection => projection.Alias));
        Assert.Equal(50, first.Source.Projection.Count);
        Assert.Equal(2, pruned.Projection.Count);
    }

    /// <summary>A shared dependency receives both sides of a diamond before its single canonical rewrite.</summary>
    [Fact]
    public void Ordinary_cte_pruning_propagates_global_demand_through_a_dependency_diamond()
    {
        // Arrange
        var leaf = CreateWideReference("leaf_left", 91);
        var otherLeaf = Assert.IsType<MySqlMaterializedSourceExpression>(leaf.WithAlias("leaf_right"));
        var left = CreateWidePassthrough("left", "left_seed", 92, leaf);
        var right = CreateWidePassthrough("right", "right_seed", 93, otherLeaf);
        var root = CreateSource("root",
            [left, right],
            [WideProjection("left", "Column01"), WideProjection("right", "Column50")]);

        // Act
        var pruned = PruneCteRoot(root);

        // Assert
        var leftResult = Assert.IsType<MySqlMaterializedSourceExpression>(pruned.Tables[0]);
        var rightResult = Assert.IsType<MySqlMaterializedSourceExpression>(pruned.Tables[1]);
        var firstLeaf = Assert.IsType<MySqlMaterializedSourceExpression>(leftResult.Source.Tables[0]);
        var secondLeaf = Assert.IsType<MySqlMaterializedSourceExpression>(rightResult.Source.Tables[0]);
        Assert.Same(firstLeaf.Definition, secondLeaf.Definition);
        Assert.Equal(["Column01", "Column50"], firstLeaf.Source.Projection.Select(projection => projection.Alias));
        Assert.Equal("Column01",
            Assert.Single(leftResult.Source.Projection)
                .Alias);

        Assert.Equal("Column50",
            Assert.Single(rightResult.Source.Projection)
                .Alias);

        Assert.Equal(50, leaf.Source.Projection.Count);
    }

    /// <summary>The legacy shared-value fence keeps its complete schema even in a mixed ordinary graph.</summary>
    [Fact]
    public void Ordinary_cte_pruning_preserves_the_materialized_dependency_schema()
    {
        // Arrange
        var fence = CreateWideReference("fence", 91, MySqlSourceBindingKind.Materialized);
        var ordinary = CreateWidePassthrough("ordinary", "ordinary_seed", 92, fence);
        var root = CreateSource("root", [ordinary], [WideProjection("ordinary", "Column01")]);

        // Act
        var pruned = PruneCteRoot(root);

        // Assert
        var result = Assert.IsType<MySqlMaterializedSourceExpression>(Assert.Single(pruned.Tables));
        var dependency = Assert.IsType<MySqlMaterializedSourceExpression>(Assert.Single(result.Source.Tables));
        Assert.Equal("Column01",
            Assert.Single(result.Source.Projection)
                .Alias);

        Assert.Equal(MySqlSourceBindingKind.Materialized, dependency.Kind);
        Assert.Equal(50, dependency.Source.Projection.Count);
        Assert.Equal(50, fence.Source.Projection.Count);
    }

    /// <summary>Row-only consumers retain their bag without keeping unrelated payload columns.</summary>
    [Fact]
    public void Ordinary_cte_pruning_keeps_a_consumed_relation_with_no_referenced_payload()
    {
        // Arrange
        var reference = CreateWideReference("rows", 91);
        var root = CreateSource("root",
            [reference],
            [new ProjectionExpression(new SqlConstantExpression(1, IntTypeMapping.Default), "Value")]);

        // Act
        var pruned = PruneCteRoot(root);

        // Assert
        var result = Assert.IsType<MySqlMaterializedSourceExpression>(Assert.Single(pruned.Tables));
        Assert.Empty(result.Source.Projection);
        Assert.Single(pruned.Projection);
        Assert.Equal(50, reference.Source.Projection.Count);
    }

    /// <summary>A malformed reference cannot silently read a different schema after the global pass.</summary>
    [Fact]
    public void Ordinary_cte_pruning_rejects_a_column_absent_from_the_canonical_schema()
    {
        // Arrange
        var reference = CreateWideReference("rows", 91);
        var root = CreateSource("root", [reference], [WideProjection("rows", "Missing")]);

        // Act
        var exception = Record.Exception(() => PruneCteRoot(root));

        // Assert
        Assert.Contains("canonical schema",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(50, reference.Source.Projection.Count);
    }

    /// <summary>Creates fifty independent scalar columns so accidental local pruning is visible.</summary>
    private static MySqlMaterializedSourceExpression CreateWideReference(
        string alias,
        int bindingId,
        MySqlSourceBindingKind kind = MySqlSourceBindingKind.Ordinary
    )
    {
        var projections = Enumerable
            .Range(1, 50)
            .Select(index => new ProjectionExpression(
                new SqlConstantExpression(index, IntTypeMapping.Default),
                "Column" + index.ToString("D2", CultureInfo.InvariantCulture)))
            .ToList();

        return new MySqlMaterializedSourceExpression(alias,
            bindingId,
            new MySqlMaterializedSourceDefinition(CreateSource("wide_seed", [], projections), kind),
            annotations: null);
    }

    /// <summary>Creates a separate consumer which initially forwards the entire shared schema.</summary>
    private static MySqlMaterializedSourceExpression CreateWidePassthrough(
        string alias,
        string sourceAlias,
        int bindingId,
        MySqlMaterializedSourceExpression dependency
    )
    {
        var projections = dependency
            .Source
            .Projection
            .Select(projection =>
                WideProjection(dependency.Alias!, projection.Alias))
            .ToList();

        return new MySqlMaterializedSourceExpression(alias,
            bindingId,
            new MySqlMaterializedSourceDefinition(CreateSource(sourceAlias, [dependency], projections),
                MySqlSourceBindingKind.Ordinary),
            annotations: null);
    }

    /// <summary>Creates an actual typed SQL column reference, independent of CLR property discovery.</summary>
    private static ProjectionExpression WideProjection(
        string tableAlias,
        string name
    ) => new(new ColumnExpression(name, tableAlias, typeof(int), IntTypeMapping.Default, nullable: false), name);

    /// <summary>Runs the same top-level shaped-query pruning entry point as query compilation.</summary>
    private static SelectExpression PruneCteRoot(
        SelectExpression root
    )
    {
        var shaped = new ShapedQueryExpression(root, Expression.Constant(0));
        var pruned = Assert.IsType<ShapedQueryExpression>(MySqlCteColumnPruner.Prune(shaped));

        return Assert.IsType<SelectExpression>(pruned.QueryExpression);
    }
}
