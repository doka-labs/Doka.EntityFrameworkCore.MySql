namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Rewrites APPLY shapes that MariaDB can represent without a LATERAL derived
/// table while preserving query cardinality or DML target-set semantics.
/// </summary>
internal sealed class MySqlMariaDbApplyRewritingExpressionVisitor
    : MySqlShapedQueryTraversingExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlMariaDbApplyRewritingExpressionVisitor(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
    }

    protected override Expression VisitExtension(
        Expression node
    )
    {
        var visited = base.VisitExtension(node);

        return visited switch
        {
            SelectExpression selectExpression => RewriteFlattenableApply(selectExpression),
            DeleteExpression deleteExpression => RewriteDeleteTargetSelection(deleteExpression),
            UpdateExpression updateExpression => RewriteUpdateTargetSelection(updateExpression),
            _ => visited,
        };
    }

    /// <summary>
    /// Lifts a simple correlated derived table into an ordinary predicate join.
    /// The rewrite keeps duplicate rows and OUTER APPLY null extension intact.
    /// </summary>
    private SelectExpression RewriteFlattenableApply(
        SelectExpression selectExpression
    )
    {
        var rewritten = selectExpression;

        for (var index = 0; index < rewritten.Tables.Count; index++)
        {
            rewritten = TryFlattenApply(rewritten, index) ?? rewritten;
        }

        return rewritten;
    }

    /// <summary>
    /// Flattens only projection-preserving APPLY shapes. DISTINCT, grouping,
    /// ordering, pagination, and unmappable projections retain APPLY because
    /// moving those operations across the join would change query semantics.
    /// </summary>
    private SelectExpression? TryFlattenApply(
        SelectExpression outer,
        int tableIndex
    )
    {
        var table = outer.Tables[tableIndex];
        var inner = MySqlTableExpressionHelper.GetApplySelect(table);

        if (inner is null
            || inner.Alias is null
            || inner.Tables.Count != 1
            || inner.IsDistinct
            || inner.GroupBy.Count > 0
            || inner.Having is not null
            || inner.Orderings.Count > 0
            || inner.Offset is not null
            || inner.Limit is not null
            || inner.Tags.Count > 0
            || inner
                .GetAnnotations()
                .Any()
            || table
                .GetAnnotations()
                .Any())
        {
            return null;
        }

        var projectionMap = CreateProjectionMap(inner);
        if (projectionMap is null)
        {
            return null;
        }

        var remapper = new ApplyProjectionRemappingExpressionVisitor(
            _sqlExpressionFactory,
            inner.Alias,
            projectionMap,
            inner.Predicate,
            MySqlTableExpressionHelper.CollectAliases(inner.Tables),
            makeNullable: table is OuterApplyExpression);
        var predicate = (SqlExpression?)remapper.Visit(outer.Predicate);
        var having = (SqlExpression?)remapper.Visit(outer.Having);
        var offset = (SqlExpression?)remapper.Visit(outer.Offset);
        var limit = (SqlExpression?)remapper.Visit(outer.Limit);
        var groupBy = outer
            .GroupBy.Select(expression => (SqlExpression)remapper.Visit(expression))
            .ToArray();
        var projections = outer
            .Projection.Select(projection => projection.Update((SqlExpression)remapper.Visit(projection.Expression)))
            .ToArray();
        var orderings = outer
            .Orderings.Select(ordering => ordering.Update((SqlExpression)remapper.Visit(ordering.Expression)))
            .ToArray();

        if (remapper.HasUnmappedReference)
        {
            return null;
        }

        TableExpressionBase? rewrittenTable = table switch
        {
            CrossApplyExpression when inner.Predicate is null => new CrossJoinExpression(inner.Tables[0]),
            CrossApplyExpression => new InnerJoinExpression(inner.Tables[0], inner.Predicate!),
            OuterApplyExpression => new LeftJoinExpression(
                inner.Tables[0],
                inner.Predicate ?? _sqlExpressionFactory.Constant(true)),
            _ => null,
        };

        if (rewrittenTable is null)
        {
            return null;
        }

        var tables = outer.Tables.ToArray();
        tables[tableIndex] = rewrittenTable;

        return outer.Update(
            tables,
            predicate,
            groupBy,
            having,
            projections,
            orderings,
            offset,
            limit);
    }

    private static Dictionary<string, SqlExpression>? CreateProjectionMap(
        SelectExpression selectExpression
    )
    {
        var projections = new Dictionary<string, SqlExpression>(StringComparer.Ordinal);

        foreach (var projection in selectExpression.Projection)
        {
            if (string.IsNullOrEmpty(projection.Alias)
                || !projections.TryAdd(projection.Alias, projection.Expression))
            {
                return null;
            }
        }

        return projections;
    }

    /// <summary>
    /// ExecuteDelete targets a set of rows rather than a result sequence. An
    /// unreferenced CROSS APPLY therefore becomes EXISTS, while an unreferenced
    /// OUTER APPLY cannot change the target set and can be removed.
    /// </summary>
    private DeleteExpression RewriteDeleteTargetSelection(
        DeleteExpression deleteExpression
    )
    {
        var selectExpression = RewriteMutationTargetSelection(deleteExpression.SelectExpression, []);

        return deleteExpression.Update(deleteExpression.Table, selectExpression);
    }

    /// <summary>
    /// Applies the same set-based rewrite to ExecuteUpdate, but also protects
    /// APPLY aliases referenced by a setter column or value expression.
    /// </summary>
    private UpdateExpression RewriteUpdateTargetSelection(
        UpdateExpression updateExpression
    )
    {
        var setterExpressions = updateExpression
            .ColumnValueSetters.SelectMany(static setter => new SqlExpression[]
            {
                setter.Column,
                setter.Value,
            })
            .ToArray();
        var selectExpression = RewriteMutationTargetSelection(updateExpression.SelectExpression, setterExpressions);

        return updateExpression.Update(selectExpression, updateExpression.ColumnValueSetters);
    }

    private SelectExpression RewriteMutationTargetSelection(
        SelectExpression selectExpression,
        IReadOnlyList<SqlExpression> additionalExpressions
    )
    {
        List<TableExpressionBase>? rewrittenTables = null;
        var predicate = selectExpression.Predicate;

        for (var index = 0; index < selectExpression.Tables.Count; index++)
        {
            var table = selectExpression.Tables[index];
            var inner = MySqlTableExpressionHelper.GetApplySelect(table);
            var canRewrite = inner?.Alias is not null
                && !ReferencesAlias(selectExpression, inner.Alias, additionalExpressions);

            if (!canRewrite)
            {
                rewrittenTables?.Add(table);
                continue;
            }

            rewrittenTables ??= CopyTablesBefore(selectExpression.Tables, index);

            if (table is CrossApplyExpression)
            {
                var exists = _sqlExpressionFactory.Exists(
                    (SelectExpression)inner!.Clone(alias: null, AliasRemovingCloningExpressionVisitor.Instance));
                predicate = predicate is null ? exists : _sqlExpressionFactory.AndAlso(predicate, exists);
            }
        }

        return rewrittenTables is null
            ? selectExpression
            : selectExpression.Update(
                rewrittenTables,
                predicate,
                selectExpression.GroupBy,
                selectExpression.Having,
                selectExpression.Projection,
                selectExpression.Orderings,
                selectExpression.Offset,
                selectExpression.Limit);
    }

    private static List<TableExpressionBase> CopyTablesBefore(
        IReadOnlyList<TableExpressionBase> tables,
        int exclusiveEnd
    )
    {
        var copy = new List<TableExpressionBase>(tables.Count - 1);

        for (var index = 0; index < exclusiveEnd; index++)
        {
            copy.Add(tables[index]);
        }

        return copy;
    }

    private static bool ReferencesAlias(
        SelectExpression selectExpression,
        string tableAlias,
        IReadOnlyList<SqlExpression>? additionalExpressions = null
    )
    {
        var finder = new ColumnAliasFindingExpressionVisitor(tableAlias);

        finder.Visit(selectExpression.Predicate);
        finder.Visit(selectExpression.Having);
        finder.Visit(selectExpression.Offset);
        finder.Visit(selectExpression.Limit);

        foreach (var projection in selectExpression.Projection)
        {
            finder.Visit(projection.Expression);
        }

        foreach (var grouping in selectExpression.GroupBy)
        {
            finder.Visit(grouping);
        }

        foreach (var ordering in selectExpression.Orderings)
        {
            finder.Visit(ordering.Expression);
        }

        if (additionalExpressions is not null)
        {
            foreach (var expression in additionalExpressions)
            {
                finder.Visit(expression);
            }
        }

        return finder.Found;
    }

    private sealed class ColumnAliasFindingExpressionVisitor : ExpressionVisitor
    {
        private readonly string _tableAlias;

        public ColumnAliasFindingExpressionVisitor(
            string tableAlias
        )
        {
            _tableAlias = tableAlias;
        }

        public bool Found { get; private set; }

        public override Expression? Visit(
            Expression? node
        )
        {
            if (Found || node is null)
            {
                return node;
            }

            if (node is ColumnExpression column
                && column.TableAlias == _tableAlias)
            {
                Found = true;
                return node;
            }

            return base.Visit(node);
        }
    }

    private sealed class ApplyProjectionRemappingExpressionVisitor : ExpressionVisitor
    {
        private readonly IReadOnlySet<string> _innerTableAliases;
        private readonly bool _makeNullable;
        private readonly SqlExpression? _matchPredicate;
        private readonly IReadOnlyDictionary<string, SqlExpression> _projectionMap;
        private readonly ISqlExpressionFactory _sqlExpressionFactory;
        private readonly string _tableAlias;

        public ApplyProjectionRemappingExpressionVisitor(
            ISqlExpressionFactory sqlExpressionFactory,
            string tableAlias,
            IReadOnlyDictionary<string, SqlExpression> projectionMap,
            SqlExpression? matchPredicate,
            IReadOnlySet<string> innerTableAliases,
            bool makeNullable
        )
        {
            _sqlExpressionFactory = sqlExpressionFactory;
            _tableAlias = tableAlias;
            _projectionMap = projectionMap;
            _matchPredicate = matchPredicate;
            _innerTableAliases = innerTableAliases;
            _makeNullable = makeNullable;
        }

        public bool HasUnmappedReference { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not ColumnExpression column
                || column.TableAlias != _tableAlias)
            {
                return base.VisitExtension(node);
            }

            if (!_projectionMap.TryGetValue(column.Name, out var replacement)
                || replacement is not ColumnExpression replacementColumn)
            {
                HasUnmappedReference = true;
                return node;
            }

            if (!_makeNullable)
            {
                return replacementColumn;
            }

            if (_innerTableAliases.Contains(replacementColumn.TableAlias))
            {
                return replacementColumn.MakeNullable();
            }

            // LEFT JOIN makes inner columns nullable automatically. An outer column
            // stays populated on an unmatched row, so CASE preserves OUTER APPLY's
            // required null-extended projection.
            if (_matchPredicate is null)
            {
                HasUnmappedReference = true;
                return node;
            }

            return _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(_matchPredicate, replacementColumn),
                ],
                _sqlExpressionFactory.Constant(
                    null,
                    replacementColumn.Type,
                    replacementColumn.TypeMapping));
        }
    }

    /// <summary>
    /// Supplies the public SelectExpression cloning contract with an identity
    /// visitor so an APPLY table alias can be removed for EXISTS syntax.
    /// </summary>
    private sealed class AliasRemovingCloningExpressionVisitor : ExpressionVisitor
    {
        public static AliasRemovingCloningExpressionVisitor Instance { get; } = new();

        private AliasRemovingCloningExpressionVisitor() { }
    }
}
