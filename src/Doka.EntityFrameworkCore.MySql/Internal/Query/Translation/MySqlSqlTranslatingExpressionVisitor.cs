namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds MySQL-family scalar translations that require access to the relational
/// expression visitor rather than the method-call translator pipeline.
/// </summary>
/// <remarks>
/// MySQL and MariaDB both provide native variadic <c>LEAST</c> and
/// <c>GREATEST</c> functions, and both return <see langword="null"/> when any
/// argument is <see langword="null"/>. Sources retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/comparison-operators.html">
/// MySQL 8.4 comparison functions</see>,
/// <see href="https://mariadb.com/docs/server/reference/sql-structure/operators/comparison-operators/least">
/// MariaDB LEAST</see>, and
/// <see href="https://mariadb.com/docs/server/reference/sql-structure/operators/comparison-operators/greatest">
/// MariaDB GREATEST</see>.
///
/// DateTime subtraction is translated through a private tick-valued SQL sentinel.
/// The SQL generator emits signed <c>TIMESTAMPDIFF(MICROSECOND) * 10</c>, which avoids
/// the database <c>TIME</c> range while preserving the engines' six-digit temporal
/// precision.
/// </remarks>
internal sealed class MySqlSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    private static readonly bool[] s_twoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public MySqlSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor
    ) : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
        _typeMappingSource = dependencies.TypeMappingSource;
    }

    /// <inheritdoc />
    public override SqlExpression? GenerateGreatest(
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    ) => GenerateComparisonFunction("GREATEST", expressions, resultType);

    /// <inheritdoc />
    public override SqlExpression? GenerateLeast(
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    ) => GenerateComparisonFunction("LEAST", expressions, resultType);

    /// <summary>
    /// Preserves promoted coalesce result mappings, translates <see cref="DateTime"/>
    /// subtraction to the provider's tick-valued SQL sentinel, and delegates every
    /// other binary expression to EF Core.
    /// </summary>
    protected override Expression VisitBinary(
        BinaryExpression binaryExpression
    )
    {
        if (HasPromotedCoalesceOperand(binaryExpression)
            && TranslatePromotedCoalesce(binaryExpression) is { } coalesce)
        {
            return coalesce;
        }

        if (!IsDateTimeDifference(binaryExpression))
        {
            return base.VisitBinary(binaryExpression);
        }

        var left = Visit(binaryExpression.Left);
        var right = Visit(binaryExpression.Right);

        if (left is not SqlExpression leftSql
            || right is not SqlExpression rightSql)
        {
            return QueryCompilationContext.NotTranslatedExpression;
        }

        return _sqlExpressionFactory.Function(
            "__mysql_datetime_diff_ticks",
            [
                rightSql,
                leftSql,
            ],
            nullable: true,
            argumentsPropagateNullability: s_twoArgumentNullPropagation,
            binaryExpression.Type,
            MySqlTimeSpanTicksTypeMapping.Default);
    }

    /// <summary>
    /// Preserves the explicit CLR conversion carried by a coalesce expression
    /// instead of letting EF Core infer the result mapping from its left operand.
    /// </summary>
    /// <remarks>
    /// A coalesce such as <c>uint? ?? double</c> stores its numeric promotion in a
    /// conversion around the nullable left operand. The relational base visitor removes
    /// that promotion before creating <c>COALESCE</c>, which would otherwise make the
    /// result reader use the unsigned-integer mapping and truncate fractional values.
    /// </remarks>
    private SqlExpression? TranslatePromotedCoalesce(
        BinaryExpression binaryExpression
    )
    {
        if (Visit(binaryExpression.Left) is not SqlExpression left
            || Visit(binaryExpression.Right) is not SqlExpression right)
        {
            return null;
        }

        var resultType = binaryExpression.Type.UnwrapNullableType();
        var resultTypeMapping = _typeMappingSource.FindMapping(resultType);
        if (resultTypeMapping is null)
        {
            return null;
        }

        var convertedLeft = _sqlExpressionFactory.Convert(left, resultType, resultTypeMapping);
        var convertedRight = right.Type == resultType
            ? _sqlExpressionFactory.ApplyTypeMapping(right, resultTypeMapping)
            : _sqlExpressionFactory.Convert(right, resultType, resultTypeMapping);

        return _sqlExpressionFactory.Coalesce(convertedLeft, convertedRight, resultTypeMapping);
    }

    private static bool HasPromotedCoalesceOperand(
        BinaryExpression binaryExpression
    )
    {
        if (binaryExpression.NodeType != ExpressionType.Coalesce)
        {
            return false;
        }

        var resultType = binaryExpression.Type.UnwrapNullableType();
        var leftSourceType = binaryExpression.Left.Type.UnwrapNullableType();

        if (binaryExpression.Left is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            leftSourceType = conversion.Operand.Type.UnwrapNullableType();
        }

        return leftSourceType != resultType;
    }

    private SqlExpression GenerateComparisonFunction(
        string functionName,
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    )
    {
        var resultTypeMapping = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions.InferTypeMapping(expressions);

        return _sqlExpressionFactory.Function(
            functionName,
            expressions,
            nullable: true,
            argumentsPropagateNullability: Enumerable.Repeat(true, expressions.Count),
            resultType,
            resultTypeMapping);
    }

    private static bool IsDateTimeDifference(
        BinaryExpression binaryExpression
    ) => binaryExpression.NodeType == ExpressionType.Subtract
        && binaryExpression.Left.Type.UnwrapNullableType() == typeof(DateTime)
        && binaryExpression.Right.Type.UnwrapNullableType() == typeof(DateTime)
        && binaryExpression.Type.UnwrapNullableType() == typeof(TimeSpan);
}
