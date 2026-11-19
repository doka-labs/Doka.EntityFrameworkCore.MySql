namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Rejects application callbacks that EF could execute while extracting source-row parameters.</summary>
internal sealed class MySqlInsertSourceValueValidator : ExpressionVisitor
{
    private readonly IEvaluatableExpressionFilter _filter;
    private readonly IModel _model;

    private readonly RowFindingVisitor _rowFinder;

    /// <summary>Binds model validation to the current lexical row scope.</summary>
    public MySqlInsertSourceValueValidator(
        IModel model,
        IEvaluatableExpressionFilter filter,
        IReadOnlySet<ParameterExpression> rowParameters
    )
    {
        _model = model;
        _filter = filter;
        _rowFinder = new RowFindingVisitor(rowParameters);
    }

    /// <summary>Validates one lexical row scope without executing expressions or inspecting source rows.</summary>
    internal static void Validate(
        Expression body,
        IModel model,
        IEvaluatableExpressionFilter filter,
        IReadOnlySet<ParameterExpression> rowParameters
    ) => new MySqlInsertSourceValueValidator(model, filter, rowParameters).Visit(body);

    /// <inheritdoc />
    protected override Expression VisitLambda<T>(
        Expression<T> node
    ) => node; // WHY: The source guard visits nested row lambdas separately with their complete lexical row scope.

    /// <inheritdoc />
    protected override Expression VisitInvocation(
        InvocationExpression node
    )
    {
        if (node.Expression is not LambdaExpression)
        {
            throw new InvalidOperationException("INSERT SELECT source lambdas cannot invoke client delegates.");
        }

        // WHY: Literal expression lambdas are ordinary EF inlining syntax, not
        // application delegates. Their row bodies receive separate lexical validation.
        return base.VisitInvocation(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (!ReferencesRow(node)
            && _filter.IsEvaluatableExpression(node, _model)
            && !IsQueryComposition(node.Method)
            && !MySqlInsertValueValidator.IsPureBuiltInCall(node.Method))
        {
            throw new InvalidOperationException(
                "INSERT SELECT source lambdas cannot evaluate a client callback; capture its scalar result before composing the source.");
        }

        return base.VisitMethodCall(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMember(
        MemberExpression node
    )
    {
        if (node.Member is PropertyInfo
            && !ReferencesRow(node)
            && _filter.IsEvaluatableExpression(node, _model)
            && !MySqlInsertValueValidator.IsBuiltInValueType(node.Member.DeclaringType)
            && !(node.Member.DeclaringType == typeof(EF) && node.Member.Name == nameof(EF.Functions))
            && !IsContextSet(node)
            && !IsListCount(node))
        {
            throw new InvalidOperationException(
                "INSERT SELECT source lambdas cannot invoke a client property getter; capture its scalar value before composing the source.");
        }

        return base.VisitMember(node);
    }

    /// <inheritdoc />
    protected override Expression VisitNew(
        NewExpression node
    )
    {
        if (!ReferencesRow(node)
            && !(node.Members is not null && node.Type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            && (!MySqlInsertValueValidator.IsBuiltInValueType(node.Type)
                || (node.Constructor is { } constructor
                    && !constructor
                        .GetParameters()
                        .All(parameter => MySqlInsertValueValidator.IsBuiltInValueType(parameter.ParameterType)))))
        {
            throw new InvalidOperationException(
                "INSERT SELECT source lambdas cannot construct client objects; use a SQL-translatable row projection.");
        }

        return base.VisitNew(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMemberInit(
        MemberInitExpression node
    )
    {
        if (!ReferencesRow(node))
        {
            return base.VisitMemberInit(node);
        }

        // WHY: EF can inline a row-dependent DTO initializer without invoking its
        // parameterless constructor. Validate its arguments and assignments, while
        // retaining the row dependence that makes client constant evaluation impossible.
        // WHY: Expression arguments are a stable indexable collection; preserve their
        // existing order without introducing an enumerator or materializing a copy.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < node.NewExpression.Arguments.Count; index++)
        {
            Visit(node.NewExpression.Arguments[index]);
        }

        // WHY: Member bindings retain their original order in the immutable expression tree.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < node.Bindings.Count; index++)
        {
            VisitMemberBinding(node.Bindings[index]);
        }

        return node;
    }

    /// <summary>Keeps EF's declarative query operators and scalar extraction markers intact.</summary>
    private static bool IsQueryComposition(
        MethodInfo method
    ) => method.DeclaringType == typeof(Queryable)
        || (method.DeclaringType == typeof(DbContext) && method.Name == nameof(DbContext.Set))
        || (method.DeclaringType == typeof(EF)
            && method.Name is nameof(EF.Constant) or nameof(EF.Parameter))
        || (method.DeclaringType == typeof(EFExtensions) && method.Name == nameof(EFExtensions.MultipleParameters))
        || (typeof(IQueryable).IsAssignableFrom(method.ReturnType)
            && (method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                || method.DeclaringType == typeof(RelationalQueryableExtensions)));

    /// <summary>Retains normal mapped DbSet root lookup, which EF validates against actual provider ownership.</summary>
    private static bool IsContextSet(
        MemberExpression node
    ) => node.Expression is not null
        && typeof(DbContext).IsAssignableFrom(node.Expression.Type)
        && node.Type.IsGenericType
        && node.Type.GetGenericTypeDefinition() == typeof(DbSet<>);

    /// <summary>Allows the nonvirtual framework List.Count getter without trusting arbitrary collection implementations.</summary>
    private static bool IsListCount(
        MemberExpression node
    ) => node.Member.DeclaringType is { IsGenericType: true } type
        && type.GetGenericTypeDefinition() == typeof(List<>)
        && node.Member.Name == nameof(List<int>.Count);

    /// <summary>Recognizes dependence on the currently active source rows without evaluating a lambda.</summary>
    private bool ReferencesRow(
        Expression node
    )
    {
        _rowFinder.Found = false;
        _rowFinder.Visit(node);

        return _rowFinder.Found;
    }

    private sealed class RowFindingVisitor : ExpressionVisitor
    {
        private readonly IReadOnlySet<ParameterExpression> _rows;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public RowFindingVisitor(
            IReadOnlySet<ParameterExpression> rows
        )
        {
            _rows = rows;
        }

        internal bool Found { get; set; }

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
            Found |= _rows.Contains(node);

            return node;
        }
    }
}
