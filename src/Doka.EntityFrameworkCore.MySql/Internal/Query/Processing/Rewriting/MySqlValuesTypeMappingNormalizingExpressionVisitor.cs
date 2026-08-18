namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Removes a default string mapping from an inline rowset when one outer IN operand
/// provides the only authoritative store type.
/// </summary>
/// <remarks>
/// EF Core can assign the provider's fallback string mapping to local collection
/// values before it sees the mapped column used by <c>IN</c>. Leaving both mappings
/// in the tree makes the standard inference pass treat them as conflicting. This
/// normalizer rewrites an alias only when every contextual use agrees on one
/// non-default store type and every rowset reference still carries only the
/// fallback. Ambiguous or mixed mappings remain untouched.
/// </remarks>
internal static class MySqlValuesTypeMappingNormalizingExpressionVisitor
{
    /// <summary>
    /// Normalizes eligible inline-rowset mappings before EF Core performs its standard
    /// type-inference pass.
    /// </summary>
    /// <param name="expression">The relational query tree.</param>
    /// <param name="typeMappingSource">The provider type-mapping source.</param>
    /// <returns>The original or normalized query tree.</returns>
    public static Expression Normalize(
        Expression expression,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(typeMappingSource);

        var defaultStringMapping = (RelationalTypeMapping?)typeMappingSource.FindMapping(typeof(string))
            ?? throw new InvalidOperationException("The provider must define a default string type mapping.");

        var valuesAliases = new ValuesAliasFindingVisitor().Find(expression);

        if (valuesAliases.Count == 0)
        {
            return expression;
        }

        var existingMappingsByAlias = new ValuesColumnMappingFindingVisitor(valuesAliases).Find(expression);
        var contextualMappingsByAlias = new InContextMappingFindingVisitor(valuesAliases).Find(expression);
        var aliasesToNormalize = contextualMappingsByAlias
            .Where(pair => pair.Value.Count == 1
                && !pair.Value.Contains(defaultStringMapping.StoreType)
                && existingMappingsByAlias.TryGetValue(pair.Key, out var existingMappings)
                && existingMappings.Count == 1
                && existingMappings.Contains(defaultStringMapping.StoreType))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        return aliasesToNormalize.Count == 0
            ? expression
            : new DefaultStringMappingClearingVisitor(aliasesToNormalize, defaultStringMapping.StoreType).Visit(
                expression);
    }

    private static int FindValueColumnIndex(
        IReadOnlyList<string> columnNames
    )
    {
        for (var index = 0; index < columnNames.Count; index++)
        {
            if (columnNames[index] == "Value")
            {
                return index;
            }
        }

        return -1;
    }

