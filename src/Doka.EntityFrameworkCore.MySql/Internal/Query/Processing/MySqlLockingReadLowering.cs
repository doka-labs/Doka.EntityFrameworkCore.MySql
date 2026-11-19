namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Removes exact locking operators only from the executed query's source chain.</summary>
internal static class MySqlLockingReadLowering
{
    /// <summary>Retains one typed mode while rejecting scopes in which a locking marker could disappear.</summary>
    public static Expression Process(
        Expression query,
        MySqlQueryCompilationContext compilation
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(compilation);

        var markers = new MarkerInventory();
        markers.Visit(query);

        if (markers.Mode == MySqlLockingReadMode.None)
        {
            return query;
        }

        if (compilation.IsPrecompiling)
        {
            // WHY: Runtime compiled queries retain provider command metadata, but EF's generated
            // command construction has not qualified that transport. Do not emit an unguarded lock.
            throw new InvalidOperationException("Locking reads are not supported in pregenerated queries.");
        }

        var lowered = VisitSourceChain(query);
        compilation.LockingReadMode = markers.Mode;

        return lowered;
    }

    /// <summary>Follows only known query-source positions; selectors and other sources are independent scopes.</summary>
    private static Expression VisitSourceChain(
        Expression expression
    )
    {
        // WHY: Adjacent markers have no rebuilding work; follow their source without
        // retaining one stack frame per marker. Ordinary operators still rebuild outward.
        while (expression is MethodCallExpression marker
            && TryGetMode(marker.Method, out _))
        {
            expression = marker.Arguments[0];
        }

        if (expression is MethodCallExpression call)
        {
            if (!IsSourceChainOperator(call.Method))
            {
                throw new InvalidOperationException(
                    "The locking read uses an unsupported query operator or terminal.");
            }

            // WHY: Argument zero retains the executed source scope; every other argument is nested.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 1; index < call.Arguments.Count; index++)
            {
                RejectNestedMarker(call.Arguments[index]);
            }

            // WHY: A join's second input participates directly only if EF later retains a
            // physical table. Validate its actual roots without assigning it a separate lock mode.
            if (IsJoinOperator(call.Method))
            {
                ValidateUnmarkedSource(call.Arguments[1]);
            }

            var source = VisitSourceChain(call.Arguments[0]);

            if (ReferenceEquals(source, call.Arguments[0]))
            {
                return call;
            }

            var arguments = call.Arguments.ToArray();
            arguments[0] = source;

            return call.Update(call.Object, arguments);
        }

        ValidateSourceRoot(expression);

        return expression;
    }

    /// <summary>Checks an unmarked join input while retaining scalar predicate subqueries as separate scopes.</summary>
    private static void ValidateUnmarkedSource(
        Expression expression
    )
    {
        // WHY: Validate a join's first branch before continuing through its second;
        // linear source operators need no retained frame or temporary traversal stack.
        while (expression is MethodCallExpression call
            && IsSourceChainOperator(call.Method))
        {
            if (IsJoinOperator(call.Method))
            {
                ValidateUnmarkedSource(call.Arguments[0]);
                expression = call.Arguments[1];
            }
            else
            {
                expression = call.Arguments[0];
            }
        }

        ValidateSourceRoot(expression);
    }

    /// <summary>Rejects an ineffective entity source before projection can erase its entity metadata.</summary>
    private static void ValidateSourceRoot(
        Expression expression
    )
    {
        if (expression is EntityQueryRootExpression root)
        {
            if (root.GetType() != typeof(EntityQueryRootExpression)
                || root.EntityType.FindPrimaryKey() is null
                || root.EntityType.GetViewName() is not null)
            {
                throw new InvalidOperationException(
                    "Locking reads require a mapped keyed table source; raw SQL, views and temporal or bound sources are unsupported.");
            }

            return;
        }

        RejectNestedMarker(expression);
    }

