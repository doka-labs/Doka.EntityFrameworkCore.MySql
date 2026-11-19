namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>Only the actual typed recursive scope overrides ordinary MySQL's safe LATERAL policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Simple_apply_is_flattened_for_a_direct_recursive_frontier_only(
        bool recursive
    )
    {
        // Arrange
        using var context = CreateContext();
        var source = CreateRecursiveApplySource(recursive, hasLimit: false);
        var visitor = new MySqlApplyRewritingExpressionVisitor(context.GetService<ISqlExpressionFactory>(),
            flattenJsonTablesOnly: true);

        // Act
        var rewritten = Assert.IsType<SelectExpression>(visitor.Visit(source));

        // Assert
        if (recursive)
        {
            var join = Assert.IsType<InnerJoinExpression>(rewritten.Tables[1]);
            Assert.Equal("physical", join.Table.Alias);
            Assert.IsType<MySqlRecursiveCteReferenceExpression>(rewritten.Tables[0]);
            Assert.Equal("physical",
                Assert.IsType<ColumnExpression>(Assert.Single(rewritten.Projection)
                        .Expression)
                    .TableAlias);
        }
        else
        {
            Assert.IsType<CrossApplyExpression>(rewritten.Tables[1]);
            Assert.Same(source, rewritten);
        }
    }

    /// <summary>A recursive frontier cannot make correlated LIMIT safe to move across its cardinality boundary.</summary>
    [Fact]
    public void Recursive_scope_does_not_flatten_a_paged_apply()
    {
        // Arrange
        using var context = CreateContext();
        var source = CreateRecursiveApplySource(recursive: true, hasLimit: true);
        var visitor = new MySqlApplyRewritingExpressionVisitor(context.GetService<ISqlExpressionFactory>(),
            flattenJsonTablesOnly: true);

        // Act
        var rewritten = Assert.IsType<SelectExpression>(visitor.Visit(source));

        // Assert
        var apply = Assert.IsType<CrossApplyExpression>(rewritten.Tables[1]);
        var inner = Assert.IsType<SelectExpression>(apply.Table);
        Assert.Equal(1,
            Assert.IsType<SqlConstantExpression>(inner.Limit)
                .Value);

        Assert.Same(source, rewritten);
    }

    /// <summary>Builds an actual correlated APPLY with a mapped column and an optional semantic page.</summary>
    private static SelectExpression CreateRecursiveApplySource(
        bool recursive,
        bool hasLimit
    )
    {
        TableExpressionBase frontier = recursive
            ? new MySqlRecursiveCteReferenceExpression("frontier",
                91,
                new SqlConstantExpression(2, IntTypeMapping.Default))
            : CreateWideReference("frontier", 91);

        var physical = CreateWideReference("physical", 92);
        var inner = CreateSource("inner", [physical], [WideProjection("physical", "Column01")]);
        var predicate = new SqlBinaryExpression(ExpressionType.Equal,
            WideProjection("physical", "Column01")
                .Expression,
            WideProjection("frontier", "Column01")
                .Expression,
            typeof(bool),
            BoolTypeMapping.Default);

        inner = inner.Update(inner.Tables,
            predicate,
            inner.GroupBy,
            inner.Having,
            inner.Projection,
            inner.Orderings,
            offset: null,
            limit: hasLimit
                ? new SqlConstantExpression(1, IntTypeMapping.Default)
                : null);

        return CreateSource("outer",
            [frontier, new CrossApplyExpression(inner)],
            [WideProjection("inner", "Column01")]);
    }
}
