namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Extracts completed sources once and substitutes lexical leaf references before navigation expansion.
/// </summary>
internal sealed class MySqlMaterializedSourceBinding(
    IModel model,
    IDictionary<int, Expression> definitions
) : ExpressionVisitor
{
    private int _nextBindingId;

    /// <inheritdoc />
    protected override Expression VisitMethodCall(
        MethodCallExpression node
    )
    {
        if (!node.Method.IsGenericMethod
            || node.Method.GetGenericMethodDefinition() != MySqlQueryableExtensions.WithMaterializedSourceMethod)
        {
            return base.VisitMethodCall(node);
        }

        var source = Visit(node.Arguments[0]);
        var compose = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
        var bindingId = _nextBindingId++;
        var sourceType = node.Method.GetGenericArguments()[0];
        var origin = ResolveOrigin(source, new Stack<string>());

        if (origin is not null
            && origin.EntityType.ClrType != sourceType)
        {
            origin = RefineEntityType(origin, sourceType);
        }

        Expression reference = origin is not null
            ? new MySqlMaterializedEntityQueryRootExpression(origin, bindingId)
            : new MySqlMaterializedQueryRootExpression(sourceType, bindingId);

        // WHY: References must not embed their source. Re-expanding the same input at every use
        // makes a short nested query exponential before relational translation even begins.
        definitions.Add(bindingId, source);
        var body = ReplacingExpressionVisitor.Replace(compose.Parameters[0], reference, compose.Body);

        return Visit(body);
    }

    /// <summary>Follows the selected source rather than guessing a mapping from its CLR type.</summary>
    private EntityQueryRootExpression? ResolveOrigin(
        Expression source,
        Stack<string> members
    )
    {
        if (source is MySqlMaterializedEntityQueryRootExpression bound)
        {
            return FollowNavigations(bound.SourceOrigin, members);
        }

        if (source is EntityQueryRootExpression root)
        {
            return ReferenceEquals(model.FindEntityType(root.EntityType.Name), root.EntityType)
                ? FollowNavigations(WithEntityType(root, root.EntityType), members)
                : null;
        }

        if (source is not MethodCallExpression call
            || !call.Method.IsGenericMethod)
        {
            return null;
        }

        if (call.Method.DeclaringType == typeof(Queryable))
        {
            switch (call.Method.Name)
            {
                case nameof(Queryable.Select):
                case nameof(Queryable.Join):
                case nameof(Queryable.GroupJoin):
                    var selector = UnwrapLambda(call.Arguments[^1]);

                    return ResolveProjection(selector.Body, members, selector.Parameters, call.Arguments);

                case nameof(Queryable.SelectMany):
                    var collection = UnwrapLambda(call.Arguments[1]);
                    var inner = ResolveProjection(
                        collection.Body,
                        new Stack<string>(),
                        collection.Parameters,
                        [call.Arguments[0]]);

                    if (call.Arguments.Count == 2)
                    {
                        return inner is null ? null : FollowNavigations(inner, members);
                    }

                    var result = UnwrapLambda(call.Arguments[2]);

                    return ResolveProjection(
                        result.Body,
                        members,
                        result.Parameters,
                        [call.Arguments[0], (Expression?)inner ?? Expression.Empty()]);

                case nameof(Queryable.Cast):
                case nameof(Queryable.OfType):
                    var parent = ResolveOrigin(call.Arguments[0], new Stack<string>());
                    var refined = parent is null
                        ? null
                        : RefineEntityType(parent, call.Method.GetGenericArguments()[0]);

                    return refined is null ? null : FollowNavigations(refined, members);

                case nameof(Queryable.Concat):
                case nameof(Queryable.Union):
                case nameof(Queryable.Intersect):
                case nameof(Queryable.Except):
                    var first = ResolveOrigin(call.Arguments[0], members);
                    var second = ResolveOrigin(call.Arguments[1], members);

                    // WHY: A source with conflicting origins cannot lend one branch's temporal or named
                    // identity to the other. EF validates the original set operation independently.
                    return first is not null && first.Equals(second) ? first : null;
            }
        }

        return IsIdentityOperator(call) ? ResolveOrigin(call.Arguments[0], members) : null;
    }

    /// <summary>Resolves retained entities through selectors and transparent projection members.</summary>
    private EntityQueryRootExpression? ResolveProjection(
        Expression expression,
        Stack<string> members,
        IReadOnlyList<ParameterExpression> parameters,
        IReadOnlyList<Expression> inputs
    )
    {
        while (true)
        {
            switch (expression)
            {
                case ParameterExpression parameter:
                    for (var index = 0; index < parameters.Count; index++)
                    {
                        if (ReferenceEquals(parameter, parameters[index]))
                        {
                            return ResolveOrigin(inputs[index], members);
                        }
                    }

                    return null;

                case MemberExpression { Expression: { } owner } member:
                    members.Push(member.Member.Name);

                    try
                    {
                        return ResolveProjection(owner, members, parameters, inputs);
                    }
                    finally
                    {
                        members.Pop();
                    }

                case NewExpression { Members: { } projected } creation when members.TryPeek(out var projectedName):
                    for (var index = 0; index < projected.Count; index++)
                    {
                        if (projected[index].Name == projectedName)
                        {
                            return ResolveSelectedMember(creation.Arguments[index], members, parameters, inputs);
                        }
                    }

                    return null;

                case MemberInitExpression initialization when members.TryPeek(out var initializedName):
                    foreach (var binding in initialization.Bindings)
                    {
                        if (binding is MemberAssignment assignment
                            && binding.Member.Name == initializedName)
                        {
                            return ResolveSelectedMember(assignment.Expression, members, parameters, inputs);
                        }
                    }

                    return null;

                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } conversion:
                    expression = conversion.Operand;
                    continue;

                case MethodCallExpression call when call.Method.DeclaringType == typeof(EF)
                    && call.Method.Name == nameof(EF.Property)
                    && call.Arguments[1] is ConstantExpression { Value: string navigation }:
                    members.Push(navigation);

                    try
                    {
                        return ResolveProjection(call.Arguments[0], members, parameters, inputs);
                    }
                    finally
                    {
                        members.Pop();
                    }

                case MethodCallExpression call when IsIdentityOperator(call):
                    expression = call.Arguments[0];
                    continue;

                default:
                    return ResolveOrigin(expression, members);
            }
        }
    }

    /// <summary>Consumes one transparent member without allocating a replacement expression tree.</summary>
    private EntityQueryRootExpression? ResolveSelectedMember(
        Expression selected,
        Stack<string> members,
        IReadOnlyList<ParameterExpression> parameters,
        IReadOnlyList<Expression> inputs
    )
    {
        var member = members.Pop();

        try
        {
            return ResolveProjection(selected, members, parameters, inputs);
        }
        finally
        {
            members.Push(member);
        }
    }

    /// <summary>Traverses only navigations configured on the exact selected mapping.</summary>
    private static EntityQueryRootExpression? FollowNavigations(
        EntityQueryRootExpression origin,
        Stack<string> members
    )
    {
        foreach (var member in members)
        {
            var target = origin.EntityType.FindNavigation(member)?.TargetEntityType
                ?? origin.EntityType.FindSkipNavigation(member)?.TargetEntityType;

            if (target is null)
            {
                return null;
            }

            origin = WithEntityType(origin, target);
        }

        return origin;
    }

    /// <summary>Restricts type refinements to the selected mapping's existing inheritance hierarchy.</summary>
    private static EntityQueryRootExpression? RefineEntityType(
        EntityQueryRootExpression origin,
        Type type
    )
    {
        var entityType = origin
            .EntityType
            .GetRootType()
            .GetDerivedTypesInclusive()
            .FirstOrDefault(candidate => candidate.ClrType == type);

        return entityType is null ? null : WithEntityType(origin, entityType);
    }

    /// <summary>Keeps only root semantics needed by navigation expansion, without retaining a provider or source DAG.</summary>
    private static EntityQueryRootExpression WithEntityType(
        EntityQueryRootExpression origin,
        IEntityType entityType
    ) => origin switch
    {
        MySqlTemporalQueryRootExpression temporal => new MySqlTemporalQueryRootExpression(
            entityType,
            temporal.Operation,
            temporal.PointInTime,
            temporal.From,
            temporal.To),
        MySqlApplicationTimeQueryRootExpression application => new MySqlApplicationTimeQueryRootExpression(
            entityType,
            application.From,
            application.To),
        _ => new EntityQueryRootExpression(entityType),
    };

    /// <summary>Recognizes operators that retain their input entity identity.</summary>
    private static bool IsIdentityOperator(
        MethodCallExpression call
    ) =>
        ((call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
            && call.Method.Name is nameof(Queryable.Where)
                or nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.ThenByDescending)
                or nameof(Queryable.Skip)
                or nameof(Queryable.Take)
                or nameof(Queryable.Distinct)
                or nameof(Queryable.Reverse)
                or nameof(Queryable.DefaultIfEmpty)
                or nameof(Queryable.AsQueryable))
        || ((call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                || call.Method.DeclaringType == typeof(RelationalQueryableExtensions))
            && call.Method.Name is nameof(EntityFrameworkQueryableExtensions.AsNoTracking)
                or nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution)
                or nameof(EntityFrameworkQueryableExtensions.AsTracking)
                or nameof(EntityFrameworkQueryableExtensions.Include)
                or nameof(EntityFrameworkQueryableExtensions.ThenInclude)
                or nameof(EntityFrameworkQueryableExtensions.TagWith)
                or nameof(EntityFrameworkQueryableExtensions.TagWithCallSite)
                or nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                or nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes)
                or nameof(RelationalQueryableExtensions.AsSplitQuery)
                or nameof(RelationalQueryableExtensions.AsSingleQuery));

    /// <summary>Reads a selector without executing or compiling user code.</summary>
    private static LambdaExpression UnwrapLambda(
        Expression expression
    ) => (LambdaExpression)(expression is UnaryExpression { NodeType: ExpressionType.Quote } quote
        ? quote.Operand
        : expression);
}
