namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Restores property representations after lossless <c>JSON_TABLE</c>
/// extraction from model-owned JSON documents.
/// </summary>
internal sealed class MySqlJsonTableDocumentDecodingExpressionVisitor : ExpressionVisitor
{
    private readonly IReadOnlyDictionary<JsonTableColumn, ValueDecodingMapping> _mappings;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    private MySqlJsonTableDocumentDecodingExpressionVisitor(
        IReadOnlyDictionary<JsonTableColumn, ValueDecodingMapping> mappings,
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _mappings = mappings;
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <summary>
    /// Rewrites projected document columns that require an explicit provider
    /// representation decoder.
    /// </summary>
    public static Expression Rewrite(
        Expression query,
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        var mappings = MappingCollector.Collect(query);

        return mappings.Count == 0
            ? query
            : new MySqlJsonTableDocumentDecodingExpressionVisitor(mappings, sqlExpressionFactory).Visit(query);
    }

    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is ShapedQueryExpression shapedQuery)
        {
            return shapedQuery.UpdateQueryExpression(Visit(shapedQuery.QueryExpression));
        }

        if (node is SelectExpression selectExpression)
        {
            // SelectExpression.VisitChildren also visits EF Core's private identifier
            // lists, whose entries must remain ColumnExpression instances. Rebuild the
            // finalized SQL surface explicitly so document decoders can replace columns
            // in projections and predicates without corrupting identity metadata.
            return selectExpression.Update(
                VisitExpressions(selectExpression.Tables),
                (SqlExpression?)Visit(selectExpression.Predicate),
                VisitExpressions(selectExpression.GroupBy),
                (SqlExpression?)Visit(selectExpression.Having),
                VisitExpressions(selectExpression.Projection),
                VisitExpressions(selectExpression.Orderings),
                (SqlExpression?)Visit(selectExpression.Offset),
                (SqlExpression?)Visit(selectExpression.Limit));
        }

        if (node is not ColumnExpression column
            || !_mappings.TryGetValue(new JsonTableColumn(column.TableAlias, column.Name), out var mapping))
        {
            return base.VisitExtension(node);
        }

        var extractionColumn = new ColumnExpression(
            column.Name,
            column.TableAlias,
            mapping.ExtractionTypeMapping.ClrType,
            mapping.ExtractionTypeMapping,
            column.IsNullable);

        return MySqlJsonTableValueEncoding.Decode(
            extractionColumn,
            mapping.ElementType,
            mapping.ResultTypeMapping,
            _sqlExpressionFactory);
    }

    private IReadOnlyList<TExpression> VisitExpressions<TExpression>(
        IReadOnlyList<TExpression> expressions
    ) where TExpression : Expression
    {
        List<TExpression>? rewritten = null;

        for (var index = 0; index < expressions.Count; index++)
        {
            var expression = expressions[index];
            var visited = (TExpression)Visit(expression);

            if (rewritten is null
                && visited != expression)
            {
                rewritten = [];
                rewritten.EnsureCapacity(expressions.Count);

                for (var previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    rewritten.Add(expressions[previousIndex]);
                }
            }

            rewritten?.Add(visited);
        }

        return rewritten ?? expressions;
    }

    private readonly record struct JsonTableColumn(
        string TableAlias,
        string ColumnName
    );

    private readonly record struct ValueDecodingMapping(
        Type ElementType,
        RelationalTypeMapping ExtractionTypeMapping,
        RelationalTypeMapping ResultTypeMapping
    );

    private sealed class MappingCollector : ExpressionVisitor
    {
        private readonly Dictionary<JsonTableColumn, ValueDecodingMapping> _mappings = [];

        public static Dictionary<JsonTableColumn, ValueDecodingMapping> Collect(
            Expression query
        )
        {
            var collector = new MappingCollector();
            collector.Visit(query);

            return collector._mappings;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ShapedQueryExpression shapedQuery)
            {
                Visit(shapedQuery.QueryExpression);

                return node;
            }

            if (node is MySqlJsonTableExpression { ColumnInfos: { Count: > 0 } columns } jsonTable)
            {
                foreach (var column in columns)
                {
                    if (column.ResultTypeMapping is not { } resultTypeMapping)
                    {
                        continue;
                    }

                    var elementType = resultTypeMapping.ClrType.UnwrapNullableType();

                    if (!MySqlJsonTableValueEncoding.RequiresDecoding(elementType, resultTypeMapping))
                    {
                        continue;
                    }

                    _mappings[new JsonTableColumn(jsonTable.Alias, column.Name)] = new ValueDecodingMapping(
                        elementType,
                        column.TypeMapping,
                        resultTypeMapping);
                }
            }

            return base.VisitExtension(node);
        }
    }
}
