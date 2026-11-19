namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    private bool UsesWindowFunctions => ((MySqlQueryCompilationContext)_queryCompilationContext).UsesWindowFunctions;

    /// <summary>Consumes only the exact completion marker inserted before EF navigation expansion.</summary>
    private ShapedQueryExpression? TranslateWindowBoundary(
        MethodCallExpression call
    )
    {
        if (!call.Method.IsGenericMethod
            || call.Method.GetGenericMethodDefinition() != MySqlIndexedSelectLowering.CompleteMethod)
        {
            return null;
        }

        var source = Visit(call.Arguments[0]) as ShapedQueryExpression
            ?? throw new InvalidOperationException(
                "The completed window source could not be translated to a relational query.");

        PrepareWindowComposition(source);

        return source;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression TranslateSelect(
        ShapedQueryExpression source,
        LambdaExpression selector
    )
    {
        if (!UsesWindowFunctions)
        {
            return base.TranslateSelect(source, selector);
        }

        var select = (SelectExpression)source.QueryExpression;
        var indexFinding = new IndexMarkerFinding();
        indexFinding.Visit(selector.Body);

        if (indexFinding.Markers.Count != 0
            || MySqlIndexedSelectLowering.ContainsWindowCall(selector.Body))
        {
            // WHY: A window introduced after a completed page or DISTINCT ranks exactly that
            // selected source, not the unbounded rows that happened to precede it in the same SQL block.
            if (select.Limit is not null
                || select.Offset is not null
                || select.IsDistinct
                || select.GroupBy.Count != 0)
            {
                select.PushdownIntoSubquery();
            }
        }

        if (indexFinding.Markers.Count != 0)
        {
            var replacements = new Expression[indexFinding.Markers.Count];

            for (var index = 0; index < indexFinding.Markers.Count; index++)
            {
                replacements[index] = TranslateIndexMarker(source, selector.Parameters[0], indexFinding.Markers[index]);
            }

            selector = Expression.Lambda(
                selector.Type,
                ReplacingExpressionVisitor.Replace(indexFinding.Markers, replacements, selector.Body),
                selector.Parameters);
        }

        return base.TranslateSelect(source, selector);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateWhere(
        ShapedQueryExpression source,
        LambdaExpression predicate
    )
    {
        if (!UsesWindowFunctions)
        {
            return base.TranslateWhere(source, predicate);
        }

        if (MySqlIndexedSelectLowering.ContainsWindowCall(predicate.Body))
        {
            throw new InvalidOperationException(
                "A window calculation must be projected before it is used by a filter.");
        }

        PrepareWindowComposition(source);

        return base.TranslateWhere(source, predicate);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateGroupBy(
        ShapedQueryExpression source,
        LambdaExpression keySelector,
        LambdaExpression? elementSelector,
        LambdaExpression? resultSelector
    )
    {
        if (!UsesWindowFunctions)
        {
            return base.TranslateGroupBy(source, keySelector, elementSelector, resultSelector);
        }

        if (MySqlIndexedSelectLowering.ContainsWindowCall(keySelector.Body))
        {
            throw new InvalidOperationException(
                "A window calculation must be projected before it is used by grouping.");
        }

        PrepareWindowComposition(source);

        return base.TranslateGroupBy(source, keySelector, elementSelector, resultSelector);
    }

    /// <summary>Preserves completed projected windows before a later relational operator remaps its lambda.</summary>
    private void PrepareWindowComposition(
        ShapedQueryExpression source
    )
    {
        if (!UsesWindowFunctions)
        {
            return;
        }

        var select = (SelectExpression)source.QueryExpression;
        var finding = new WindowFinding(select);
        finding.Visit(source.ShaperExpression);

        if (finding.Found)
        {
            select.PushdownIntoSubquery();
        }
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateLeftJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector
    )
    {
        PrepareWindowComposition(outer);
        PrepareWindowComposition(inner);

        return base.TranslateLeftJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateRightJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector
    )
    {
        PrepareWindowComposition(outer);
        PrepareWindowComposition(inner);

        return base.TranslateRightJoin(outer, inner, outerKeySelector, innerKeySelector, resultSelector);
    }

    /// <summary>Translates the index from the actual SQL scope and stores the checked CLR conversion in its mapping.</summary>
    private SqlExpression TranslateIndexMarker(
        ShapedQueryExpression source,
        ParameterExpression parameter,
        MethodCallExpression marker
    )
    {
        var engine = _queryCompilationContext.ContextOptions.FindExtension<MySqlOptionsExtension>()
            ?.ServerVersion?.Profile.Engine;

        if (engine?.Has(EngineCapability.WindowFunctions) != true)
        {
            throw new InvalidOperationException("The configured engine does not support window calculations.");
        }

        var select = (SelectExpression)source.QueryExpression;
        SqlExpression[] partitions = [];
        var orderings = select.Orderings.ToArray();
        SqlExpression? included = null;
        SqlExpression? skipped = null;

        if (marker.Method == MySqlIndexedSelectLowering.PartitionIndexMethod)
        {
            var partitionKeys = marker.Arguments[0] as ListInitExpression
                ?? throw new InvalidOperationException("A partitioned index requires structural partition keys.");

            var orderingKeys = marker.Arguments[1] as ListInitExpression
                ?? throw new InvalidOperationException("A partitioned index requires structural ordering keys.");

            var descending = marker.Arguments[2] is ConstantExpression { Value: bool[] values }
                ? values
                : throw new InvalidOperationException("A partitioned index requires fixed ordering directions.");

            if (orderingKeys.Initializers.Count != descending.Length
                || partitionKeys.Initializers.Any(initializer
                    => initializer.AddMethod != MySqlIndexedSelectLowering.KeyListAddMethod)
                || orderingKeys.Initializers.Any(initializer
                    => initializer.AddMethod != MySqlIndexedSelectLowering.KeyListAddMethod))
            {
                throw new InvalidOperationException("A partitioned index has inconsistent ordering metadata.");
            }

            var translatedPartitions = new SqlExpression[partitionKeys.Initializers.Count];

            for (var index = 0; index < translatedPartitions.Length; index++)
            {
                translatedPartitions[index] = TranslateIndexLeaf(partitionKeys
                    .Initializers[index]
                    .Arguments[0]);
            }

            var translatedOrderings = new OrderingExpression[orderingKeys.Initializers.Count];

            for (var index = 0; index < translatedOrderings.Length; index++)
            {
                translatedOrderings[index] = new OrderingExpression(TranslateIndexLeaf(orderingKeys
                        .Initializers[index]
                        .Arguments[0]),
                    !descending[index]);
            }

            partitions = translatedPartitions;
            orderings = translatedOrderings;
            included = marker.Arguments[3] is ConstantExpression { Value: true }
                ? null
                : TranslateIndexLeaf(marker.Arguments[3]);

            skipped = marker.Arguments[4] is ConstantExpression { Value: 0L }
                ? null
                : TranslateIndexLeaf(marker.Arguments[4]);
        }

        if (orderings.Length == 0)
        {
            throw new InvalidOperationException("An indexed Select requires an explicit source ordering.");
        }

        var nested = new WindowFinding();

        foreach (var partition in partitions)
        {
            nested.Visit(partition);
        }

        foreach (var ordering in orderings)
        {
            nested.Visit(ordering.Expression);
        }

        nested.Visit(included);

        if (nested.Found)
        {
            throw new InvalidOperationException(
                "A window calculation must be projected before it is used by another window.");
        }

        var longMapping = _typeMappingSource.FindMapping(typeof(long))!;
        SqlExpression ordinal;

        if (included is null)
        {
            // WHY: EF11's native quotation preserves partitions and ordering, so the
            // framework node needs no provider-specific quotation adapter.
            ordinal = new RowNumberExpression(partitions, orderings, longMapping);
        }
        else
        {
            var candidate = _sqlExpressionFactory.Case(
                [new CaseWhenClause(included, _sqlExpressionFactory.Constant(1L, longMapping))],
                _sqlExpressionFactory.Constant(0L, longMapping));

            // WHY: A filtered group member is indexed among its retained rows. Counting the
            // predicate prefix preserves that scope without changing the enclosing group's aggregates.
            ordinal = new MySqlWindowExpression(
                MySqlWindowFunction.Sum,
                [candidate],
                partitions,
                orderings,
                new MySqlWindowFrame(true,
                    MySqlWindowBoundKind.UnboundedPreceding,
                    null,
                    MySqlWindowBoundKind.CurrentRow,
                    null),
                typeof(long),
                longMapping,
                nullable: false);
        }

        var zeroBased = _sqlExpressionFactory.Subtract(ordinal, _sqlExpressionFactory.Constant(1L, longMapping));

        if (skipped is not null)
        {
            zeroBased = _sqlExpressionFactory.Subtract(zeroBased, skipped);
        }

        // WHY: Both qualified connectors check Int64-to-Int32 reads, as EF's ordinary Count
        // mapping already relies on. Reuse that mapping; an extra converter is not preserved by EF's type-mapping quotation.
        return _sqlExpressionFactory.Convert(zeroBased, typeof(int), _typeMappingSource.FindMapping(typeof(int)));

        SqlExpression TranslateIndexLeaf(
            Expression expression
        )
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert, Type: var target } conversion
                && target == typeof(object))
            {
                expression = conversion.Operand;
            }

            return TranslateExpression(
                    ReplacingExpressionVisitor.Replace(parameter, source.ShaperExpression, expression))
                ?? throw new InvalidOperationException(
                    "A partitioned index key or filter could not be translated to SQL.");
        }
    }

    private sealed class IndexMarkerFinding : ExpressionVisitor
    {
        internal List<MethodCallExpression> Markers { get; } = [];

        protected override Expression VisitLambda<T>(
            Expression<T> node
        ) => node;

        protected override Expression VisitMethodCall(
            MethodCallExpression node
        )
        {
            if (node.Method == MySqlIndexedSelectLowering.IndexMethod
                || node.Method == MySqlIndexedSelectLowering.PartitionIndexMethod)
            {
                Markers.Add(node);

                return node;
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class WindowFinding : ExpressionVisitor
    {
        private readonly SelectExpression? _source;

        /// <summary>Initializes the immutable dependencies for this operation.</summary>
        public WindowFinding(
            SelectExpression? source = null
        )
        {
            _source = source;
        }

        internal bool Found { get; private set; }

        public override Expression? Visit(
            Expression? node
        ) => Found
            ? node
            : base.Visit(node);

        protected override Expression VisitExtension(
            Expression node
        )
        {
            switch (node)
            {
                case MySqlWindowExpression or RowNumberExpression:
                    Found = true;

                    return node;

                case SelectExpression:
                    return node;

                case GroupByShaperExpression grouping:
                    // WHY: Only the key shapes the current group row. The group's enumerable
                    // has its own relational scope, and EF forbids generic traversal of its shaper.
                    Visit(grouping.KeySelector);

                    return node;

                case ProjectionBindingExpression binding
                    when _source is not null && ReferenceEquals(binding.QueryExpression, _source):
                    Visit(_source.GetProjection(binding));

                    return node;
            }

            return base.VisitExtension(node);
        }
    }
}
