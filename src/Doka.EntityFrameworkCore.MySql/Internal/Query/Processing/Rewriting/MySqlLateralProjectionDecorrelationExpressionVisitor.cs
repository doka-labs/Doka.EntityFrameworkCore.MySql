namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reuses an equivalent inner join key for outer-column references in a
/// MySQL LATERAL projection.
/// </summary>
/// <remarks>
/// MySQL 8.4 can reject a nested subquery which references a parent projection
/// column with error 1247 even though the enclosing derived table is LATERAL.
/// Oracle verified the issue and documents its fix for MySQL 9.0. This rewrite
/// only substitutes a column after the LATERAL predicate proves equality with
/// an inner column, so query cardinality and correlation remain unchanged.
/// Sources retrieved 2026-07-28:
/// <see href="https://bugs.mysql.com/bug.php?id=113887">MySQL Bug #113887</see>
/// and
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/lateral-derived-tables.html">
/// MySQL 8.4 Lateral Derived Tables</see>.
/// </remarks>
internal sealed class MySqlLateralProjectionDecorrelationExpressionVisitor
    : MySqlShapedQueryTraversingExpressionVisitor
{
    protected override Expression VisitExtension(
        Expression node
    )
    {
        var visited = base.VisitExtension(node);

        return visited is SelectExpression selectExpression ? RewriteApplyProjections(selectExpression) : visited;
    }

    private static SelectExpression RewriteApplyProjections(
        SelectExpression selectExpression
    )
    {
        TableExpressionBase[]? rewrittenTables = null;

        for (var index = 0; index < selectExpression.Tables.Count; index++)
        {
            var table = selectExpression.Tables[index];
            var inner = MySqlTableExpressionHelper.GetApplySelect(table);

            if (inner is null)
            {
                continue;
            }

            var rewrittenInner = RewriteProjection(inner);
            if (ReferenceEquals(rewrittenInner, inner))
            {
                continue;
            }

            rewrittenTables ??= selectExpression.Tables.ToArray();
            rewrittenTables[index] = table switch
            {
                CrossApplyExpression crossApply => crossApply.Update(rewrittenInner),
                OuterApplyExpression outerApply => outerApply.Update(rewrittenInner),
                _ => table,
            };
        }

        return rewrittenTables is null
            ? selectExpression
            : selectExpression.Update(
                rewrittenTables,
                selectExpression.Predicate,
                selectExpression.GroupBy,
                selectExpression.Having,
                selectExpression.Projection,
                selectExpression.Orderings,
                selectExpression.Offset,
                selectExpression.Limit);
    }

    private static SelectExpression RewriteProjection(
        SelectExpression selectExpression
    )
    {
        var innerAliases = MySqlTableExpressionHelper.CollectAliases(selectExpression.Tables);
        var replacements = FindEquivalentInnerColumns(selectExpression.Predicate, innerAliases);

        if (replacements.Count == 0)
        {
            return selectExpression;
        }

        var rewriter = new EquivalentColumnReplacingExpressionVisitor(replacements);
        var projections = selectExpression
            .Projection.Select(projection => projection.Update((SqlExpression)rewriter.Visit(projection.Expression)))
            .ToArray();

        return rewriter.ReplacementCount == 0
            ? selectExpression
            : selectExpression.Update(
                selectExpression.Tables,
                selectExpression.Predicate,
                selectExpression.GroupBy,
                selectExpression.Having,
                projections,
                selectExpression.Orderings,
                selectExpression.Offset,
                selectExpression.Limit);
    }

    private static Dictionary<(string TableAlias, string Name), ColumnExpression> FindEquivalentInnerColumns(
        SqlExpression? predicate,
        IReadOnlySet<string> innerAliases
    )
    {
        var replacements = new Dictionary<(string TableAlias, string Name), ColumnExpression>();
        var conflicts = new HashSet<(string TableAlias, string Name)>();

        CollectEquivalentInnerColumns(predicate, innerAliases, replacements, conflicts);

        return replacements;
    }

    private static void CollectEquivalentInnerColumns(
        SqlExpression? expression,
        IReadOnlySet<string> innerAliases,
        IDictionary<(string TableAlias, string Name), ColumnExpression> replacements,
        ISet<(string TableAlias, string Name)> conflicts
    )
    {
        if (expression is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } conjunction)
        {
            CollectEquivalentInnerColumns(conjunction.Left, innerAliases, replacements, conflicts);
            CollectEquivalentInnerColumns(conjunction.Right, innerAliases, replacements, conflicts);
            return;
        }

        if (expression is not SqlBinaryExpression
            {
                OperatorType: ExpressionType.Equal, Left: ColumnExpression left, Right: ColumnExpression right,
            })
        {
            return;
        }

        var leftIsInner = innerAliases.Contains(left.TableAlias);
        var rightIsInner = innerAliases.Contains(right.TableAlias);

        if (leftIsInner == rightIsInner)
        {
            return;
        }

        var outer = leftIsInner ? right : left;
        var inner = leftIsInner ? left : right;
        var key = (outer.TableAlias, outer.Name);

        if (conflicts.Contains(key))
        {
            return;
        }

        if (replacements.TryGetValue(key, out var existing)
            && (existing.TableAlias != inner.TableAlias || existing.Name != inner.Name))
        {
            replacements.Remove(key);
            conflicts.Add(key);
            return;
        }

        replacements[key] = inner;
    }

    private sealed class EquivalentColumnReplacingExpressionVisitor : ExpressionVisitor
    {
        private readonly IReadOnlyDictionary<(string TableAlias, string Name), ColumnExpression> _replacements;

        public EquivalentColumnReplacingExpressionVisitor(
            IReadOnlyDictionary<(string TableAlias, string Name), ColumnExpression> replacements
        )
        {
            _replacements = replacements;
        }

        public int ReplacementCount { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ColumnExpression column
                && _replacements.TryGetValue((column.TableAlias, column.Name), out var replacement))
            {
                ReplacementCount++;
                return replacement;
            }

            return base.VisitExtension(node);
        }
    }
}
