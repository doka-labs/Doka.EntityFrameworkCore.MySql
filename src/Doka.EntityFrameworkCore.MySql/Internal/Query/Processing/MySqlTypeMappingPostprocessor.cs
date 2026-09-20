namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Applies element type inference from primitive-collection consumers back to
/// parameterized <c>JSON_TABLE</c> sources.
/// </summary>
internal sealed class MySqlTypeMappingPostprocessor : RelationalTypeMappingPostprocessor
{
    private readonly IModel _model;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private Dictionary<JsonTableColumn, DeferredValueMapping>? _deferredValueMappings;

    public MySqlTypeMappingPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _model = queryCompilationContext.Model;
        _sqlExpressionFactory = relationalDependencies.SqlExpressionFactory;
        _typeMappingSource = relationalDependencies.TypeMappingSource;
    }

    /// <inheritdoc />
    public override Expression Process(
        Expression expression
    )
    {
        _deferredValueMappings?.Clear();

        var processed = base.Process(expression);

        if (_deferredValueMappings is not { Count: > 0 } deferredValueMappings)
        {
            return processed;
        }

        // EF Core first infers the relational element mapping from the
        // collection consumer. Only after that pass can JSON text extraction
        // be decoded without assuming the provider's default mapping.
        return new DeferredValueDecodingExpressionVisitor(
                deferredValueMappings,
                _sqlExpressionFactory)
            .Visit(processed);
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(
        Expression expression
    )
    {
        if (expression is MySqlJsonTableExpression
            {
                JsonExpression: SqlParameterExpression { TypeMapping: null, } parameter,
            } jsonTable
            && TryGetInferredTypeMapping(jsonTable.Alias, "value", out var elementTypeMapping))
        {
            return ApplyTypeMapping(jsonTable, parameter, elementTypeMapping);
        }

        return base.VisitExtension(expression);
    }

    private MySqlJsonTableExpression ApplyTypeMapping(
        MySqlJsonTableExpression jsonTable,
        SqlParameterExpression parameter,
        RelationalTypeMapping elementTypeMapping
    )
    {
        var parameterElementTypeMapping = MySqlJsonTableValueEncoding.GetParameterElementTypeMapping(
            elementTypeMapping,
            _typeMappingSource);

        var parameterTypeMapping = _typeMappingSource.FindMapping(
            parameter.Type,
            _model,
            parameterElementTypeMapping);

        if (parameterTypeMapping?.ElementTypeMapping is not RelationalTypeMapping)
        {
            throw new InvalidOperationException(
                $"A JSON collection mapping for '{parameter.Type}' "
                + $"and element mapping '{elementTypeMapping.StoreType}' "
                + "could not be found.");
        }

        var elementType = elementTypeMapping.ClrType.UnwrapNullableType();
        var usesBase64StringTransport = MySqlJsonTableValueEncoding.UsesBase64StringTransport(
            elementType,
            elementTypeMapping);

        var extractionTypeMapping = MySqlJsonTableValueEncoding.GetExtractionTypeMapping(
                elementType,
                elementTypeMapping,
                _typeMappingSource)
            ?? elementTypeMapping;

        if (MySqlJsonTableValueEncoding.RequiresDecoding(elementType, elementTypeMapping))
        {
            var mappings = _deferredValueMappings ??= [];

            mappings[new JsonTableColumn(jsonTable.Alias, "value")] = new DeferredValueMapping(
                elementType,
                elementTypeMapping,
                extractionTypeMapping,
                usesBase64StringTransport);
        }

        List<MySqlJsonTableExpression.ColumnInfo> columns = [];
        columns.EnsureCapacity((jsonTable.ColumnInfos?.Count ?? 0) + 1);
        columns.Add(new(
            Name: "value",
            TypeMapping: extractionTypeMapping,
            Path: [],
            AsJson: false,
            ForOrdinality: false));

        if (jsonTable.ColumnInfos is not null)
        {
            columns.AddRange(jsonTable.ColumnInfos);
        }

        return jsonTable.Update(parameter.ApplyTypeMapping(parameterTypeMapping), jsonTable.Path, columns);
    }

    private readonly record struct DeferredValueMapping(
        Type ElementType,
        RelationalTypeMapping ElementTypeMapping,
        RelationalTypeMapping ExtractionTypeMapping,
        bool UsesBase64StringTransport
    );

    private readonly record struct JsonTableColumn(
        string TableAlias,
        string ColumnName
    );

    private sealed class DeferredValueDecodingExpressionVisitor(
        IReadOnlyDictionary<JsonTableColumn, DeferredValueMapping> mappings,
        ISqlExpressionFactory sqlExpressionFactory
    ) : ExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is ShapedQueryExpression shapedQuery)
            {
                return shapedQuery.UpdateQueryExpression(Visit(shapedQuery.QueryExpression));
            }

            if (node is not ColumnExpression column
                || !mappings.TryGetValue(new JsonTableColumn(column.TableAlias, column.Name), out var mapping))
            {
                return base.VisitExtension(node);
            }

            var extractionColumn = (ColumnExpression)column.ApplyTypeMapping(mapping.ExtractionTypeMapping);

            return MySqlJsonTableValueEncoding.Decode(
                extractionColumn,
                mapping.ElementType,
                mapping.ElementTypeMapping,
                sqlExpressionFactory,
                mapping.UsesBase64StringTransport);
        }
    }
}
