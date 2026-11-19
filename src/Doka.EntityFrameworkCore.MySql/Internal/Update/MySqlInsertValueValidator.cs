namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Prevents EF parameter extraction from executing user callbacks inside a recorded assignment.</summary>
internal sealed class MySqlInsertValueValidator : ExpressionVisitor
{
    private readonly IEvaluatableExpressionFilter _evaluatableFilter;
    private readonly IModel _model;

    private readonly SourceFindingVisitor _sourceFinder;

    /// <summary>Binds assignment validation to its model, filter and source parameter.</summary>
    public MySqlInsertValueValidator(
        IModel model,
        IEvaluatableExpressionFilter evaluatableFilter,
        ParameterExpression source
    )
    {
        _model = model;
        _evaluatableFilter = evaluatableFilter;
        _sourceFinder = new SourceFindingVisitor(source);
    }

    /// <summary>Allows captured scalar fields and translatable expressions, without invoking arbitrary callbacks.</summary>
    internal static void Validate(
        LambdaExpression value,
        IModel model,
        IEvaluatableExpressionFilter evaluatableFilter
    ) => new MySqlInsertValueValidator(model, evaluatableFilter, value.Parameters[0]).Visit(value.Body);

    /// <inheritdoc />
    protected override Expression VisitInvocation(
        InvocationExpression node
    ) => throw new InvalidOperationException("INSERT SELECT assignments cannot invoke client delegates.");

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        // WHY: SQL translation happens after parameter extraction. A row-independent custom
        // callback could otherwise execute before SQL gets a chance to reject the assignment.
        // Respect provider/plugin exclusions so registered SQL-only functions remain composable.
        if (!ReferencesSource(node)
            && _evaluatableFilter.IsEvaluatableExpression(node, _model)
            && !IsPureBuiltInCall(node.Method))
        {
            throw new InvalidOperationException(
                "INSERT SELECT assignments cannot evaluate a client callback; capture its scalar result before composing the operation.");
        }

        return base.VisitMethodCall(node);
    }

    /// <inheritdoc />
    protected override Expression VisitNew(
        NewExpression node
    )
    {
        if (!IsBuiltInValueType(node.Type)
            || (node.Constructor is { } constructor
                && !constructor
                    .GetParameters()
                    .All(parameter => IsBuiltInValueType(parameter.ParameterType))))
        {
            throw new InvalidOperationException("INSERT SELECT assignments cannot construct client objects.");
        }

        return base.VisitNew(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMember(
        MemberExpression node
    )
    {
        if (node.Member is PropertyInfo
            && !ReferencesSource(node)
            && _evaluatableFilter.IsEvaluatableExpression(node, _model)
            && !IsBuiltInValueType(node.Member.DeclaringType)
            && !(node.Member.DeclaringType == typeof(EF) && node.Member.Name == nameof(EF.Functions)))
        {
            throw new InvalidOperationException(
                "INSERT SELECT assignments cannot invoke a client property getter; capture its scalar value first.");
        }

        return base.VisitMember(node);
    }

    /// <summary>Recognizes only side-effect-free built-in scalar construction and operations.</summary>
    internal static bool IsBuiltInValueType(
        Type? type
    ) => type is not null
        && ((Nullable.GetUnderlyingType(type) is { } underlying && IsBuiltInValueType(underlying))
            || (type.IsArray && IsBuiltInValueType(type.GetElementType()))
            || type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(Math)
            || type == typeof(MathF)
            || type == typeof(Convert));

    /// <summary>Recognizes pure scalar calls and empty-input factories without admitting application callbacks.</summary>
    internal static bool IsPureBuiltInCall(
        MethodInfo method
    ) => (IsBuiltInValueType(method.DeclaringType)
            && method
                .GetParameters()
                .All(parameter => IsBuiltInValueType(parameter.ParameterType)))

        // WHY: These two parameterless framework factories create no elements and
        // invoke no application code; they remain valid empty SQL collection inputs.
        || ((method.DeclaringType == typeof(Array) || method.DeclaringType == typeof(Enumerable))
            && method is { Name: nameof(Array.Empty), IsGenericMethod: true }
            && method.GetParameters()
                .Length
            == 0)
        || IsArrayToReadOnlySpan(method);

    /// <summary>Recognizes only the array view conversion selected by C#14 collection Contains syntax.</summary>
    private static bool IsArrayToReadOnlySpan(
        MethodInfo method
    ) => method is { IsStatic: true, Name: "op_Implicit", DeclaringType: { IsGenericType: true } type }
        && type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)
        && method.ReturnType == type

        // WHY: This exact framework intrinsic wraps an array without enumerating it
        // or invoking application code. Other span conversions and methods are not admitted.
        && method.GetParameters() is [{ ParameterType: { IsSZArray: true } array }]
        && array.GetElementType() == type.GenericTypeArguments[0];

    /// <summary>Determines dependence without interpreting or executing any expression.</summary>
    private bool ReferencesSource(
        Expression expression
    )
    {
        _sourceFinder.Reset();
        _sourceFinder.Visit(expression);

        return _sourceFinder.Found;
    }

    private sealed class SourceFindingVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _source;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public SourceFindingVisitor(
            ParameterExpression source
        )
        {
            _source = source;
        }

        internal bool Found { get; private set; }

        internal void Reset() => Found = false;

        /// <inheritdoc />
        public override Expression? Visit(
            Expression? node
        ) => Found
            ? node
            : base.Visit(node);

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            Found |= ReferenceEquals(node, _source);

            return node;
        }
    }
}
