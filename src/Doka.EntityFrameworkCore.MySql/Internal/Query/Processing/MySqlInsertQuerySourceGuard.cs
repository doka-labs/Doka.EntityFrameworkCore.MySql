namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Rejects write-incompatible source requests before projection can erase an Include or marker.</summary>
internal sealed class MySqlInsertQuerySourceGuard : ExpressionVisitor
{
    private readonly IEvaluatableExpressionFilter _filter;
    private readonly IModel _model;

    private readonly HashSet<ParameterExpression> _rowParameters = [];

    /// <summary>Binds source validation to the current model and evaluatable-expression policy.</summary>
    public MySqlInsertQuerySourceGuard(
        IModel model,
        IEvaluatableExpressionFilter filter
    )
    {
        _model = model;
        _filter = filter;
    }

    /// <summary>Retains the read-only binding and loading boundaries even when a later SELECT discards them.</summary>
    internal static void Validate(
        Expression source,
        IModel model,
        IEvaluatableExpressionFilter filter
    ) => new MySqlInsertQuerySourceGuard(model, filter).Visit(source);

    /// <inheritdoc />
    protected override Expression VisitLambda<T>(
        Expression<T> node
    )
    {
        List<ParameterExpression>? added = null;

        foreach (var parameter in node.Parameters)
        {
            if (!typeof(IQueryable).IsAssignableFrom(parameter.Type)
                && _rowParameters.Add(parameter))
            {
                (added ??= []).Add(parameter);
            }
        }

        try
        {
            if (added is not null)
            {
                // WHY: Validate before EF can evaluate a closed fragment during either
                // parameter extraction or SQL projection translation. Keep outer rows
                // visible to correlated nested lambdas; IQueryable scope binders remain
                // ordinary provider composition, not application row materializers.
                MySqlInsertSourceValueValidator.Validate(node.Body, _model, _filter, _rowParameters);
            }

            return base.VisitLambda(node);
        }
        finally
        {
            if (added is not null)
            {
                foreach (var parameter in added)
                {
                    _rowParameters.Remove(parameter);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (node.Method.DeclaringType == typeof(MySqlQueryableExtensions))
        {
            if (node.Method.Name == nameof(MySqlQueryableExtensions.WithMaterializedSource))
            {
                throw new InvalidOperationException("INSERT SELECT cannot consume a read-only materialized source.");
            }

            if (node.Method.Name is nameof(MySqlQueryableExtensions.ForUpdate)
                or nameof(MySqlQueryableExtensions.ForShare)
                or nameof(MySqlQueryableExtensions.ForUpdateNoWait)
                or nameof(MySqlQueryableExtensions.ForUpdateSkipLocked))
            {
                throw new InvalidOperationException("INSERT SELECT cannot consume a locking-read source.");
            }
        }

        if (node.Method.DeclaringType == typeof(RelationalQueryableExtensions)
            && node.Method.Name == nameof(RelationalQueryableExtensions.AsSplitQuery))
        {
            throw new InvalidOperationException("INSERT SELECT cannot consume a split-query source.");
        }

        if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
            && node.Method.Name is nameof(EntityFrameworkQueryableExtensions.Include)
                or nameof(EntityFrameworkQueryableExtensions.ThenInclude))
        {
            var include = node.Arguments[1];

            while (include is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            {
                include = quote.Operand;
            }

            if (include is LambdaExpression lambda
                && IsCollection(lambda.ReturnType))
            {
                throw new InvalidOperationException("INSERT SELECT cannot consume a collection Include source.");
            }

            if (include is ConstantExpression { Value: string path }
                && IncludesCollection(node
                        .Method
                        .GetGenericArguments()[0],
                    path))
            {
                throw new InvalidOperationException("INSERT SELECT cannot consume a collection Include source.");
            }
        }

        return base.VisitMethodCall(node);
    }

    /// <summary>Checks string Include metadata without interpreting a name as SQL.</summary>
    private bool IncludesCollection(
        Type sourceType,
        string path
    )
    {
        foreach (var candidate in _model
            .GetEntityTypes()
            .Where(entity => entity.ClrType == sourceType))
        {
            var entity = candidate;

            foreach (var member in path.Split('.'))
            {
                if (entity.FindSkipNavigation(member) is not null)
                {
                    return true;
                }

                if (entity.FindNavigation(member) is not { } navigation)
                {
                    break;
                }

                if (navigation.IsCollection)
                {
                    return true;
                }

                entity = navigation.TargetEntityType;
            }
        }

        return false;
    }

    /// <summary>Identifies collection-navigation Include signatures before EF removes an unused Include.</summary>
    private static bool IsCollection(
        Type type
    ) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