    /// <summary>Distinguishes exact Doka method identities from tags and similarly named application methods.</summary>
    private static bool TryGetMode(
        MethodInfo method,
        out MySqlLockingReadMode mode
    )
    {
        mode = MySqlLockingReadMode.None;

        if (method.DeclaringType != typeof(MySqlQueryableExtensions)
            || !method.IsGenericMethod)
        {
            return false;
        }

        var definition = method.GetGenericMethodDefinition();

        if (definition == MySqlQueryableExtensions.ForUpdateMethod)
        {
            mode = MySqlLockingReadMode.Update;
        }
        else if (definition == MySqlQueryableExtensions.ForShareMethod)
        {
            mode = MySqlLockingReadMode.Share;
        }
        else if (definition == MySqlQueryableExtensions.ForUpdateNoWaitMethod)
        {
            mode = MySqlLockingReadMode.NoWait;
        }
        else if (definition == MySqlQueryableExtensions.ForUpdateSkipLockedMethod)
        {
            mode = MySqlLockingReadMode.SkipLocked;
        }

        return mode != MySqlLockingReadMode.None;
    }

    /// <summary>Admits ordinary composition whose completed physical SELECT scope can be validated later.</summary>
    private static bool IsSourceChainOperator(
        MethodInfo method
    )
    {
        if (method.DeclaringType == typeof(Queryable))
        {
            return method.Name is nameof(Queryable.Where)
                or nameof(Queryable.Select)
                or nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.ThenByDescending)
                or nameof(Queryable.Skip)
                or nameof(Queryable.Take)
                or nameof(Queryable.First)
                or nameof(Queryable.FirstOrDefault)
                or nameof(Queryable.Single)
                or nameof(Queryable.SingleOrDefault)
                or nameof(Queryable.Join)
                or nameof(Queryable.LeftJoin)
                or nameof(Queryable.GroupJoin)
                or nameof(Queryable.SelectMany)
                or nameof(Queryable.OfType)
                or nameof(Queryable.Cast)
                or nameof(Queryable.AsQueryable);
        }

        if (method.DeclaringType == typeof(EntityFrameworkQueryableExtensions))
        {
            return method.Name is nameof(EntityFrameworkQueryableExtensions.AsTracking)
                or nameof(EntityFrameworkQueryableExtensions.AsNoTracking)
                or nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution)
                or nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                or nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes)
                or nameof(EntityFrameworkQueryableExtensions.Include)
                or nameof(EntityFrameworkQueryableExtensions.ThenInclude)
                or nameof(EntityFrameworkQueryableExtensions.TagWith)
                or nameof(EntityFrameworkQueryableExtensions.TagWithCallSite);
        }

        return (method.DeclaringType == typeof(RelationalQueryableExtensions)
                && method.Name == nameof(RelationalQueryableExtensions.AsSingleQuery))
            || (method.DeclaringType == typeof(MySqlQueryableExtensions)
                && method.IsGenericMethod
                && method.GetGenericMethodDefinition() == MySqlQueryableExtensions.StraightJoinMethod);
    }

    private static bool IsJoinOperator(
        MethodInfo method
    ) => (method.DeclaringType == typeof(Queryable)
            && method.Name is nameof(Queryable.Join) or nameof(Queryable.LeftJoin) or nameof(Queryable.GroupJoin))
        || (method.DeclaringType == typeof(MySqlQueryableExtensions)
            && method.IsGenericMethod
            && method.GetGenericMethodDefinition() == MySqlQueryableExtensions.StraightJoinMethod);

    private static void RejectNestedMarker(
        Expression expression
    )
    {
        var inventory = new MarkerInventory();
        inventory.Visit(expression);

        if (inventory.Mode != MySqlLockingReadMode.None)
        {
            throw new InvalidOperationException("A locking read cannot be embedded in another query scope.");
        }
    }

    /// <summary>Discovers all exact markers before simplification can erase a terminal's source.</summary>
    private sealed class MarkerInventory : ExpressionVisitor
    {
        public MySqlLockingReadMode Mode { get; private set; }

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (TryGetMode(node.Method, out var mode))
            {
                if (Mode != MySqlLockingReadMode.None
                    && Mode != mode)
                {
                    throw new InvalidOperationException("A query cannot combine different locking read modes.");
                }

                Mode = mode;
            }

            return base.VisitMethodCall(node);
        }
    }
}