    private abstract class ShapedQueryTraversingExpressionVisitor
        : MySqlShapedQueryTraversingExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        )
        {
            switch (node)
            {
                case RelationalGroupByShaperExpression groupByShaperExpression:
                    {
                        var keySelector = Visit(groupByShaperExpression.KeySelector);
                        var elementSelector = Visit(groupByShaperExpression.ElementSelector);
                        var groupingEnumerable = (ShapedQueryExpression)Visit(
                            groupByShaperExpression.GroupingEnumerable);

                        return keySelector == groupByShaperExpression.KeySelector
                            && elementSelector == groupByShaperExpression.ElementSelector
                            && groupingEnumerable == groupByShaperExpression.GroupingEnumerable
                                ? groupByShaperExpression
                                : new RelationalGroupByShaperExpression(
                                    keySelector,
                                    elementSelector,
                                    groupingEnumerable);
                    }
                default:
                    return base.VisitExtension(node);
            }
        }
    }

    private sealed class ValuesAliasFindingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly HashSet<string> _aliases = new(StringComparer.Ordinal);

        public HashSet<string> Find(
            Expression expression
        )
        {
            Visit(expression);

            return _aliases;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ValuesExpression { Alias: { } alias } valuesExpression
                && FindValueColumnIndex(valuesExpression.ColumnNames) >= 0)
            {
                _aliases.Add(alias);
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class ValuesColumnMappingFindingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly IReadOnlySet<string> _aliases;
        private readonly Dictionary<string, HashSet<string>> _storeTypesByAlias = new(StringComparer.Ordinal);

        public ValuesColumnMappingFindingVisitor(
            IReadOnlySet<string> aliases
        )
        {
            _aliases = aliases;
        }

        public Dictionary<string, HashSet<string>> Find(
            Expression expression
        )
        {
            Visit(expression);

            return _storeTypesByAlias;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ColumnExpression
                {
                    Name: "Value", Type: { } type, TypeMapping: { } typeMapping,
                } columnExpression
                && type == typeof(string)
                && typeMapping.ClrType == typeof(string)
                && _aliases.Contains(columnExpression.TableAlias))
            {
                if (!_storeTypesByAlias.TryGetValue(columnExpression.TableAlias, out var storeTypes))
                {
                    storeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _storeTypesByAlias.Add(columnExpression.TableAlias, storeTypes);
                }

                storeTypes.Add(typeMapping.StoreType);
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class InContextMappingFindingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly IReadOnlySet<string> _aliases;
        private readonly Dictionary<string, HashSet<string>> _storeTypesByAlias = new(StringComparer.Ordinal);

        public InContextMappingFindingVisitor(
            IReadOnlySet<string> aliases
        )
        {
            _aliases = aliases;
        }

        public Dictionary<string, HashSet<string>> Find(
            Expression expression
        )
        {
            Visit(expression);

            return _storeTypesByAlias;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is InExpression
                {
                    Item.TypeMapping: { } itemTypeMapping,
                    Subquery.Projection:
                    [
                    { Expression: ColumnExpression { Name: "Value", Type: { } type, } valueColumn, },
                    ],
                }
                && type == typeof(string)
                && itemTypeMapping.ClrType == typeof(string)
                && _aliases.Contains(valueColumn.TableAlias))
            {
                if (!_storeTypesByAlias.TryGetValue(valueColumn.TableAlias, out var storeTypes))
                {
                    storeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _storeTypesByAlias.Add(valueColumn.TableAlias, storeTypes);
                }

                storeTypes.Add(itemTypeMapping.StoreType);
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class DefaultStringMappingClearingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly IReadOnlySet<string> _aliases;
        private readonly string _defaultStoreType;

        public DefaultStringMappingClearingVisitor(
            IReadOnlySet<string> aliases,
            string defaultStoreType
        )
        {
            _aliases = aliases;
            _defaultStoreType = defaultStoreType;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ColumnExpression
                {
                    Name: "Value", Type: { } type, TypeMapping: { } typeMapping,
                } columnExpression
                && type == typeof(string)
                && _aliases.Contains(columnExpression.TableAlias)
                && IsDefaultStringMapping(typeMapping))
            {
                return columnExpression.ApplyTypeMapping(null);
            }

            if (node is ValuesExpression { Alias: { } alias, RowValues: { } rows, } valuesExpression
                && _aliases.Contains(alias)
                && FindValueColumnIndex(valuesExpression.ColumnNames) is var valueIndex and >= 0)
            {
                var rewrittenRows = new RowValueExpression[rows.Count];

                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var values = row.Values.ToArray();

                    values[valueIndex] = ClearDefaultStringMapping(values[valueIndex]);
                    rewrittenRows[rowIndex] = new RowValueExpression(values);
                }

                return valuesExpression.Update(rewrittenRows);
            }

            if (node is ValuesExpression
                {
                    Alias: { } parameterAlias,
                    ValuesParameter:
                    {
                        TypeMapping.ElementTypeMapping: RelationalTypeMapping elementTypeMapping,
                    } valuesParameter,
                } parameterValuesExpression
                && _aliases.Contains(parameterAlias)
                && IsDefaultStringMapping(elementTypeMapping))
            {
                return parameterValuesExpression.Update(valuesParameter.ApplyTypeMapping(null));
            }

            return base.VisitExtension(node);
        }

        private SqlExpression ClearDefaultStringMapping(
            SqlExpression expression
        )
        {
            if (expression.Type != typeof(string)
                || expression.TypeMapping is not { } typeMapping
                || !IsDefaultStringMapping(typeMapping))
            {
                return expression;
            }

            return expression switch
            {
                SqlConstantExpression constantExpression => constantExpression.ApplyTypeMapping(null),
                SqlParameterExpression parameterExpression => parameterExpression.ApplyTypeMapping(null),
                _ => expression,
            };
        }

        private bool IsDefaultStringMapping(
            RelationalTypeMapping typeMapping
        ) => typeMapping.ClrType == typeof(string)
            && typeMapping.StoreType.Equals(_defaultStoreType, StringComparison.OrdinalIgnoreCase);
    }
}
