namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Rewrites correlated, column-based offsets to a windowed derived table.
/// </summary>
/// <remarks>
/// MySQL and MariaDB accept constants and parameters in <c>LIMIT</c>/<c>OFFSET</c>,
/// but not a column from an outer query. The equivalent row-number predicate
/// keeps the index dynamic without evaluating the query on the client.
/// Sources retrieved 2026-07-29:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/select.html">
/// MySQL 8.4 SELECT</see> and
/// <see href="https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/limit">
/// MariaDB LIMIT</see>.
/// </remarks>
internal sealed class MySqlDynamicOffsetRewritingExpressionVisitor : MySqlShapedQueryTraversingExpressionVisitor
{
    private readonly RelationalTypeMapping _longTypeMapping;
    private readonly SqlAliasManager _sqlAliasManager;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlDynamicOffsetRewritingExpressionVisitor(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        SqlAliasManager sqlAliasManager
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
        _sqlAliasManager = sqlAliasManager ?? throw new ArgumentNullException(nameof(sqlAliasManager));
        _longTypeMapping = typeMappingSource?.FindMapping(typeof(long))
            ?? throw new ArgumentNullException(nameof(typeMappingSource));
    }

    protected override Expression VisitExtension(
        Expression node
    )
    {
        var visited = base.VisitExtension(node);

        return visited is SelectExpression selectExpression
            ? RewriteDynamicOffset(selectExpression) ?? visited
            : visited;
    }

    private SelectExpression? RewriteDynamicOffset(
        SelectExpression selectExpression
    )
    {
        if (selectExpression.Offset is null
            || selectExpression.Limit is not SqlConstantExpression limit
            || Convert.ToInt64(limit.Value, CultureInfo.InvariantCulture) != 1
            || selectExpression.Tables.Count == 0
            || selectExpression.Projection.Count == 0
            || selectExpression.Orderings.Count == 0
            || selectExpression.IsDistinct
            || selectExpression.GroupBy.Count > 0
            || selectExpression.Having is not null
            || selectExpression.Tags.Count > 0
            || selectExpression
                .GetAnnotations()
                .Any())
        {
            return null;
        }

        var innerAliases = MySqlTableExpressionHelper
            .CollectAliases(selectExpression.Tables)
            .ToHashSet(StringComparer.Ordinal);

        var offsetAliases = ColumnAliasCollector.Collect(selectExpression.Offset);

        if (offsetAliases.Count == 0
            || offsetAliases.Overlaps(innerAliases))
        {
            return null;
        }

        var split = SplitPredicate(selectExpression.Predicate, innerAliases);
        if (split is null
            || split.Correlations.Count == 0)
        {
            return null;
        }

        var rankedAlias = _sqlAliasManager.GenerateTableAlias("ranked");
        var projectionAliases = CreateProjectionAliases(selectExpression.Projection);
        var rankedProjections = new List<ProjectionExpression>(
            selectExpression.Projection.Count + split.Correlations.Count + 1);

        for (var index = 0; index < selectExpression.Projection.Count; index++)
        {
            rankedProjections.Add(
                new ProjectionExpression(selectExpression.Projection[index].Expression, projectionAliases[index]));
        }

        for (var index = 0; index < split.Correlations.Count; index++)
        {
            rankedProjections.Add(new ProjectionExpression(split.Correlations[index].Inner, GetPartitionAlias(index)));
        }

        rankedProjections.Add(
            new ProjectionExpression(
                new RowNumberExpression(
                    split
                        .Correlations.Select(static correlation => (SqlExpression)correlation.Inner)
                        .ToArray(),
                    selectExpression.Orderings,
                    _longTypeMapping),
                "__row"));

#pragma warning disable EF1001 // The window rewrite must construct EF Core's internal SelectExpression shape.
        var ranked = new SelectExpression(
            rankedAlias,
            selectExpression.Tables,
            split.LocalPredicate,
            groupBy: [],
            having: null,
            rankedProjections,
            distinct: false,
            orderings: [],
            offset: null,
            limit: null,
            _sqlAliasManager);
#pragma warning restore EF1001

        var outerProjections = new List<ProjectionExpression>(selectExpression.Projection.Count);

        for (var index = 0; index < selectExpression.Projection.Count; index++)
        {
            var expression = selectExpression.Projection[index].Expression;
            outerProjections.Add(
                new ProjectionExpression(
                    CreateColumn(projectionAliases[index], rankedAlias, expression, nullable: IsNullable(expression)),
                    selectExpression.Projection[index].Alias));
        }

        var predicate = split.OuterPredicate;

        for (var index = 0; index < split.Correlations.Count; index++)
        {
            var correlation = split.Correlations[index];
            var partition = CreateColumn(
                GetPartitionAlias(index),
                rankedAlias,
                correlation.Inner,
                nullable: correlation.Inner.IsNullable);

            predicate = AndAlso(predicate, _sqlExpressionFactory.Equal(correlation.Outer, partition));
        }

        var rowNumber = new ColumnExpression("__row", rankedAlias, typeof(long), _longTypeMapping, nullable: false);
        var offset = _sqlExpressionFactory.Convert(selectExpression.Offset, typeof(long), _longTypeMapping);
        var requestedRow = _sqlExpressionFactory.Add(
            offset,
            _sqlExpressionFactory.Constant(1L, _longTypeMapping),
            _longTypeMapping);

        predicate = AndAlso(predicate, _sqlExpressionFactory.Equal(rowNumber, requestedRow));

#pragma warning disable EF1001 // The window rewrite must construct EF Core's internal SelectExpression shape.
        return new SelectExpression(
            selectExpression.Alias,
            [ranked],
            predicate,
            groupBy: [],
            having: null,
            outerProjections,
            distinct: false,
            orderings: [],
            offset: null,
            limit,
            _sqlAliasManager,
            tags: selectExpression.Tags.ToHashSet(StringComparer.Ordinal));
#pragma warning restore EF1001
    }

