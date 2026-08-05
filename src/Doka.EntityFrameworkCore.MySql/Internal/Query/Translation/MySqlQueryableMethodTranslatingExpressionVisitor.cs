using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL / MariaDB queryable-method translator. Overrides the two hook points that turn a JSON
/// source into a relational rowset (<see cref="TranslatePrimitiveCollection"/> for raw / column
/// primitive collections, <see cref="TransformJsonQueryToTable"/> for <c>ToJson()</c>-owned
/// entity collections) so the cross-engine JSON_TABLE grammar gets emitted in place of the
/// "no LINQ translation" fallback the base provider would otherwise hit.
/// </summary>
internal sealed class
    MySqlQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    private const string MySqlValuesOrderingColumnName = "_ord";
    private const string MySqlValuesValueColumnName = "Value";

    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalQueryCompilationContext _queryCompilationContext;

    public MySqlQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _typeMappingSource = relationalDependencies.TypeMappingSource;
        _sqlExpressionFactory = relationalDependencies.SqlExpressionFactory;
        _queryCompilationContext = queryCompilationContext;
    }

    private MySqlQueryableMethodTranslatingExpressionVisitor(
        MySqlQueryableMethodTranslatingExpressionVisitor parentVisitor
    ) : base(parentVisitor)
    {
        _typeMappingSource = parentVisitor._typeMappingSource;
        _sqlExpressionFactory = parentVisitor._sqlExpressionFactory;
        _queryCompilationContext = parentVisitor._queryCompilationContext;
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() =>
        new MySqlQueryableMethodTranslatingExpressionVisitor(this);

    /// <summary>
    /// Converts the public temporal query root into an ordinary relational select whose
    /// table expressions carry the complete temporal operation. Deferring engine-specific
    /// syntax to SQL generation keeps translation independent of the configured server family.
    /// </summary>
    protected override Expression VisitExtension(
        Expression extensionExpression
    )
    {
        if (extensionExpression is not MySqlTemporalQueryRootExpression temporalQueryRoot)
        {
            return base.VisitExtension(extensionExpression);
        }

        var entityType = temporalQueryRoot.EntityType;
        var temporalEntityType = entityType.IsMySqlTemporal() ? entityType : entityType.GetRootType();

        if (!temporalEntityType.IsMySqlTemporal())
        {
            throw new InvalidOperationException(
                $"Temporal query operation '{temporalQueryRoot.Operation}' cannot be applied "
                + $"to non-temporal entity type '{entityType.DisplayName()}'.");
        }

        var tableName = temporalEntityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Temporal entity type '{temporalEntityType.DisplayName()}' is not mapped to a table.");

        var storeObject = StoreObjectIdentifier.Table(tableName, temporalEntityType.GetSchema());
        var periodStartProperty = GetTemporalPeriodProperty(
            temporalEntityType,
            temporalEntityType.GetMySqlTemporalPeriodStartPropertyName(),
            "start");

        var periodEndProperty = GetTemporalPeriodProperty(
            temporalEntityType,
            temporalEntityType.GetMySqlTemporalPeriodEndPropertyName(),
            "end");

        var periodStartColumn = periodStartProperty.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"Temporal period-start property '{periodStartProperty.Name}' has no table column mapping.");

        var periodEndColumn = periodEndProperty.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"Temporal period-end property '{periodEndProperty.Name}' has no table column mapping.");

        var selectExpression = CreateSelect(entityType);
        selectExpression = (SelectExpression)new TemporalAnnotationApplyingExpressionVisitor(
            temporalQueryRoot,
            temporalEntityType.GetMySqlTemporalHistoryTableName(),
            temporalEntityType.GetMySqlTemporalHistoryTableSchema(),
            periodStartColumn,
            periodEndColumn).Visit(selectExpression);

        return new ShapedQueryExpression(
            selectExpression,
            new RelationalStructuralTypeShaperExpression(
                entityType,
                new ProjectionBindingExpression(selectExpression, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

    private static IReadOnlyProperty GetTemporalPeriodProperty(
        IReadOnlyEntityType entityType,
        string? propertyName,
        string boundaryName
    )
    {
        if (propertyName is null
            || entityType.FindProperty(propertyName) is not { } property)
        {
            throw new InvalidOperationException(
                $"Temporal entity type '{entityType.DisplayName()}' has no valid period-{boundaryName} property.");
        }

        return property;
    }

    /// <summary>
    /// Applies temporal metadata to every physical table in the generated select. This
    /// mirrors EF Core's provider contract for table-sharing and inheritance query shapes.
    /// </summary>
    private sealed class TemporalAnnotationApplyingExpressionVisitor : ExpressionVisitor
    {
        private readonly MySqlTemporalQueryRootExpression _queryRoot;
        private readonly string? _historyTableName;
        private readonly string? _historyTableSchema;
        private readonly string _periodStartColumn;
        private readonly string _periodEndColumn;

        public TemporalAnnotationApplyingExpressionVisitor(
            MySqlTemporalQueryRootExpression queryRoot,
            string? historyTableName,
            string? historyTableSchema,
            string periodStartColumn,
            string periodEndColumn
        )
        {
            _queryRoot = queryRoot;
            _historyTableName = historyTableName;
            _historyTableSchema = historyTableSchema;
            _periodStartColumn = periodStartColumn;
            _periodEndColumn = periodEndColumn;
        }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not TableExpression tableExpression)
            {
                return base.VisitExtension(node);
            }

            var annotatedTable = tableExpression
                .AddAnnotation(MySqlAnnotationNames.TemporalOperation, _queryRoot.Operation)
                .AddAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn, _periodStartColumn)
                .AddAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn, _periodEndColumn);

            if (_historyTableName is not null)
            {
                annotatedTable = annotatedTable.AddAnnotation(
                    MySqlAnnotationNames.TemporalHistoryTable,
                    _historyTableName);
            }

            if (_historyTableSchema is not null)
            {
                annotatedTable = annotatedTable.AddAnnotation(
                    MySqlAnnotationNames.TemporalHistorySchema,
                    _historyTableSchema);
            }

            if (_queryRoot.PointInTime is { } pointInTime)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalPointInTime, pointInTime);
            }

            if (_queryRoot.From is { } from)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalRangeStart, from);
            }

            if (_queryRoot.To is { } to)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalRangeEnd, to);
            }

            return annotatedTable;
        }
    }

    /// <summary>
    /// Keeps join-based query shapes as native multi-table deletes. Falling back to
    /// EF Core's key-subquery rewrite would make MySQL read the target table from a
    /// nested query and trigger error 1093 even though the native delete grammar can
    /// express the operation directly.
    /// </summary>
    protected override bool IsValidSelectExpressionForExecuteDelete(
        SelectExpression selectExpression
    ) => selectExpression.Offset is null
        && selectExpression.GroupBy.Count == 0
        && selectExpression.Having is null
        && (selectExpression.Tables.Count == 1
            || (selectExpression.Orderings.Count == 0 && selectExpression.Limit is null));

    /// <summary>
    /// Accepts MySQL's native join-based update shape and single-table
    /// <c>LIMIT</c>. Offsets and ordered multi-table updates still use EF Core's
    /// primary-key join rewrite.
    /// </summary>
    protected override bool IsValidSelectExpressionForExecuteUpdate(
        SelectExpression selectExpression,
        TableExpressionBase targetTable,
        [NotNullWhen(true)] out TableExpression? tableExpression
    )
    {
        tableExpression = null;

        if (selectExpression.Offset is not null
            || selectExpression.IsDistinct
            || selectExpression.GroupBy.Count > 0
            || selectExpression.Having is not null
            || selectExpression.Orderings.Count > 0
            || selectExpression.Tables.Count == 0
            || (selectExpression.Tables.Count > 1 && selectExpression.Limit is not null))
        {
            return false;
        }

        if (targetTable is JoinExpressionBase join)
        {
            targetTable = join.Table;
        }

        tableExpression = targetTable as TableExpression;
        return tableExpression is not null;
    }

    /// <summary>
    /// Collapses indexing into a naturally ordered <c>JSON_TABLE</c> rowset back
    /// into one <see cref="JsonScalarExpression"/>. EF Core's ExecuteUpdate setter
    /// recognizer requires that scalar path form for updates such as
    /// <c>SetProperty(entity => entity.Values.ElementAt(1), value)</c>.
    /// </summary>
    protected override ShapedQueryExpression? TranslateElementAtOrDefault(
        ShapedQueryExpression source,
        Expression index,
        bool returnDefault
    )
    {
        if (returnDefault
            || TranslateExpression(index) is not { } translatedIndex
            || source.QueryExpression is not SelectExpression selectExpression
            || !TryGetElementProjection(source, selectExpression, out var projection, out var projectionColumn))
        {
            return base.TranslateElementAtOrDefault(source, index, returnDefault);
        }

        switch (selectExpression)
        {
            case
            {
                Tables: [MySqlJsonTableExpression jsonTable],
                Predicate: null,
                GroupBy: [],
                Having: null,
                IsDistinct: false,
                Orderings: [{ IsAscending: true, Expression: ColumnExpression orderingColumn }],
                Limit: null,
                Offset: null,
            }
                when orderingColumn.TableAlias == jsonTable.Alias
                && orderingColumn.Name == GetOrdinalityColumnName(jsonTable):
                {
                    var path = jsonTable.Path?.ToList() ?? [];
                    var json = jsonTable.JsonExpression;

                    if (json is JsonScalarExpression innerScalar)
                    {
                        json = innerScalar.Json;
                        path.InsertRange(0, innerScalar.Path);
                    }

                    path.Add(new PathSegment(translatedIndex));

                    return UpdateElementQuery(
                        source,
                        new JsonScalarExpression(
                            json,
                            path,
                            projection.Type,
                            projection.TypeMapping,
                            projectionColumn.IsNullable));
                }
            case
            {
                Tables:
                [
                    ValuesExpression
                {
                    ColumnNames: [MySqlValuesOrderingColumnName, MySqlValuesValueColumnName,],
                    ValuesParameter: { } parameter,
                },
                ],
                Predicate: null,
                GroupBy: [],
                Having: null,
                IsDistinct: false,
                Orderings:
                [
                { IsAscending: true, Expression: ColumnExpression { Name: MySqlValuesOrderingColumnName, }, },
                ],
                Limit: null,
                Offset: null,
            }:
                {
                    var elementMapping = projection.TypeMapping ?? _typeMappingSource.FindMapping(projection.Type);
                    var collectionMapping = parameter.TypeMapping
                        ?? _typeMappingSource.FindMapping(parameter.Type, _queryCompilationContext.Model, elementMapping);
                    var json = collectionMapping is null ? parameter : parameter.ApplyTypeMapping(collectionMapping);

                    return UpdateElementQuery(
                        source,
                        new JsonScalarExpression(
                            json,
                            [new PathSegment(translatedIndex)],
                            projection.Type,
                            elementMapping,
                            projectionColumn.IsNullable));
                }
            case
            {
                Tables:
                [
                    ValuesExpression
                {
                    ColumnNames: [MySqlValuesOrderingColumnName, MySqlValuesValueColumnName,], RowValues: { } rows,
                },
                ],
                Predicate: null,
                GroupBy: [],
                Having: null,
                IsDistinct: false,
                Orderings:
                [
                { IsAscending: true, Expression: ColumnExpression { Name: MySqlValuesOrderingColumnName, }, },
                ],
                Limit: null,
                Offset: null,
            }:
                {
                    var whenClauses = rows
                        .Select(row => new CaseWhenClause(
                            _sqlExpressionFactory.Equal(translatedIndex, row.Values[0]),
                            row.Values[1]))
                        .ToArray();
                    var fallback = _sqlExpressionFactory.Constant(null, projection.Type, projection.TypeMapping);

                    return UpdateElementQuery(source, _sqlExpressionFactory.Case(whenClauses, fallback));
                }
            default:
                return base.TranslateElementAtOrDefault(source, index, returnDefault);
        }
    }

    private static bool TryGetElementProjection(
        ShapedQueryExpression source,
        SelectExpression selectExpression,
        [NotNullWhen(true)] out SqlExpression? projection,
        [NotNullWhen(true)] out ColumnExpression? projectionColumn
    )
    {
        var shaper = source.ShaperExpression;
        if (shaper is UnaryExpression { NodeType: ExpressionType.Convert } conversion
            && Nullable.GetUnderlyingType(conversion.Operand.Type) == conversion.Type)
        {
            shaper = conversion.Operand;
        }

        if (shaper is not ProjectionBindingExpression projectionBinding
            || selectExpression.GetProjection(projectionBinding) is not SqlExpression sqlProjection)
        {
            projection = null;
            projectionColumn = null;
            return false;
        }

        projectionColumn = sqlProjection switch
        {
            ColumnExpression column => column,
            SqlUnaryExpression { OperatorType: ExpressionType.Convert, Operand: ColumnExpression column, } => column,
            _ => null,
        };
        projection = projectionColumn is null ? null : sqlProjection;

        return projection is not null;
    }

    private ShapedQueryExpression UpdateElementQuery(
        ShapedQueryExpression source,
        SqlExpression element
    )
    {
        // EF's JSON setter recognizer requires this provider-constructed internal scalar SelectExpression.
#pragma warning disable EF1001
        var scalarSelect = new SelectExpression(element, _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001

        return source.UpdateQueryExpression(scalarSelect);
    }

    /// <summary>
    /// Serializes a relational scalar expression through the engines' JSON
    /// constructor. This covers column-to-JSON updates for provider types whose
    /// JSON representation differs from their relational representation, such as
    /// temporal, binary and GUID values.
    /// </summary>
    protected override bool TrySerializeScalarToJson(
        JsonScalarExpression target,
        SqlExpression value,
        [NotNullWhen(true)] out SqlExpression? jsonValue
    )
    {
#pragma warning disable EF9002 // The provider override must compose EF Core's experimental JSON scalar serializer.
        if (base.TrySerializeScalarToJson(target, value, out jsonValue))
        {
            return true;
        }
#pragma warning restore EF9002

        var jsonTypeMapping = target.Json.TypeMapping;
        var valueForJsonObject = NormalizeRelationalScalarForJson(target.Type, value);
        var jsonObject = _sqlExpressionFactory.Function(
            "JSON_OBJECT",
            [
                _sqlExpressionFactory.Constant("value"),
                valueForJsonObject,
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                false,
                true,
            ],
            typeof(string),
            jsonTypeMapping);

        jsonValue = _sqlExpressionFactory.Function(
            "JSON_EXTRACT",
            [
                jsonObject,
                _sqlExpressionFactory.Constant("$.value"),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
            ],
            typeof(string),
            jsonTypeMapping);

        return true;
    }

    private SqlExpression NormalizeRelationalScalarForJson(
        Type targetType,
        SqlExpression value
    )
    {
        var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var stringTypeMapping = _typeMappingSource.FindMapping(typeof(string));

        if (nonNullableTargetType == typeof(DateTime))
        {
            return _sqlExpressionFactory.Function(
                "DATE_FORMAT",
                [
                    value,
                    _sqlExpressionFactory.Constant("%Y-%m-%dT%H:%i:%s.%f", stringTypeMapping),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                ],
                typeof(string),
                stringTypeMapping);
        }

        if (nonNullableTargetType != typeof(byte[]))
        {
            return nonNullableTargetType == typeof(Guid)
                && value.TypeMapping?.StoreType.StartsWith("binary", StringComparison.OrdinalIgnoreCase) == true
                    ? FormatBinaryGuid(value, stringTypeMapping)
                    : value;
        }

        var base64 = _sqlExpressionFactory.Function(
            "TO_BASE64",
            [value],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(string),
            stringTypeMapping);

        return _sqlExpressionFactory.Function(
            "REPLACE",
            [
                base64,
                _sqlExpressionFactory.Constant("\n", stringTypeMapping),
                _sqlExpressionFactory.Constant(string.Empty, stringTypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string),
            stringTypeMapping);
    }

    private SqlExpression FormatBinaryGuid(
        SqlExpression value,
        RelationalTypeMapping? stringTypeMapping
    )
    {
        var formatted = _sqlExpressionFactory.Function(
            "LOWER",
            [
                _sqlExpressionFactory.Function(
                    "HEX",
                    [value],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(string),
                    stringTypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(string),
            stringTypeMapping);

        foreach (var position in (ReadOnlySpan<int>)[9, 14, 19, 24])
        {
            formatted = _sqlExpressionFactory.Function(
                "INSERT",
                [
                    formatted,
                    _sqlExpressionFactory.Constant(position),
                    _sqlExpressionFactory.Constant(0),
                    _sqlExpressionFactory.Constant("-", stringTypeMapping),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                    false,
                    false
                ],
                typeof(string),
                stringTypeMapping);
        }

        return formatted;
    }

    /// <summary>
    /// Composes partial JSON ExecuteUpdate setters as nested <c>JSON_SET</c> calls.
    /// Objects and collections are parsed as JSON fragments. Boolean SQL values are
    /// parsed explicitly as JSON booleans because both engines otherwise store their
    /// numeric <c>0</c>/<c>1</c> representation.
    /// </summary>
    protected override SqlExpression? GenerateJsonPartialUpdateSetter(
        Expression target,
        SqlExpression value,
        ref SqlExpression? existingSetterValue
    )
    {
        var (jsonColumn, path, isJsonScalar) = target switch
        {
            JsonScalarExpression { TypeMapping.ElementTypeMapping: null } scalar =>
                ((ColumnExpression)scalar.Json, scalar.Path, true),
            JsonScalarExpression scalar => ((ColumnExpression)scalar.Json, scalar.Path, false),
            JsonQueryExpression query => (query.JsonColumn, query.Path, false),
            _ => throw new UnreachableException(),
        };

        var targetType = Nullable.GetUnderlyingType(target.Type) ?? target.Type;
        var jsonValue = isJsonScalar && targetType == typeof(bool)
            ? SerializeBooleanToJson(value, jsonColumn.TypeMapping)
            : isJsonScalar
                ? value
                : _sqlExpressionFactory.Function(
                    "JSON_EXTRACT",
                    [
                        value,
                        _sqlExpressionFactory.Constant("$"),
                    ],
                    nullable: true,
                    argumentsPropagateNullability:
                    [
                        true,
                        false,
                    ],
                    typeof(string),
                    jsonColumn.TypeMapping);

        var jsonSet = _sqlExpressionFactory.Function(
            MySqlSentinelContract.GetName(MySqlSentinelKind.JsonSet),
            [
                existingSetterValue ?? jsonColumn,
                _sqlExpressionFactory.Constant(path, RelationalTypeMapping.NullMapping),
                jsonValue,
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string),
            jsonColumn.TypeMapping);

        if (existingSetterValue is null)
        {
            return jsonSet;
        }

        existingSetterValue = jsonSet;
        return null;
    }

    private SqlExpression SerializeBooleanToJson(
        SqlExpression value,
        RelationalTypeMapping? jsonTypeMapping
    )
    {
        var stringTypeMapping = _typeMappingSource.FindMapping(typeof(string));
        var serializedValue = _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.IsNull(value),
                    _sqlExpressionFactory.Constant(null, typeof(string), stringTypeMapping)),
                new CaseWhenClause(value, _sqlExpressionFactory.Constant("true", stringTypeMapping)),
            ],
            _sqlExpressionFactory.Constant("false", stringTypeMapping));

        return _sqlExpressionFactory.Function(
            "JSON_EXTRACT",
            [
                serializedValue,
                _sqlExpressionFactory.Constant("$"),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false
            ],
            typeof(string),
            jsonTypeMapping);
    }

    /// <summary>
    /// Tells EF Core's translator that a <see cref="SelectExpression"/> whose row source is a
    /// <see cref="MySqlJsonTableExpression"/> ordered by its synthetic <c>key</c> ordinality
    /// column is "naturally ordered" -- the ordering reflects the inherent row order JSON_TABLE
    /// produces (1, 2, 3, ...), not an explicit user OrderBy. Without this hook EF Core fires
    /// <c>DistinctAfterOrderByWithoutRowLimitingOperatorWarning</c> on every Distinct over a
    /// JSON_TABLE-derived collection because it cannot tell the ordering apart from a user
    /// `.OrderBy()` whose meaning would be lost across the Distinct. Mirrors the SqlServer
    /// <see href="https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.SqlServer/Query/Internal/SqlServerQueryableMethodTranslatingExpressionVisitor.cs">
    /// OPENJSON</see> recognition shape; differs only in not requiring a <c>Convert</c> wrapper
    /// around the key column (our key is already typed <see cref="int"/>; SqlServer's OPENJSON
    /// key is string and gets converted to int for ordering).
    /// </summary>
    protected override bool IsNaturallyOrdered(
        SelectExpression selectExpression
    )
    {
        if (selectExpression.Tables is not [MySqlJsonTableExpression jsonTable, ..])
        {
            return false;
        }

        if (selectExpression.Orderings is not [{ IsAscending: true, Expression: ColumnExpression orderingColumn }])
        {
            return false;
        }

        return orderingColumn.TableAlias == jsonTable.Alias
            && orderingColumn.Name == GetOrdinalityColumnName(jsonTable);
    }

    /// <summary>
    /// Translates a SQL expression that holds a primitive collection (an <c>int[]</c> column,
    /// an inline array constant, etc.) into a JSON_TABLE call exposing two columns: <c>value</c>
    /// (the element value) and <c>key</c> (its 1-based ordinality position).
    /// </summary>
    protected override ShapedQueryExpression? TranslatePrimitiveCollection(
        SqlExpression sqlExpression,
        IProperty? property,
        string tableAlias
    )
    {
        var elementTypeMapping = (RelationalTypeMapping?)sqlExpression.TypeMapping?.ElementTypeMapping;
        var sequenceType = TryGetSequenceType(sqlExpression.Type)
            ?? throw new InvalidOperationException(
                "Primitive-collection translation requires a sequence type; "
                + $"'{sqlExpression.Type}' is not enumerable.");

        var unwrapped = sequenceType.UnwrapNullableType();
        var jsonTableValueMapping = GetJsonTableValueMapping(unwrapped, elementTypeMapping);

        var columns = new List<MySqlJsonTableExpression.ColumnInfo>(2);

        if (jsonTableValueMapping is not null)
        {
            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: "value",
                    TypeMapping: jsonTableValueMapping,
                    Path: [],
                    AsJson: false,
                    ForOrdinality: false));
        }

        const string ordinalityColumnName = "key";

        // The ordinality column matches SqlServer OPENJSON's "key" column semantics. Type mapping
        // is integer because JSON_TABLE FOR ORDINALITY always returns BIGINT-shaped values.
        var keyTypeMapping = _typeMappingSource.FindMapping(typeof(int))!;
        columns.Add(
            new MySqlJsonTableExpression.ColumnInfo(
                Name: ordinalityColumnName,
                TypeMapping: keyTypeMapping,
                Path: null,
                AsJson: false,
                ForOrdinality: true));

        var jsonTableExpression = new MySqlJsonTableExpression(
            alias: tableAlias,
            jsonExpression: sqlExpression,
            path: null,
            columnInfos: columns);

        var isNullable = property?.GetElementType()?.IsNullable
            ?? IsNullableType(sequenceType);

        var valueColumn = new ColumnExpression(
            name: "value",
            tableAlias: tableAlias,
            type: jsonTableValueMapping?.ClrType ?? unwrapped,
            typeMapping: jsonTableValueMapping ?? _typeMappingSource.FindMapping(unwrapped)!,
            nullable: isNullable);

        var valueProjection = DecodeJsonTableValue(
            valueColumn,
            unwrapped,
            elementTypeMapping);

        var keyColumn = new ColumnExpression(
            name: ordinalityColumnName,
            tableAlias: tableAlias,
            type: typeof(int),
            typeMapping: keyTypeMapping,
            nullable: false);

        var identifier = new List<(ColumnExpression Column, ValueComparer Comparer)>(1)
        {
            (keyColumn, keyTypeMapping.Comparer),
        };

        var tables = new List<TableExpressionBase>(1) { jsonTableExpression };

