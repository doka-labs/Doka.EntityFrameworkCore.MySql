namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Lowers directed joins and detects completed sources in one traversal before ordinary EF processing.</summary>
internal sealed class MySqlStraightJoinLowering : ExpressionVisitor
{
    /// <summary>Identifies the one private inner-source marker consumed by Doka translation.</summary>
    internal static readonly MethodInfo MarkInnerMethod = typeof(MySqlStraightJoinLowering).GetMethod(
        nameof(MarkInner),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Gets whether this query contains the exact completed-source binding operator.</summary>
    public bool ContainsMaterializedSource { get; private set; }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (node.Method.DeclaringType != typeof(MySqlQueryableExtensions)
            || !node.Method.IsGenericMethod)
        {
            return base.VisitMethodCall(node);
        }

        var definition = node.Method.GetGenericMethodDefinition();
        ContainsMaterializedSource |= definition == MySqlQueryableExtensions.WithMaterializedSourceMethod;

        if (definition != MySqlQueryableExtensions.StraightJoinMethod)
        {
            return base.VisitMethodCall(node);
        }

        var arguments = node.Method.GetGenericArguments();
        var outer = Visit(node.Arguments[0]);
        var inner = Visit(node.Arguments[1]);
        var marked = Expression.Call(CloseKnownMethod(MarkInnerMethod, [arguments[1]]), inner);

        // WHY: NavigationExpansion recognizes the exact Queryable.Join operator. Lower before both
        // ordinary and compiled-query preprocessing, and let EF process all three selectors normally.
        return Expression.Call(
            CloseKnownMethod(QueryableMethods.Join, arguments),
            outer,
            marked,
            Visit(node.Arguments[2]),
            Visit(node.Arguments[3]),
            Visit(node.Arguments[4]));
    }

    /// <summary>Closes only the two known unconstrained methods used by EF's runtime translation.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification =
            "Only the statically preserved Join and unary-marker definitions are accepted; neither has generic member-preservation requirements.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification =
            "Runtime EF query compilation closes expression methods here; this is not a new NativeAOT execution guarantee. Precompiled SQL quotation has separate tests.")]
    private static MethodInfo CloseKnownMethod(
        MethodInfo definition,
        Type[] arguments
    )
    {
        if (definition != QueryableMethods.Join
            && definition != MarkInnerMethod)
        {
            throw new InvalidOperationException("Only the exact StraightJoin lowering methods may be closed here.");
        }

        // WHY: Compiled query lambdas do not execute the public generic factory. Their closed CLR
        // types become available only in MethodCallExpression metadata during EF preprocessing.
        return definition.MakeGenericMethod(arguments);
    }

    /// <summary>Marks only an inner input during translation; application code never executes this method.</summary>
    // WHY: EF's navigation expansion passes this marker through only for the exact
    // static unary IQueryable<T> -> IQueryable<T> shape with one generic type argument.
    private static IQueryable<T> MarkInner<T>(
        IQueryable<T> source
    ) => throw new InvalidOperationException("A StraightJoin input marker is only valid during Doka SQL translation.");
}