    private PredicateSplit? SplitPredicate(
        SqlExpression? predicate,
        HashSet<string> innerAliases
    )
    {
        var local = new List<SqlExpression>();
        var outer = new List<SqlExpression>();
        var correlations = new List<Correlation>();

        foreach (var clause in SplitConjunction(predicate))
        {
            var aliases = ColumnAliasCollector.Collect(clause);
            var containsInner = aliases.Overlaps(innerAliases);
            var containsOuter = aliases.Any(alias => !innerAliases.Contains(alias));

            if (!containsOuter)
            {
                local.Add(clause);
                continue;
            }

            if (!containsInner)
            {
                outer.Add(clause);
                continue;
            }

            if (!TryGetCorrelation(clause, innerAliases, out var correlation))
            {
                return null;
            }

            correlations.Add(correlation);
        }

        return new PredicateSplit(Combine(local), Combine(outer), correlations);
    }

    private static bool TryGetCorrelation(
        SqlExpression expression,
        HashSet<string> innerAliases,
        out Correlation correlation
    )
    {
        if (expression is SqlBinaryExpression
            {
                OperatorType: ExpressionType.Equal, Left: ColumnExpression left, Right: ColumnExpression right,
            })
        {
            var leftIsInner = innerAliases.Contains(left.TableAlias);
            var rightIsInner = innerAliases.Contains(right.TableAlias);

            if (leftIsInner != rightIsInner)
            {
                correlation = leftIsInner ? new Correlation(left, right) : new Correlation(right, left);
                return true;
            }
        }

        correlation = default;
        return false;
    }

    private static List<SqlExpression> SplitConjunction(
        SqlExpression? expression
    )
    {
        if (expression is null)
        {
            return [];
        }

        var clauses = new List<SqlExpression>();
        AddClauses(expression, clauses);
        return clauses;

        static void AddClauses(
            SqlExpression candidate,
            ICollection<SqlExpression> clauses
        )
        {
            if (candidate is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } conjunction)
            {
                AddClauses(conjunction.Left, clauses);
                AddClauses(conjunction.Right, clauses);
                return;
            }

            clauses.Add(candidate);
        }
    }

    private SqlExpression? Combine(
        IReadOnlyList<SqlExpression> expressions
    )
    {
        SqlExpression? combined = null;

        foreach (var expression in expressions)
        {
            combined = AndAlso(combined, expression);
        }

        return combined;
    }

    private SqlExpression AndAlso(
        SqlExpression? left,
        SqlExpression right
    ) => left is null ? right : _sqlExpressionFactory.AndAlso(left, right);

    private static string[] CreateProjectionAliases(
        IReadOnlyList<ProjectionExpression> projections
    )
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new string[projections.Count];

        for (var index = 0; index < projections.Count; index++)
        {
            var baseAlias = string.IsNullOrWhiteSpace(projections[index].Alias)
                ? $"value{index}"
                : projections[index].Alias;

            var alias = baseAlias;
            var suffix = 0;

            while (!used.Add(alias))
            {
                alias = $"{baseAlias}{suffix++}";
            }

            aliases[index] = alias;
        }

        return aliases;
    }

    private static ColumnExpression CreateColumn(
        string name,
        string tableAlias,
        SqlExpression source,
        bool nullable
    ) => new(name, tableAlias, source.Type, source.TypeMapping, nullable);

    private static bool IsNullable(
        SqlExpression expression
    ) => expression is not ColumnExpression column || column.IsNullable;

    private static string GetPartitionAlias(
        int index
    ) => $"__partition{index}";

    private readonly record struct Correlation(
        ColumnExpression Inner,
        ColumnExpression Outer
    );

    private sealed record PredicateSplit(
        SqlExpression? LocalPredicate,
        SqlExpression? OuterPredicate,
        IReadOnlyList<Correlation> Correlations
    );

    private sealed class ColumnAliasCollector : ExpressionVisitor
    {
        private readonly HashSet<string> _aliases = new(StringComparer.Ordinal);

        public static HashSet<string> Collect(
            Expression expression
        )
        {
            var collector = new ColumnAliasCollector();
            collector.Visit(expression);
            return collector._aliases;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ColumnExpression column)
            {
                _aliases.Add(column.TableAlias);
                return column;
            }

            return base.VisitExtension(node);
        }
    }
}