#pragma warning disable EF1001 // SelectExpression's table-list ctor is EF Core internal; the LINQ -> JSON_TABLE translation needs it.
        var select = new SelectExpression(
            tables: tables,
            projection: valueProjection,
            identifier: identifier,
            sqlAliasManager: _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001

        select.AppendOrdering(new OrderingExpression(keyColumn, ascending: true));

        Expression shaper = new ProjectionBindingExpression(select, new ProjectionMember(), MakeNullable(sequenceType));

        if (shaper.Type != sequenceType)
        {
            shaper = Expression.Convert(shaper, sequenceType);
        }

        return new ShapedQueryExpression(select, shaper);
    }

    private RelationalTypeMapping? GetJsonTableValueMapping(
        Type elementType,
        RelationalTypeMapping? elementTypeMapping
    )
    {
        if (elementTypeMapping is null)
        {
            return null;
        }

        if (elementType == typeof(Guid)
            && elementTypeMapping.StoreType.StartsWith("binary", StringComparison.OrdinalIgnoreCase))
        {
            return _typeMappingSource.FindMapping(typeof(string), "char(36)");
        }

        return elementType == typeof(byte[])
            ? _typeMappingSource.FindMapping(typeof(string), "longtext")
            : elementTypeMapping;
    }

    /// <summary>
    /// Restores JSON string encodings to their relational binary representation.
    /// </summary>
    private SqlExpression DecodeJsonTableValue(
        ColumnExpression valueColumn,
        Type elementType,
        RelationalTypeMapping? elementTypeMapping
    )
    {
        if (elementType == typeof(string))
        {
            // JSON_UNQUOTE leaves an already unquoted string unchanged, but gives the
            // expression coercible collation. The compared model column can therefore
            // supply its configured collation instead of colliding with JSON_TABLE's
            // connection-default implicit collation on MariaDB.
            return _sqlExpressionFactory.Function(
                "JSON_UNQUOTE",
                [valueColumn],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string),
                valueColumn.TypeMapping);
        }

        if (elementTypeMapping is null)
        {
            return valueColumn;
        }

        if (elementType == typeof(byte[]))
        {
            return _sqlExpressionFactory.Function(
                "FROM_BASE64",
                [valueColumn],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(byte[]),
                elementTypeMapping);
        }

        if (elementType != typeof(Guid)
            || !elementTypeMapping.StoreType.StartsWith("binary", StringComparison.OrdinalIgnoreCase))
        {
            return valueColumn;
        }

        var normalized = _sqlExpressionFactory.Function(
            "REPLACE",
            [
                valueColumn,
                _sqlExpressionFactory.Constant("-", valueColumn.TypeMapping),
                _sqlExpressionFactory.Constant(string.Empty, valueColumn.TypeMapping),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string),
            valueColumn.TypeMapping);

        return _sqlExpressionFactory.Function(
            "UNHEX",
            [normalized],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(Guid),
            elementTypeMapping);
    }

    /// <summary>
    /// Turns a <see cref="JsonQueryExpression"/> (an owned-JSON entity collection) into a
    /// JSON_TABLE rowset. One typed column per primitive property (<c>PATH '$.PropertyName'</c>),
    /// one <c>JSON</c> column per nested complex/owned navigation (<c>JSON PATH '$.Nav'</c>),
    /// plus a <c>key FOR ORDINALITY</c> column for stable ordering. The base provider falls back
    /// to <c>JsonQueryLinqOperatorsNotSupported</c>; this override is what lets owned-JSON
    /// collections survive LINQ composition (`.Where`, `.Select`, `.OrderBy`, `.Count`, ...).
    /// </summary>
    protected override ShapedQueryExpression? TransformJsonQueryToTable(
        JsonQueryExpression jsonQueryExpression
    )
    {
        // JSON_TABLE's row-source path must be a literal SQL string, so non-constant array
        // indices (parameter ElementAt, dynamic expression) cannot be spliced through CONCAT
        // there. The dynamic case is handled in MySqlQuerySqlGenerator.VisitJsonTableExpression
        // by wrapping the JSON source in JSON_EXTRACT(col, CONCAT('$...')) and using '$[*]' as
        // the static row-source path; both engines accept this composition.
        var structuralType = jsonQueryExpression.StructuralType;
        var lastNamedSegment = jsonQueryExpression.Path.LastOrDefault(static segment => segment.PropertyName is not null);
        var aliasHint = lastNamedSegment.PropertyName ?? jsonQueryExpression.JsonColumn.Name;
        var alias = _queryCompilationContext.SqlAliasManager.GenerateTableAlias(aliasHint);

        var columns = new List<MySqlJsonTableExpression.ColumnInfo>();

        foreach (var prop in structuralType.GetPropertiesInHierarchy())
        {
            var jsonPropertyName = prop.GetJsonPropertyName();
            if (jsonPropertyName is null)
            {
                continue;
            }

            var typeMapping = prop.GetRelationalTypeMapping();
            var asJson = typeMapping.ElementTypeMapping is not null;

            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: jsonPropertyName,
                    TypeMapping: typeMapping,
                    Path: [new PathSegment(jsonPropertyName)],
                    AsJson: asJson,
                    ForOrdinality: false));
        }

        var containerColumnName = structuralType.GetContainerColumnName()
            ?? throw new UnreachableException("Owned-JSON structural type must declare a container column.");

        var containerColumn = structuralType
            .ContainingEntityType.GetTableMappings()
            .SelectMany(static m => m.Table.Columns)
            .Single(c => c.Name == containerColumnName);

        var nestedJsonPropertyNames = structuralType switch
        {
            IEntityType entityType => entityType
                .GetNavigationsInHierarchy()
                .Where(n => n.ForeignKey.IsOwnership
                    && n.TargetEntityType.IsMappedToJson()
                    && n.ForeignKey.PrincipalToDependent == n)
                .Select(n => n.TargetEntityType.GetJsonPropertyName()
                    ?? throw new UnreachableException(
                        "Owned-JSON navigation target must declare a JSON property name.")),
            IComplexType complexType => complexType
                .GetComplexProperties()
                .Select(p => p.ComplexType.GetJsonPropertyName()
                    ?? throw new UnreachableException(
                        "Owned-JSON complex sub-type must declare a JSON property name.")),
            _ => throw new UnreachableException(
                "JsonQueryExpression.StructuralType must be IEntityType or IComplexType."),
        };

        foreach (var name in nestedJsonPropertyNames)
        {
            columns.Add(
                new MySqlJsonTableExpression.ColumnInfo(
                    Name: name,
                    TypeMapping: containerColumn.StoreTypeMapping,
                    Path: [new PathSegment(name)],
                    AsJson: true,
                    ForOrdinality: false));
        }

        // Ordinality column for stable ordering across the rowset. JSON_TABLE FOR ORDINALITY is
        // 1-based; the spec test corpus does not depend on a particular start index, only on
        // deterministic ordering. MySQL compares JSON_TABLE column names case-insensitively, so
        // a JSON property named "Key" cannot share the usual synthetic "key" name.
        var ordinalityColumnName = CreateOrdinalityColumnName(columns);
        var keyTypeMapping = _typeMappingSource.FindMapping(typeof(int))!;
        columns.Add(
            new MySqlJsonTableExpression.ColumnInfo(
                Name: ordinalityColumnName,
                TypeMapping: keyTypeMapping,
                Path: null,
                AsJson: false,
                ForOrdinality: true));

        var jsonTableExpression = new MySqlJsonTableExpression(
            alias: alias,
            jsonExpression: jsonQueryExpression.JsonColumn,
            path: jsonQueryExpression.Path,
            columnInfos: columns);

