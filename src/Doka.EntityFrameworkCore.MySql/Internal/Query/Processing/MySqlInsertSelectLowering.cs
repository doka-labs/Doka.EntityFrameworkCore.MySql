namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Interprets only the known setter syntax when EF compiles the asynchronous terminal as a Task result.</summary>
internal static class MySqlInsertSelectLowering
{
    private static readonly MethodInfo s_valueMarker = typeof(MySqlInsertProjectionMarker).GetMethod(
        nameof(MySqlInsertProjectionMarker.Value),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Leaves ordinary queries alone and lowers a compiled INSERT terminal without invoking its callback.</summary>
    internal static Expression Lower(
        Expression query,
        EntityQueryRootExpression target,
        IModel model,
        IEvaluatableExpressionFilter evaluatableFilter
    )
    {
        if (query is not MethodCallExpression call
            || call.Method.DeclaringType != typeof(MySqlQueryableExtensions)
            || call.Method.Name != nameof(MySqlQueryableExtensions.ExecuteInsertFromQueryAsync))
        {
            return query;
        }

        if (!ReferenceEquals(model.FindEntityType(target.EntityType.Name), target.EntityType))
        {
            throw new InvalidOperationException(
                "A compiled INSERT SELECT requires a target set from the current EF context and model.");
        }

        MySqlInsertQuerySourceGuard.Validate(call.Arguments[0], model, evaluatableFilter);
        var callback = UnwrapLambda(call.Arguments[2]);
        var setters = new List<MethodCallExpression>();
        var current = callback.Body;

        while (current is MethodCallExpression { Method.DeclaringType: { IsGenericType: true } owner } setter
            && owner.GetGenericTypeDefinition() == typeof(MySqlInsertSettersBuilder<,>)
            && setter.Method.Name == nameof(MySqlInsertSettersBuilder<object, object>.SetProperty))
        {
            setters.Add(setter);
            current = setter.Object!;
        }

        if (!ReferenceEquals(current, callback.Parameters[0])
            || setters.Count == 0)
        {
            throw new InvalidOperationException(
                "A compiled INSERT SELECT requires an expression-bodied fluent SetProperty chain, without client callbacks.");
        }

        setters.Reverse();
        var sourceType = call
            .Method
            .GetGenericArguments()[0];

        var source = Expression.Parameter(sourceType, "sourceRow");
        var values = new Expression[setters.Count];
        var properties = new IProperty[setters.Count];
        var assigned = new HashSet<IProperty>();

        // WHY: Compiled query lambdas do not execute the public generic factory. Parse its
        // exact expression syntax instead of compiling/invoking a user-supplied Action.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < setters.Count; index++)
        {
            var setter = setters[index];
            var targetSelector = UnwrapLambda(setter.Arguments[0]);
            var valueSelector = UnwrapLambda(setter.Arguments[1]);
            var property = MySqlInsertPropertySelector.Resolve(target.EntityType, targetSelector);
            var selectedType = Nullable.GetUnderlyingType(targetSelector.ReturnType) ?? targetSelector.ReturnType;
            var mappedType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

            if (selectedType != mappedType
                || valueSelector.Parameters[0].Type != sourceType)
            {
                throw new InvalidOperationException(
                    "A compiled INSERT SELECT assignment must preserve the mapped property and source row types.");
            }

            if (!assigned.Add(property))
            {
                throw new InvalidOperationException(
                    $"INSERT SELECT assigns property '{property.Name}' more than once.");
            }

            MySqlInsertValueValidator.Validate(valueSelector, model, evaluatableFilter);
            properties[index] = property;
            var value = ReplacingExpressionVisitor.Replace(valueSelector.Parameters[0], source, valueSelector.Body);
            values[index] = Expression.Convert(Expression.Call(CloseValueMarker(valueSelector.ReturnType), value),
                typeof(object));
        }

        var selectMethod = CloseSelect(sourceType);

        // WHY: Select already closes Expression<Func<TSource, object[]>>. Reuse that
        // exact delegate type rather than asking expression trees to synthesize one.
        var projectionType = selectMethod
            .GetParameters()[1]
            .ParameterType
            .GenericTypeArguments[0];

        var projection =
            Expression.Lambda(projectionType, MySqlInsertProjectionMarker.CreateValueArray(values), source);

        var projectedSource = Expression.Call(selectMethod, call.Arguments[0], Expression.Quote(projection));
        var tokenParameter = GetTokenParameter(call.Arguments[3]);

        return new MySqlInsertSelectQueryExpression(projectedSource,
            target.EntityType,
            properties,
            tokenParameter,
            target);
    }

    /// <summary>Unwraps only an actual expression lambda, never a compiled delegate or arbitrary constant.</summary>
    private static LambdaExpression UnwrapLambda(
        Expression expression
    )
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
        {
            expression = quote.Operand;
        }

        return expression as LambdaExpression
            ?? throw new InvalidOperationException(
                "Compiled INSERT SELECT assignments must remain expression lambdas.");
    }

    /// <summary>Retains only a parameter name, so a compiled query cannot retain a caller's token source.</summary>
    private static string? GetTokenParameter(
        Expression expression
    ) => expression switch
    {
        QueryParameterExpression parameter when parameter.Type == typeof(CancellationToken) => parameter.Name,
        ConstantExpression { Value: CancellationToken { CanBeCanceled: false } } => null,
        DefaultExpression when expression.Type == typeof(CancellationToken) => null,
        MemberExpression { Member: { DeclaringType: { } type, Name: nameof(CancellationToken.None) } }
            when type == typeof(CancellationToken) => null,
        _ => throw new InvalidOperationException(
            "A compiled INSERT SELECT requires an explicit CancellationToken parameter or CancellationToken.None."),
    };

    /// <summary>Closes the one statically preserved unconstrained scalar marker during runtime query compilation.</summary>
    [UnconditionalSuppressMessage("Trimming",
        "IL2060",
        Justification =
            "Only the known unconstrained Value<T> marker is closed; it has no generic member-preservation requirements.")]
    [UnconditionalSuppressMessage("AOT",
        "IL3050",
        Justification =
            "This path compiles runtime EF queries. Pregenerated INSERT execution is explicitly rejected; no new NativeAOT execution guarantee is made.")]
    private static MethodInfo CloseValueMarker(
        Type valueType
    ) => s_valueMarker.MakeGenericMethod(valueType);

    /// <summary>Closes only EF's known unary Select operator with the statically rooted object[] projection.</summary>
    [UnconditionalSuppressMessage("Trimming",
        "IL2060",
        Justification =
            "Only the known unconstrained Queryable.Select<TSource,object[]> operator is closed; neither type parameter requires member preservation.")]
    [UnconditionalSuppressMessage("AOT",
        "IL3050",
        Justification =
            "This is runtime query compilation, separate from the explicitly guarded pregenerated INSERT contract.")]
    private static MethodInfo CloseSelect(
        Type sourceType
    ) => QueryableMethods.Select.MakeGenericMethod(sourceType, typeof(object[]));
}
