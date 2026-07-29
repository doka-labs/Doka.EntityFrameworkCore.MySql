namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the shared shaped-query traversal contract.
/// </summary>
public sealed class MySqlShapedQueryTraversingExpressionVisitorTests
{
    /// <summary>
    /// Verifies that provider visitors reach both the relational query and its
    /// result shaper.
    /// </summary>
    [Fact]
    public void Visit_traverses_query_and_shaper_branches()
    {
        var visitor = new RecordingExpressionVisitor();
        var shapedQuery = new ShapedQueryExpression(Expression.Constant("query"), Expression.Constant("shaper"));

        visitor.Visit(shapedQuery);

        Assert.Equal(
            [
                "query",
                "shaper",
            ],
            visitor.VisitedValues);
    }

    private sealed class RecordingExpressionVisitor : MySqlShapedQueryTraversingExpressionVisitor
    {
        public List<string> VisitedValues { get; } = [];

        protected override Expression VisitConstant(
            ConstantExpression node
        )
        {
            if (node.Value is string value)
            {
                VisitedValues.Add(value);
            }

            return base.VisitConstant(node);
        }
    }
}
