namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Removes unambiguous fallback string mappings before relational collection inference.
/// </summary>
/// <remarks>
/// EF Core can assign the provider's fallback string mapping to local collection
/// values before it sees the mapped column used by <c>IN</c> or equality. Leaving
/// both mappings in the tree makes the standard inference pass treat them as
/// conflicting. Inline rowsets retain their existing store-type policy. Untyped
/// string JSON parameters require full mapping consensus from typed consumers;
/// references may carry only the fallback and that authoritative mapping.
/// Converted, ambiguous and explicitly different JSON mappings remain untouched.
/// </remarks>
internal static class MySqlValuesTypeMappingNormalizingExpressionVisitor
{
    /// <summary>
    /// Normalizes eligible local-rowset mappings before EF Core performs its standard
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

        var aliasFinder = new ValuesAliasFindingVisitor(defaultStringMapping);
        var valuesAliases = aliasFinder.Find(expression);
        var jsonParameters = aliasFinder.JsonParameters;

        if (valuesAliases.Count == 0 && jsonParameters is null)
        {
            return expression;
        }

        var columnFinder = new ValuesColumnMappingFindingVisitor(valuesAliases, jsonParameters);
        var existingMappingsByAlias = columnFinder.Find(expression);

        var contextualMappingsByAlias = new InContextMappingFindingVisitor(
                valuesAliases,
                jsonParameters,
                columnFinder.ConsumerAliases)
            .Find(expression);
        var aliasesToNormalize = contextualMappingsByAlias
            .Where(pair => pair.Value.Count == 1
                && !pair.Value.Contains(defaultStringMapping.StoreType)
                && existingMappingsByAlias.TryGetValue(pair.Key, out var existingMappings)
                && existingMappings.Count == 1
                && existingMappings.Contains(defaultStringMapping.StoreType))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (jsonParameters is not null)
        {
            foreach (var (alias, mapping) in jsonParameters)
            {
                if (mapping.CanNormalize)
                {
                    aliasesToNormalize.Add(alias);
                }
            }
        }

