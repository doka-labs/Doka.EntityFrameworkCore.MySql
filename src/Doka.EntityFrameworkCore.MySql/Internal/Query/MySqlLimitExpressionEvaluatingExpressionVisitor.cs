namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Evaluates combined parameterized row limits before MySQL SQL generation.
/// </summary>
/// <remarks>
/// EF Core represents consecutive <c>Take</c> operators as <c>LEAST</c>. MySQL
/// and MariaDB support that function in ordinary expressions, but their
/// <c>LIMIT</c> grammar accepts an integer value or parameter rather than an
/// arbitrary scalar function. This visitor only inspects row-limit expressions
/// and disables SQL caching only when concrete parameter values are required.
/// Sources retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/select.html">
/// MySQL 8.4 SELECT Statement</see> and
/// <see href="https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/limit">
/// MariaDB LIMIT</see>.
/// </remarks>
internal sealed class MySqlLimitExpressionEvaluatingExpressionVisitor : ExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly ParametersCacheDecorator _parametersDecorator;

    public MySqlLimitExpressionEvaluatingExpressionVisitor(
        ISqlExpressionFactory sqlExpressionFactory,
        ParametersCacheDecorator parametersDecorator
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
        _parametersDecorator = parametersDecorator ?? throw new ArgumentNullException(nameof(parametersDecorator));
    }

    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is ShapedQueryExpression shapedQueryExpression)
        {
            return shapedQueryExpression
                .UpdateQueryExpression(Visit(shapedQueryExpression.QueryExpression))
                .UpdateShaperExpression(Visit(shapedQueryExpression.ShaperExpression));
        }

        var visited = base.VisitExtension(node);

        return visited is SelectExpression selectExpression ? EvaluateLimit(selectExpression) : visited;
    }

    private SelectExpression EvaluateLimit(
        SelectExpression selectExpression
    )
    {
        if (selectExpression.Limit is not SqlFunctionExpression { Name: "LEAST", Instance: null, } least)
        {
            return selectExpression;
        }

        var parameterValues = _parametersDecorator.GetAndDisableCaching();
        if (!TryEvaluateIntegralLimit(least, parameterValues, out var value))
        {
            throw new InvalidOperationException(
                "MySQL LIMIT requires an integral value or parameter, but the "
                + "combined row-limit expression could not be evaluated.");
        }

        var evaluatedLimit = _sqlExpressionFactory.Constant(value, selectExpression.Limit.TypeMapping);

        return selectExpression.Update(
            selectExpression.Tables,
            selectExpression.Predicate,
            selectExpression.GroupBy,
            selectExpression.Having,
            selectExpression.Projection,
            selectExpression.Orderings,
            selectExpression.Offset,
            evaluatedLimit);
    }

    private static bool TryEvaluateIntegralLimit(
        SqlExpression expression,
        IReadOnlyDictionary<string, object?> parameterValues,
        out object value
    )
    {
        if (!TryEvaluateLimitValue(expression, parameterValues, out var limit))
        {
            value = null!;
            return false;
        }

        var targetType = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
        if (!IsIntegralType(targetType))
        {
            value = null!;
            return false;
        }

        value = Convert.ChangeType(Math.Max(0m, limit), targetType, CultureInfo.InvariantCulture);

        return true;
    }

    private static bool TryEvaluateLimitValue(
        SqlExpression expression,
        IReadOnlyDictionary<string, object?> parameterValues,
        out decimal value
    )
    {
        switch (expression)
        {
            case SqlConstantExpression { Value: not null } constant:
                return TryConvertToDecimal(constant.Value, out value);

            case SqlParameterExpression parameter
                when parameterValues.TryGetValue(parameter.Name, out var parameterValue) && parameterValue is not null:
                return TryConvertToDecimal(parameterValue, out value);

            case SqlFunctionExpression { Name: "LEAST", Instance: null, Arguments.Count: > 0, } least:
                {
                    var minimum = decimal.MaxValue;

                    foreach (var argument in least.Arguments)
                    {
                        if (!TryEvaluateLimitValue(argument, parameterValues, out var argumentValue))
                        {
                            value = 0;
                            return false;
                        }

                        minimum = Math.Min(minimum, argumentValue);
                    }

                    value = minimum;
                    return true;
                }

            default:
                value = 0;
                return false;
        }
    }

    private static bool TryConvertToDecimal(
        object candidate,
        out decimal value
    )
    {
        try
        {
            value = Convert.ToDecimal(candidate, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            value = 0;
            return false;
        }
    }

    private static bool IsIntegralType(
        Type type
    ) => type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong);
}
