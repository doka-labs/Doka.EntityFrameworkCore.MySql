namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves the completed relational scope of indexed selectors and explicit window projections before navigation expansion.</summary>
internal sealed partial class MySqlIndexedSelectLowering : ExpressionVisitor
{
    /// <summary>Identifies the exact same-element query boundary consumed after navigation expansion.</summary>
    internal static readonly MethodInfo CompleteMethod = typeof(MySqlIndexedSelectLowering).GetMethod(
        nameof(Complete),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Identifies the zero-based index of the completed ordered source.</summary>
    internal static readonly MethodInfo IndexMethod = typeof(MySqlIndexedSelectLowering).GetMethod(
        nameof(Index),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Identifies an explicitly partitioned group-member index and its retained-row scope.</summary>
    internal static readonly MethodInfo PartitionIndexMethod = typeof(MySqlIndexedSelectLowering).GetMethod(
        nameof(PartitionIndex),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Identifies closed structural-key initializers without dynamic array construction.</summary>
    internal static readonly MethodInfo KeyListAddMethod = typeof(List<object>).GetMethod(nameof(List<object>.Add))!;

    private bool _containsWindows;

    /// <summary>Rewrites only exact indexed Select overloads and fences explicit completed window projections.</summary>
    internal static Expression Lower(
        Expression query,
        out bool containsWindows
    )
    {
        var lowering = new MySqlIndexedSelectLowering();
        var result = lowering.Visit(query);
        containsWindows = lowering._containsWindows;

        return result;
    }

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(
        Expression? node
    )
    {
        _containsWindows |= node?.Type == typeof(MySqlWindowDefinition);

        return base.Visit(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (node.Method.IsGenericMethod
            && node.Method.GetGenericMethodDefinition() == CompleteMethod)
        {
            _containsWindows = true;

            return node;
        }

        _containsWindows |= node.Method.DeclaringType == typeof(MySqlWindowDefinition)
            || node.Method.ReturnType == typeof(MySqlWindowDefinition);

        if (TryLowerGroupedSelect(node) is { } grouped)
        {
            return grouped;
        }

        var visited = (MethodCallExpression)base.VisitMethodCall(node);

        if (IsIndexedSelect(visited, typeof(Queryable)))
        {
            _containsWindows = true;
            var selector = UnwrapLambda(visited.Arguments[1]);
            var body = ReplacingExpressionVisitor.Replace(selector.Parameters[1],
                Expression.Call(IndexMethod),
                selector.Body);

            var selectMethod = CloseMethod(QueryableMethods.Select, visited.Method.GetGenericArguments());
            var unarySelector = CreateSelector(selectMethod, body, selector.Parameters[0]);
            var selection = Expression.Call(
                selectMethod,
                visited.Arguments[0],
                Expression.Quote(unarySelector));

            return CompleteProjection(selection, body.Type);
        }

        if (IsIndexedSelect(visited, typeof(Enumerable)))
        {
            throw new InvalidOperationException(
                "An indexed grouped selector requires a directly mapped ordered grouping scope.");
        }

        if (_containsWindows
            && visited.Method.IsGenericMethod
            && visited.Method.GetGenericMethodDefinition() == QueryableMethods.Select
            && ContainsWindowCall(UnwrapLambda(visited.Arguments[1])
                .Body))
        {
            return CompleteProjection(visited,
                visited
                    .Method
                    .GetGenericArguments()[1]);
        }

        return visited;
    }

    /// <summary>Finds expression-only window calls in this selector without attributing nested query projections to its scope.</summary>
    internal static bool ContainsWindowCall(
        Expression expression
    )
    {
        var finding = new WindowCallFinding();
        finding.Visit(expression);

        return finding.Found;
    }

    /// <summary>Recognizes the actual two-parameter LINQ selector rather than a same-named user method.</summary>
    private static bool IsIndexedSelect(
        MethodCallExpression call,
        Type declaringType
    ) => call.Method.DeclaringType == declaringType
        && call.Method is { IsGenericMethod: true, Name: nameof(Queryable.Select) }
        && call.Arguments.Count == 2
        && UnwrapLambda(call.Arguments[1])
            .Parameters.Count
        == 2;

    private static LambdaExpression UnwrapLambda(
        Expression expression
    ) => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote
        ? (LambdaExpression)quote.Operand
        : (LambdaExpression)expression;

    private static MethodCallExpression CompleteProjection(
        Expression selection,
        Type elementType
    ) => Expression.Call(CloseMethod(CompleteMethod, [elementType]), selection);

    /// <summary>Reuses the exact closed LINQ delegate type instead of synthesizing a new delegate signature.</summary>
    private static LambdaExpression CreateSelector(
        MethodInfo method,
        Expression body,
        ParameterExpression parameter
    )
    {
        var formalType = method
            .GetParameters()[1].ParameterType;

        var delegateType = formalType.IsGenericType && formalType.GetGenericTypeDefinition() == typeof(Expression<>)
            ? formalType
                .GetGenericArguments()[0]
            : formalType;

        return Expression.Lambda(delegateType, body, parameter);
    }

    /// <summary>Builds a closed structural key list that the index translator consumes without evaluating it.</summary>
    private static ListInitExpression CreateKeys(
        IEnumerable<Expression> keys
    ) => Expression.ListInit(
        Expression.New(typeof(List<object>)),
        keys.Select(key => Expression.ElementInit(KeyListAddMethod, Box(key))));

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification =
            "Only the known unconstrained Queryable.Select, AsQueryable and private unary completion marker definitions are closed here.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification =
            "This is the existing runtime EF expression-compilation path, not an additional NativeAOT execution guarantee.")]
    private static MethodInfo CloseMethod(
        MethodInfo method,
        Type[] arguments
    )
    {
        if (method != QueryableMethods.Select
            && method != QueryableMethods.AsQueryable
            && method != CompleteMethod)
        {
            throw new InvalidOperationException("Only exact window-lowering methods can be closed here.");
        }

        return method.MakeGenericMethod(arguments);
    }

    // WHY: EF's unknown-queryable hook reduces its pending projection only for a static
    // unary IQueryable<T> -> IQueryable<T> method. The marker makes the SQL scope explicit
    // before later Where/GroupBy can substitute a window back into an illegal predicate.
    private static IQueryable<T> Complete<T>(

        // ReSharper disable once UnusedParameter.Local
        IQueryable<T> source
    ) => throw new InvalidOperationException(
        "The window projection boundary can be consumed only by Doka query translation.");

    private static int Index()
        => throw new InvalidOperationException("An indexed selector can be consumed only by Doka query translation.");

    // WHY: Translation reads every marker argument from its expression tree; the CLR body must never run.
    private static int PartitionIndex(

        // ReSharper disable once UnusedParameter.Local
        IReadOnlyList<object?> partitions,

        // ReSharper disable once UnusedParameter.Local
        IReadOnlyList<object?> orderings,

        // ReSharper disable once UnusedParameter.Local
        bool[] descending,

        // ReSharper disable once UnusedParameter.Local
        bool included,

        // ReSharper disable once UnusedParameter.Local
        long skipped
    ) => throw new InvalidOperationException("A partitioned index can be consumed only by Doka query translation.");

    private sealed class WindowCallFinding : ExpressionVisitor
    {
        internal bool Found { get; private set; }

        public override Expression? Visit(
            Expression? node
        )
        {
            if (node is LambdaExpression)
            {
                return node;
            }

            if (node?.Type == typeof(MySqlWindowDefinition))
            {
                throw new InvalidOperationException(
                    "A window descriptor cannot escape its expression-only terminal calculation.");
            }

            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (node.Method.DeclaringType == typeof(MySqlWindowDefinition))
            {
                Found = true;

                return node;
            }

            return base.VisitMethodCall(node);
        }
    }
}
