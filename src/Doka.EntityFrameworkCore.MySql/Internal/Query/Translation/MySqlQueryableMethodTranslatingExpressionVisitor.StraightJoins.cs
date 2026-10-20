namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    private readonly MySqlQueryableMethodTranslatingExpressionVisitor? _straightJoinOwner;
    private HashSet<SelectExpression>? _straightJoinInputs;

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression methodCallExpression
    )
    {
        if (methodCallExpression.Method.DeclaringType == typeof(MySqlStraightJoinLowering)
            && methodCallExpression.Method.IsGenericMethod
            && methodCallExpression.Method.GetGenericMethodDefinition() == MySqlStraightJoinLowering.MarkInnerMethod)
        {
            var translated = Visit(methodCallExpression.Arguments[0]) as ShapedQueryExpression
                ?? throw new InvalidOperationException("The StraightJoin inner input could not be translated to SQL.");

            // WHY: Unrelated queries allocate no directed-edge tracking state. Subqueries share
            // their root visitor's lazy set so a marker never becomes model- or context-global.
            var owner = _straightJoinOwner ?? this;
            (owner._straightJoinInputs ??= new HashSet<SelectExpression>(ReferenceEqualityComparer.Instance)).Add(
                (SelectExpression)translated.QueryExpression);

            return translated;
        }

        return base.VisitMethodCall(methodCallExpression);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector
    )
    {
        var directed = (_straightJoinOwner ?? this)._straightJoinInputs?.Remove((SelectExpression)inner.QueryExpression)
            == true;
        var select = (SelectExpression)outer.QueryExpression;
        var translated = base.TranslateJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector);

        if (!directed
            || translated is null)
        {
            return translated;
        }

        if (select.Tables.Count == 0
            || select.Tables[^1] is not InnerJoinExpression edge)
        {
            throw new InvalidOperationException(
                "EF did not retain the expected StraightJoin edge after ordinary join translation.");
        }

        // WHY: Reuse EF's complete join/shaper/identifier processing and change only its appended edge.
        // SetTables clears its backing list, so a separate array is required even for this one replacement.
        var tables = select.Tables.ToArray();
        tables[^1] = new MySqlStraightJoinExpression(
            edge.Table,
            edge.JoinPredicate,
            edge.IsPrunable,
            edge
                .GetAnnotations()
                .ToDictionary(annotation => annotation.Name, StringComparer.Ordinal));

#pragma warning disable EF1001 // Narrow mutable-table replacement after EF's ordinary Join; qualified against the supported EF patches.
        select.SetTables(tables);
#pragma warning restore EF1001

        return translated;
    }
}
