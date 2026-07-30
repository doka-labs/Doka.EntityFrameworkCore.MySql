namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Applies element type inference from primitive-collection consumers back to
/// parameterized <c>JSON_TABLE</c> sources.
/// </summary>
internal sealed class MySqlTypeMappingPostprocessor : RelationalTypeMappingPostprocessor
{
    private readonly IModel _model;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public MySqlTypeMappingPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _model = queryCompilationContext.Model;
        _typeMappingSource = relationalDependencies.TypeMappingSource;
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
        var parameterTypeMapping = _typeMappingSource.FindMapping(parameter.Type, _model, elementTypeMapping);

        if (parameterTypeMapping?.ElementTypeMapping is null)
        {
            throw new InvalidOperationException(
                $"A JSON collection mapping for '{parameter.Type}' "
                + $"and element mapping '{elementTypeMapping.StoreType}' "
                + "could not be found.");
        }

        var columns = new List<MySqlJsonTableExpression.ColumnInfo>((jsonTable.ColumnInfos?.Count ?? 0) + 1)
        {
            new(Name: "value", TypeMapping: elementTypeMapping, Path: [], AsJson: false, ForOrdinality: false),
        };

        if (jsonTable.ColumnInfos is not null)
        {
            columns.AddRange(jsonTable.ColumnInfos);
        }

        return jsonTable.Update(parameter.ApplyTypeMapping(parameterTypeMapping), jsonTable.Path, columns);
    }
}
