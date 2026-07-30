namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates NetTopologySuite collection aggregates to MySQL spatial aggregates.
/// </summary>
/// <remarks>
/// MySQL 8.4 supplies <c>ST_Collect</c> as an aggregate and window function.
/// Unary union and envelope semantics are composed from the documented scalar
/// spatial functions. Source retrieved 2026-07-29:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/spatial-aggregate-functions.html">
/// MySQL 8.4 spatial aggregate functions</see>.
/// </remarks>
internal sealed class MySqlNetTopologySuiteAggregateMethodTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo s_geometryCombineMethod = typeof(GeometryCombiner).GetRuntimeMethod(
        nameof(GeometryCombiner.Combine),
        [typeof(IEnumerable<Geometry>)])!;

    private static readonly MethodInfo s_unionMethod = typeof(UnaryUnionOp).GetRuntimeMethod(
        nameof(UnaryUnionOp.Union),
        [typeof(IEnumerable<Geometry>)])!;

    private static readonly MethodInfo s_envelopeCombineMethod = typeof(EnvelopeCombiner).GetRuntimeMethod(
        nameof(EnvelopeCombiner.CombineAsGeometry),
        [typeof(IEnumerable<Geometry>)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _geometryTypeMapping;

    public MySqlNetTopologySuiteAggregateMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _geometryTypeMapping = typeMappingSource.FindMapping(typeof(Geometry))!;
    }

    public SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (source.Selector is not SqlExpression selector
            || method != s_geometryCombineMethod && method != s_unionMethod && method != s_envelopeCombineMethod)
        {
            return null;
        }

        if (source.Predicate is not null)
        {
            selector = _sqlExpressionFactory.Case(
                [new CaseWhenClause(source.Predicate, selector)],
                elseResult: null);
        }

        if (source.IsDistinct)
        {
            selector = new DistinctExpression(selector);
        }

        var collect = _sqlExpressionFactory.Function(
            "ST_Collect",
            [selector],
            nullable: true,
            argumentsPropagateNullability: [false],
            typeof(Geometry),
            _geometryTypeMapping);

        if (method == s_geometryCombineMethod)
        {
            return collect;
        }

        if (method == s_envelopeCombineMethod)
        {
            return _sqlExpressionFactory.Function(
                "ST_Envelope",
                [collect],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(Geometry),
                _geometryTypeMapping);
        }

        return _sqlExpressionFactory.Function(
            "ST_Union",
            [
                collect,
                collect,
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                true,
            ],
            typeof(Geometry),
            _geometryTypeMapping);
    }
}