        return aliasesToNormalize.Count == 0
            ? expression
            : new DefaultStringMappingClearingVisitor(
                aliasesToNormalize,
                defaultStringMapping.StoreType,
                jsonParameters).Visit(
                expression);
    }

    /// <summary>Tracks only converter-free string inference for one untyped parameter rowset.</summary>
    private sealed class JsonParameterMapping(RelationalTypeMapping fallbackMapping)
    {
        private RelationalTypeMapping? _contextMapping;
        private RelationalTypeMapping? _existingMapping;
        private bool _hasFallback;
        private bool _hasConflict;

        /// <summary>Whether every observation agrees with one converter-free consumer mapping.</summary>
        public bool CanNormalize => _hasFallback
            && !_hasConflict
            && _contextMapping is not null
            && !Equivalent(_contextMapping, fallbackMapping)
            && (_existingMapping is null || Equivalent(_existingMapping, _contextMapping));

        /// <summary>Recognizes the unmodified fallback contract, including equivalent plain clones.</summary>
        public bool IsFallback(RelationalTypeMapping mapping) => mapping.ClrType == typeof(string)
            && mapping.Converter is null
            && mapping.ElementTypeMapping is null
            && Equivalent(mapping, fallbackMapping);

        /// <summary>Keeps a rejected typed operand from being overridden by another valid context.</summary>
        public void RejectContext() => _hasConflict = true;

        /// <summary>Records raw rowset references without accepting a third incompatible mapping.</summary>
        public void ObserveExisting(RelationalTypeMapping mapping)
        {
            if (IsFallback(mapping))
            {
                _hasFallback = true;

                return;
            }

            if (_existingMapping is not null && !Equivalent(_existingMapping, mapping))
            {
                _hasConflict = true;
            }

            _existingMapping ??= mapping;
        }

        /// <summary>Records a typed consumer comparison and rejects ambiguous or converted semantics.</summary>
        public void ObserveContext(RelationalTypeMapping mapping)
        {
            // WHY: Arbitrary converters do not commute with CLR string predicates.
            // A prefix converter, for example, changes Length before comparison.
            if (mapping.Converter is not null
                || mapping.ElementTypeMapping is not null
                || mapping.ClrType != typeof(string)
                || (_contextMapping is not null && !Equivalent(_contextMapping, mapping)))
            {
                _hasConflict = true;
            }

            _contextMapping ??= mapping;
        }

        private static bool Equivalent(RelationalTypeMapping left, RelationalTypeMapping right)
        {
            // WHY: Store-type equality alone would erase converters, comparers or
            // facets. Only plain-string clones with the same complete contract agree.
            return ReferenceEquals(left, right)
                || (left.GetType() == right.GetType()
                    && left.ClrType == typeof(string)
                    && right.ClrType == typeof(string)
                    && left.Converter is null && right.Converter is null
                    && left.ElementTypeMapping is null && right.ElementTypeMapping is null
                    && left.StoreType == right.StoreType
                    && left.StoreTypePostfix == right.StoreTypePostfix
                    && left.DbType == right.DbType
                    && left.IsUnicode == right.IsUnicode
                    && left.IsFixedLength == right.IsFixedLength
                    && left.Size == right.Size
                    && left.Precision == right.Precision
                    && left.Scale == right.Scale
                    && EquivalentComparer(left.Comparer, right.Comparer)
                    && EquivalentComparer(left.KeyComparer, right.KeyComparer)
                    && EquivalentComparer(left.ProviderValueComparer, right.ProviderValueComparer)
                    && ReferenceEquals(left.JsonValueReaderWriter, right.JsonValueReaderWriter));
        }

        private static bool EquivalentComparer(ValueComparer left, ValueComparer right)
        {
            // WHY: EF lazily creates distinct default comparers for mapping clones.
            // Its public predicate identifies that exact implementation; custom
            // comparers still require identity, even when their store types agree.
            return ReferenceEquals(left, right)
                || (left.Type == typeof(string) && right.Type == typeof(string)
                    && left.IsDefault() && right.IsDefault());
        }
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

    private sealed class ValuesAliasFindingVisitor(RelationalTypeMapping fallbackMapping)
        : ShapedQueryTraversingExpressionVisitor
    {
        private readonly HashSet<string> _aliases = new(StringComparer.Ordinal);

        public Dictionary<string, JsonParameterMapping>? JsonParameters { get; private set; }

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

            if (node is MySqlJsonTableExpression
                {
                    JsonExpression: SqlParameterExpression { TypeMapping: null } parameter,
                    Path: null or { Count: 0 },
                } jsonTable
                && typeof(IEnumerable<string>).IsAssignableFrom(parameter.Type)
                && (jsonTable.ColumnInfos is null || jsonTable.ColumnInfos.All(column => column.Name != "value")))
            {
                // WHY: Only deferred parameter sources can acquire a fallback while
                // composing. Typed documents and row-dependent paths own their mappings.
                JsonParameters ??= new Dictionary<string, JsonParameterMapping>(StringComparer.Ordinal);
                JsonParameters.TryAdd(jsonTable.Alias, new JsonParameterMapping(fallbackMapping));
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class ValuesColumnMappingFindingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly IReadOnlySet<string> _aliases;
        private readonly IReadOnlyDictionary<string, JsonParameterMapping>? _jsonParameters;
        private readonly Dictionary<string, HashSet<string>> _storeTypesByAlias = new(StringComparer.Ordinal);

        public ValuesColumnMappingFindingVisitor(
            IReadOnlySet<string> aliases,
            IReadOnlyDictionary<string, JsonParameterMapping>? jsonParameters
        )
        {
            _aliases = aliases;
            _jsonParameters = jsonParameters;
        }

        public HashSet<string>? ConsumerAliases { get; private set; }

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
            if (_jsonParameters is not null
                && node is TableExpressionBase { Alias: { } consumerAlias }
                && node is not (MySqlJsonTableExpression or ValuesExpression))
            {
                // WHY: Typed consumer columns keep their mapping after EF pushes
                // pagination into a derived table. Local JSON/VALUES rowsets must
                // not infer an authoritative mapping from one another.
                ConsumerAliases ??= new HashSet<string>(StringComparer.Ordinal);
                ConsumerAliases.Add(consumerAlias);
            }

            if (_jsonParameters is not null
                && node is ColumnExpression { Name: "value", TypeMapping: { } jsonMapping } jsonColumn
                && jsonColumn.Type == typeof(string)
                && _jsonParameters.TryGetValue(jsonColumn.TableAlias, out var parameterMapping))
            {
                parameterMapping.ObserveExisting(jsonMapping);
            }

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
        private readonly IReadOnlyDictionary<string, JsonParameterMapping>? _jsonParameters;
        private readonly IReadOnlySet<string>? _consumerAliases;
        private readonly Dictionary<string, HashSet<string>> _storeTypesByAlias = new(StringComparer.Ordinal);
        private ConsumerExpressionEligibilityVisitor? _consumerEligibility;

        public InContextMappingFindingVisitor(
            IReadOnlySet<string> aliases,
            IReadOnlyDictionary<string, JsonParameterMapping>? jsonParameters,
            IReadOnlySet<string>? consumerAliases
        )
        {
            _aliases = aliases;
            _jsonParameters = jsonParameters;
            _consumerAliases = consumerAliases;
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
            switch (node)
            {
                case InExpression
                {
                    Item: var consumer,
                    Subquery.Projection: [{ Expression: ColumnExpression jsonValueColumn }],
                }:
                    ObserveJsonContext(jsonValueColumn, consumer);
                    break;

                case SqlBinaryExpression { OperatorType: ExpressionType.Equal } comparison:
                    if (comparison.Left is ColumnExpression left)
                    {
                        ObserveJsonContext(left, comparison.Right);
                    }

                    if (comparison.Right is ColumnExpression right)
                    {
                        ObserveJsonContext(right, comparison.Left);
                    }

                    break;
            }

            if (node is InExpression
                {
                    Item.TypeMapping: { } itemTypeMapping,
                    Subquery.Projection:
                    [
                        { Expression: ColumnExpression { Name: "Value", Type: { } type } valueColumn },
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

        private void ObserveJsonContext(ColumnExpression value, SqlExpression consumer)
        {
            if (_jsonParameters is not null
                && value.Name == "value"
                && value.Type == typeof(string)
                && _jsonParameters.TryGetValue(value.TableAlias, out var mapping)
                && consumer.TypeMapping is { } consumerMapping)
            {
                if (consumerMapping.ClrType != typeof(string)
                    || consumerMapping.Converter is not null
                    || consumerMapping.ElementTypeMapping is not null)
                {
                    mapping.RejectContext();

                    return;
                }

                if (consumer is ColumnExpression column)
                {
                    if (_consumerAliases?.Contains(column.TableAlias) != true)
                    {
                        mapping.RejectContext();

                        return;
                    }
                }
                else if (!(_consumerEligibility ??= new ConsumerExpressionEligibilityVisitor(_consumerAliases))
                    .IsEligible(consumer))
                {
                    mapping.RejectContext();

                    return;
                }

                if (consumer is SqlConstantExpression { Value: null }
                    || (consumer is SqlConstantExpression or SqlParameterExpression
                        && mapping.IsFallback(consumerMapping)))
                {
                    // WHY: NULL does not constrain a store type. Default literal
                    // and scalar-parameter mappings are generic string inference,
                    // not independent storage contracts competing with a consumer.
                    return;
                }

                // WHY: A typed function, collation or scalar owns its result
                // mapping. Validate its references, not a guessed child mapping.
                mapping.ObserveContext(consumerMapping);
            }
        }
    }

    /// <summary>Checks one scalar operand without substituting nodes or inferring projection lineage.</summary>
    private sealed class ConsumerExpressionEligibilityVisitor(IReadOnlySet<string>? consumerAliases)
        : ExpressionVisitor
    {
        private bool _eligible;

        /// <summary>Rejects hidden string conversions and unowned local-rowset references.</summary>
        public bool IsEligible(SqlExpression expression)
        {
            _eligible = true;
            Visit(expression);

            return _eligible;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (!_eligible)
            {
                return node;
            }

            if (node is SqlExpression { Type: { } type, TypeMapping: { } mapping }
                && type == typeof(string)
                && (mapping.Converter is not null || mapping.ElementTypeMapping is not null))
            {
                // WHY: An outer wrapper can drop the converter from its result
                // mapping; its operands must not hide non-commuting semantics.
                _eligible = false;

                return node;
            }

            if (node is ColumnExpression column
                && (column.TypeMapping is null || consumerAliases?.Contains(column.TableAlias) != true))
            {
                _eligible = false;

                return node;
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class DefaultStringMappingClearingVisitor : ShapedQueryTraversingExpressionVisitor
    {
        private readonly IReadOnlySet<string> _aliases;
        private readonly string _defaultStoreType;
        private readonly IReadOnlyDictionary<string, JsonParameterMapping>? _jsonParameters;

        public DefaultStringMappingClearingVisitor(
            IReadOnlySet<string> aliases,
            string defaultStoreType,
            IReadOnlyDictionary<string, JsonParameterMapping>? jsonParameters
        )
        {
            _aliases = aliases;
            _defaultStoreType = defaultStoreType;
            _jsonParameters = jsonParameters;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (_jsonParameters is not null
                && node is ColumnExpression { Name: "value", TypeMapping: { } jsonMapping } jsonColumn
                && jsonColumn.Type == typeof(string)
                && _aliases.Contains(jsonColumn.TableAlias)
                && _jsonParameters.TryGetValue(jsonColumn.TableAlias, out var parameterMapping)
                && parameterMapping.IsFallback(jsonMapping))
            {
                // WHY: Decode later from unbounded transport, after inference sees
                // the model mapping. Clearing here must never narrow Base64 extraction.
                return jsonColumn.ApplyTypeMapping(null);
            }

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

            if (node is ValuesExpression { Alias: { } alias, RowValues: { } rows } valuesExpression
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
