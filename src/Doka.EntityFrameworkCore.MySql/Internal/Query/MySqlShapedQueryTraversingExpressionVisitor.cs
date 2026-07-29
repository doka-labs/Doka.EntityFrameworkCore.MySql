namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Traverses both the relational query and shaper branches of shaped query
/// expressions.
/// </summary>
/// <remarks>
/// <see cref="ExpressionVisitor"/> does not traverse provider-specific
/// extension nodes automatically. Query-processing visitors derive from this
/// type so neither branch is skipped as EF Core query shapes evolve.
/// </remarks>
internal abstract class MySqlShapedQueryTraversingExpressionVisitor : ExpressionVisitor
{
    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is ShapedQueryExpression shapedQueryExpression)
        {
            return shapedQueryExpression
                .UpdateQueryExpression(Visit(shapedQueryExpression.QueryExpression))
                .UpdateShaperExpression(Visit(shapedQueryExpression.ShaperExpression));
        }

        return base.VisitExtension(node);
    }
}