#pragma warning disable EF1001 // RelationalQueryableMethodTranslatingExpressionVisitor.CreateSelect(JsonQueryExpression, ...) is EF Core internal; the JSON_TABLE shaper needs it.
        var select = CreateSelect(
            jsonQueryExpression,
            jsonTableExpression,
            identifierColumnName: ordinalityColumnName,
            identifierColumnType: typeof(int),
            identifierColumnTypeMapping: keyTypeMapping);
#pragma warning restore EF1001

        select.AppendOrdering(
            new OrderingExpression(
                select.CreateColumnExpression(
                    jsonTableExpression,
                    ordinalityColumnName,
                    typeof(int),
                    keyTypeMapping,
                    columnNullable: false),
                ascending: true));

        return new ShapedQueryExpression(
            select,
            new RelationalStructuralTypeShaperExpression(
                jsonQueryExpression.StructuralType,
                new ProjectionBindingExpression(select, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

    private static string CreateOrdinalityColumnName(
        IReadOnlyList<MySqlJsonTableExpression.ColumnInfo> columns
    )
    {
        const string preferredName = "key";

        if (!columns.Any(column => string.Equals(column.Name, preferredName, StringComparison.OrdinalIgnoreCase)))
        {
            return preferredName;
        }

        const string fallbackPrefix = "__doka_ordinality";
        var candidate = fallbackPrefix;
        var suffix = 0;

        while (columns.Any(column => string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{fallbackPrefix}_{++suffix}";
        }

        return candidate;
    }

    private static string GetOrdinalityColumnName(
        MySqlJsonTableExpression jsonTable
    ) => (jsonTable.ColumnInfos
            ?? throw new UnreachableException("A translated JSON_TABLE rowset must declare its columns."))
        .Single(static column => column.ForOrdinality)
        .Name;

    // Mirrors System.SharedTypeExtensions.TryGetSequenceType (EF Core internal); inlined so the
    // translator does not depend on the internal extension surface.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification =
            "The Types passed here come from EF Core's primitive-collection translation pipeline and are well-known IEnumerable<T>/T[] shapes preserved by the runtime.")]
    private static Type? TryGetSequenceType(
        Type type
    )
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GenericTypeArguments[0];
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsNullableType(
        Type type
    ) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification =
            "Nullable<T> generic instantiation is preserved by the runtime; EF Core relies on this same construction throughout the query pipeline.")]
    private static Type MakeNullable(
        Type type
    ) => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;
}
