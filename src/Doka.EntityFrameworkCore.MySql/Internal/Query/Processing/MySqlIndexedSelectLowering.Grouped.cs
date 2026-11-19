namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlIndexedSelectLowering
{
    private static readonly MethodInfo s_enumerableSelectMethod = typeof(Enumerable)
        .GetMethods()
        .Single(method => method.Name == nameof(Enumerable.Select)
            && method
                .GetParameters()[1]
                .ParameterType
                .GetGenericArguments()
                .Length
            == 2);

    /// <summary>Retains EF's group result shaper while carrying each requested member ordinal from the original rows.</summary>
    private Expression? TryLowerGroupedSelect(
        MethodCallExpression selection
    )
    {
        if (!selection.Method.IsGenericMethod
            || selection.Method.GetGenericMethodDefinition() != QueryableMethods.Select
            || FindGrouping(selection.Arguments[0], out var groupSequence) is not { } grouping)
        {
            return null;
        }

        var resultSelector = UnwrapLambda(selection.Arguments[1]);
        var group = resultSelector.Parameters[0];
        var finding = new GroupedIndexFinding(group);
        finding.Visit(resultSelector.Body);

        if (finding.Selections.Count == 0)
        {
            return null;
        }

        var originalType = grouping
            .Method
            .GetGenericArguments()[0];

        var keySelector = UnwrapLambda(grouping.Arguments[1]);
        var descriptors = finding
            .Selections
            .Select(indexed => DescribeGroupedIndex(indexed, group))
            .ToArray();

        var source = Visit(grouping.Arguments[0]);
        var sourceType = originalType;
        var members = new List<(FieldInfo Outer, FieldInfo Inner)>(descriptors.Length);

        for (var index = 0; index < descriptors.Length; index++)
        {
            var descriptor = descriptors[index];
            var row = Expression.Parameter(sourceType, "windowRow");
            var original = OriginalRow(row, index, members);
            var partitions =
                FlattenKey(ReplacingExpressionVisitor.Replace(keySelector.Parameters[0], original, keySelector.Body));

            var orderings = descriptor.Orderings.Select(ordering =>
                ReplacingExpressionVisitor.Replace(ordering.Selector.Parameters[0], original, ordering.Selector.Body));

            Expression included = Expression.Constant(true);

            // WHY: Fold filters in their declared order so each predicate extends the same expression tree.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var predicate in descriptor.Filters)
            {
                included = Expression.AndAlso(included,
                    ReplacingExpressionVisitor.Replace(predicate.Parameters[0], original, predicate.Body));
            }

            var ordinal = Expression.Call(
                PartitionIndexMethod,
                CreateKeys(partitions),
                CreateKeys(orderings),
                Expression.Constant(descriptor
                    .Orderings
                    .Select(ordering => ordering.Descending)
                    .ToArray()),
                included,
                descriptor.Skipped);

            var carrierType = TransparentIdentifierFactory.Create(sourceType, typeof(int));
            var outerMember = carrierType
                .GetTypeInfo()
                .GetDeclaredField("Outer")!;

            var innerMember = carrierType
                .GetTypeInfo()
                .GetDeclaredField("Inner")!;

            members.Add((outerMember, innerMember));
            var creation = CreateCarrier(carrierType
                    .GetConstructors()[0],
                row,
                ordinal,
                outerMember,
                innerMember);

            var selectMethod = CloseMethod(QueryableMethods.Select, [sourceType, carrierType]);
            source = CompleteProjection(Expression.Call(
                    selectMethod,
                    source,
                    Expression.Quote(CreateSelector(selectMethod, creation, row))),
                carrierType);

            sourceType = carrierType;
        }

        var carrier = Expression.Parameter(sourceType, "windowCarrier");
        var groupingKey = PreserveNullableGroupKey(ReplacingExpressionVisitor.Replace(
            keySelector.Parameters[0],
            OriginalRow(carrier, descriptors.Length, members),
            keySelector.Body));

        var groupByMethod =
            CloseGroupedMethod(QueryableMethods.GroupByWithKeySelector, [sourceType, groupingKey.Body.Type]);

        var newKey = CreateSelector(
            groupByMethod,
            groupingKey.Body,
            carrier);

        var regrouped = Expression.Call(
            groupByMethod,
            source,
            Expression.Quote(newKey));

        var groupType = regrouped
            .Type
            .GetGenericArguments()[0];

        var newGroup = Expression.Parameter(groupType, group.Name);
        var originalProjectionMethod = CloseGroupedMethod(QueryableMethods.Select, [sourceType, originalType]);

        // WHY: This projection is inserted after EF's normalizer. Its exact Queryable shape
        // lets navigation expansion compose group aggregates instead of retaining an unprocessed Enumerable.Select.
        var projectedGroup = Expression.Call(
            originalProjectionMethod,
            Expression.Call(CloseMethod(QueryableMethods.AsQueryable, [sourceType]), newGroup),
            Expression.Quote(CreateSelector(originalProjectionMethod,
                OriginalRow(carrier, descriptors.Length, members),
                carrier)));

        var replacements = new Dictionary<MethodCallExpression, Expression>(ReferenceEqualityComparer.Instance);

        for (var index = 0; index < descriptors.Length; index++)
        {
            var descriptor = descriptors[index];
            var replayed = ReplayGroupChain(descriptor.Chain, newGroup, sourceType, descriptors.Length, members);
            var parameter = Expression.Parameter(sourceType, "indexedRow");
            var ordinal = Expression.Field(OriginalRow(parameter, descriptors.Length - index - 1, members),
                members[index].Inner);

            var selector = UnwrapLambda(descriptor.Selection.Arguments[1]);
            var body = ReplacingExpressionVisitor.Replace(
                [selector.Parameters[0], selector.Parameters[1]],
                [OriginalRow(parameter, descriptors.Length, members), ordinal],
                selector.Body);

            var queryable = descriptor.Selection.Method.DeclaringType == typeof(Queryable);
            var selectMethod = CloseGroupedMethod(queryable
                    ? QueryableMethods.Select
                    : s_enumerableSelectMethod,
                [sourceType, body.Type]);

            var unary = CreateSelector(selectMethod, body, parameter);

            if (queryable && !typeof(IQueryable).IsAssignableFrom(replayed.Type))
            {
                replayed = Expression.Call(CloseMethod(QueryableMethods.AsQueryable, [sourceType]), replayed);
            }

            replacements.Add(descriptor.Selection,
                Expression.Call(
                    selectMethod,
                    replayed,
                    queryable
                        ? Expression.Quote(unary)
                        : unary));
        }

        var rewritten =
            new GroupResultRewriter(group, newGroup, projectedGroup, replacements, groupingKey.OriginalKeyMember)
                .Visit(resultSelector.Body);

        var outerSelectMethod = CloseMethod(QueryableMethods.Select, [groupType, resultSelector.ReturnType]);
        var result = Expression.Call(
            outerSelectMethod,
            ReplayGroupSequence(groupSequence,
                regrouped,
                groupType,
                newGroup,
                projectedGroup,
                groupingKey.OriginalKeyMember),
            Expression.Quote(CreateSelector(outerSelectMethod, rewritten, newGroup)));

        // WHY: The outer GroupBy/result shape stays ordinary EF LINQ. Only the original
        // indexed members and the source carrier change; Count and collection shaping remain EF-owned.
        return Visit(result);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification =
            "EF's transparent identifier factory preserves the exact constructor and Outer/Inner fields supplied here; no name-based property discovery occurs.")]
    private static NewExpression CreateCarrier(
        ConstructorInfo constructor,
        Expression row,
        Expression ordinal,
        FieldInfo outer,
        FieldInfo inner
    ) => Expression.New(constructor, [row, ordinal], outer, inner);

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(IGrouping<,>))]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification =
            "The runtime guard permits only the closed IGrouping interface, whose public Key metadata is explicitly preserved; no application type or member lookup occurs.")]
    private static MemberExpression GroupKey(
        Expression group
    )
    {
        var type = group.Type;

        if (!type.IsGenericType
            || type.GetGenericTypeDefinition() != typeof(IGrouping<,>))
        {
            throw new InvalidOperationException("Only the preserved LINQ grouping key can be rebound.");
        }

        return Expression.Property(group, type.GetProperty(nameof(IGrouping<int, int>.Key))!);
    }

    private static GroupedIndex DescribeGroupedIndex(
        MethodCallExpression selection,
        ParameterExpression group
    )
    {
        var chain = new List<MethodCallExpression>();
        var current = selection.Arguments[0];

        while (!ReferenceEquals(current, group))
        {
            if (current is not MethodCallExpression call
                || call.Method.DeclaringType is not { } declaring
                || (declaring != typeof(Queryable) && declaring != typeof(Enumerable)))
            {
                throw new InvalidOperationException(
                    "A partitioned indexed Select requires one directly composed group source.");
            }

            chain.Add(call);
            current = call.Arguments[0];
        }

        chain.Reverse();
        var orderings = new List<GroupOrdering>();
        var filters = new List<LambdaExpression>();
        Expression skipped = Expression.Constant(0L);
        var paged = false;

        foreach (var call in chain)
        {
            switch (call.Method.Name)
            {
                case nameof(Queryable.AsQueryable):
                case nameof(Enumerable.AsEnumerable):
                    break;

                case nameof(Queryable.Where) when !paged:
                    var filter = UnwrapLambda(call.Arguments[1]);

                    if (filter.Parameters.Count != 1)
                    {
                        throw new InvalidOperationException(
                            "An indexed Where cannot define the partitioned indexed Select source.");
                    }

                    filters.Add(filter);
                    break;

                case nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) when !paged:
                    orderings.Clear();
                    orderings.Add(new GroupOrdering(UnwrapLambda(call.Arguments[1]),
                        call.Method.Name.EndsWith("Descending", StringComparison.Ordinal)));

                    break;

                case nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending) when !paged && orderings.Count != 0:
                    orderings.Add(new GroupOrdering(UnwrapLambda(call.Arguments[1]),
                        call.Method.Name.EndsWith("Descending", StringComparison.Ordinal)));

                    break;

                case nameof(Queryable.Skip) when call.Arguments[1].Type == typeof(int) && orderings.Count != 0:
                    paged = true;
                    var count = call.Arguments[1];
                    skipped = Expression.Add(skipped,
                        Expression.Convert(
                            Expression.Condition(Expression.GreaterThan(count, Expression.Constant(0)),
                                count,
                                Expression.Constant(0)),
                            typeof(long)));

                    break;

                case nameof(Queryable.Take) when call.Arguments[1].Type == typeof(int) && orderings.Count != 0:
                    paged = true;
                    break;

                default:
                    throw new InvalidOperationException(
                        "This grouped indexed Select requires a separate child ranking scope; it cannot reuse the full-group index.");
            }
        }

        if (orderings.Count == 0)
        {
            throw new InvalidOperationException("A partitioned indexed Select requires an explicit member ordering.");
        }

        return new GroupedIndex(selection, chain, orderings, filters, skipped);
    }

    private static Expression ReplayGroupChain(
        IReadOnlyList<MethodCallExpression> chain,
        Expression group,
        Type carrierType,
        int depth,
        IReadOnlyList<(FieldInfo Outer, FieldInfo Inner)> members
    )
    {
        var current = group;

        foreach (var call in chain)
        {
            var arguments = call.Arguments.ToArray();
            var types = call.Method.GetGenericArguments();
            types[0] = carrierType;

            if (call.Method.DeclaringType == typeof(Queryable)
                && call.Method.Name != nameof(Queryable.AsQueryable)
                && !typeof(IQueryable).IsAssignableFrom(current.Type))
            {
                current = Expression.Call(CloseMethod(QueryableMethods.AsQueryable, [carrierType]), current);
            }

            arguments[0] = current;

            if (arguments.Length > 1
                && arguments[1] is UnaryExpression { NodeType: ExpressionType.Quote } or LambdaExpression)
            {
                var selector = UnwrapLambda(arguments[1]);
                var parameter = Expression.Parameter(carrierType, selector.Parameters[0].Name);
                var closedMethod = CloseGroupedMethod(call.Method.GetGenericMethodDefinition(), types);
                var changed = CreateSelector(
                    closedMethod,
                    ReplacingExpressionVisitor.Replace(selector.Parameters[0],
                        OriginalRow(parameter, depth, members),
                        selector.Body),
                    parameter);

                arguments[1] = call.Method.DeclaringType == typeof(Queryable)
                    ? Expression.Quote(changed)
                    : changed;
            }

            current = Expression.Call(CloseGroupedMethod(call.Method.GetGenericMethodDefinition(), types), arguments);
        }

        return current;
    }

    private static Expression OriginalRow(
        Expression carrier,
        int depth,
        IReadOnlyList<(FieldInfo Outer, FieldInfo Inner)> members
    )
    {
        for (var index = 0; index < depth; index++)
        {
            carrier = Expression.Field(carrier, members[members.Count - index - 1].Outer);
        }

        return carrier;
    }

    private static IEnumerable<Expression> FlattenKey(
        Expression key
    ) => key is NewExpression creation
        ? creation.Arguments.SelectMany(FlattenKey)
        : [key];

    private static UnaryExpression Box(
        Expression expression
    ) => Expression.Convert(expression, typeof(object));

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification =
            "Only exact LINQ definitions validated in the grouped source chain are closed, with their original selector result types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification =
            "The existing EF runtime expression compiler owns these transparent carrier types; precompiled support is qualified separately.")]
    private static MethodInfo CloseGroupedMethod(
        MethodInfo method,
        Type[] arguments
    )
    {
        if (method.DeclaringType != typeof(Queryable)
            && method.DeclaringType != typeof(Enumerable))
        {
            throw new InvalidOperationException("Only exact LINQ group methods can be closed here.");
        }

        return method.MakeGenericMethod(arguments);
    }

    private sealed record GroupOrdering(
        LambdaExpression Selector,
        bool Descending
    );

    private sealed record GroupedIndex(
        MethodCallExpression Selection,
        IReadOnlyList<MethodCallExpression> Chain,
        IReadOnlyList<GroupOrdering> Orderings,
        IReadOnlyList<LambdaExpression> Filters,
        Expression Skipped
    );

    private sealed class GroupedIndexFinding : ExpressionVisitor
    {
        private readonly ParameterExpression _group;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public GroupedIndexFinding(
            ParameterExpression group
        )
        {
            _group = group;
        }

        internal List<MethodCallExpression> Selections { get; } = [];

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if ((IsIndexedSelect(node, typeof(Queryable)) || IsIndexedSelect(node, typeof(Enumerable)))
                && ReachesGroup(node.Arguments[0]))
            {
                Selections.Add(node);

                return node;
            }

            return base.VisitMethodCall(node);
        }

        private bool ReachesGroup(
            Expression expression
        )
        {
            while (expression is MethodCallExpression call
                && call.Arguments.Count != 0)
            {
                expression = call.Arguments[0];
            }

            return ReferenceEquals(expression, _group);
        }
    }

    private sealed class GroupResultRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _currentGroup;
        private readonly FieldInfo? _originalKeyMember;
        private readonly ParameterExpression _previousGroup;
        private readonly Expression _projectedGroup;
        private readonly IReadOnlyDictionary<MethodCallExpression, Expression> _selections;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public GroupResultRewriter(
            ParameterExpression previousGroup,
            ParameterExpression currentGroup,
            Expression projectedGroup,
            IReadOnlyDictionary<MethodCallExpression, Expression> selections,
            FieldInfo? originalKeyMember
        )
        {
            _previousGroup = previousGroup;
            _currentGroup = currentGroup;
            _projectedGroup = projectedGroup;
            _selections = selections;
            _originalKeyMember = originalKeyMember;
        }

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        ) => _selections.TryGetValue(node, out var replacement)
            ? replacement
            : base.VisitMethodCall(node);

        protected override Expression VisitMember(
            MemberExpression node
        ) => ReferenceEquals(node.Expression, _previousGroup) && node.Member.Name == nameof(IGrouping<int, int>.Key)
            ? _originalKeyMember is null
                ? GroupKey(_currentGroup)
                : Expression.Field(GroupKey(_currentGroup), _originalKeyMember)
            : base.VisitMember(node);

        protected override Expression VisitParameter(
            ParameterExpression node
        ) => ReferenceEquals(node, _previousGroup)
            ? _projectedGroup
            : node;
    }
}
