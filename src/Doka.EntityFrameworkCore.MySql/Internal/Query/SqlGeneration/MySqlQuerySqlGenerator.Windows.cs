namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private MySqlWindowParameterConstraints.Builder? _windowParameterConstraintsBuilder;

    /// <summary>Writes a typed calculation; ordinary queries never enter this opt-in path.</summary>
    private void GenerateWindow(
        MySqlWindowExpression window
    )
    {
        MySqlWindowParameterConstraints.Collect(window, Profile.Engine, ref _windowParameterConstraintsBuilder);

        if (window.Function is MySqlWindowFunction.Lag or MySqlWindowFunction.Lead
            && window.Arguments.Count == 3
            && !Profile.Engine.Has(EngineCapability.WindowNavigationDefaultArgument))
        {
            // WHY: COALESCE would replace an existing NULL neighbor. A separate presence marker
            // uses the identical partition/order and selects the default only for a missing row.
            Sql.Append("CASE WHEN ");
            GenerateWindowCall(window, presence: true, includeDefault: false);
            Sql.Append(" IS NULL THEN ");
            Visit(window.Arguments[2]);
            Sql.Append(" ELSE ");
            GenerateWindowCall(window, presence: false, includeDefault: false);
            Sql.Append(" END");
        }
        else
        {
            GenerateWindowCall(window, presence: false, includeDefault: true);
        }
    }

    private void GenerateWindowCall(
        MySqlWindowExpression window,
        bool presence,
        bool includeDefault
    )
    {
        Sql.Append(window.Function switch
        {
            MySqlWindowFunction.Count or MySqlWindowFunction.LongCount => "COUNT",
            MySqlWindowFunction.Sum => "SUM",
            MySqlWindowFunction.Average => "AVG",
            MySqlWindowFunction.Min => "MIN",
            MySqlWindowFunction.Max => "MAX",
            MySqlWindowFunction.RowNumber => "ROW_NUMBER",
            MySqlWindowFunction.Rank => "RANK",
            MySqlWindowFunction.DenseRank => "DENSE_RANK",
            MySqlWindowFunction.Ntile => "NTILE",
            MySqlWindowFunction.PercentRank => "PERCENT_RANK",
            MySqlWindowFunction.CumeDist => "CUME_DIST",
            MySqlWindowFunction.Lag => "LAG",
            MySqlWindowFunction.Lead => "LEAD",
            MySqlWindowFunction.FirstValue => "FIRST_VALUE",
            MySqlWindowFunction.LastValue => "LAST_VALUE",
            MySqlWindowFunction.NthValue => "NTH_VALUE",
            _ => throw new InvalidOperationException("The window calculation is not supported."),
        });

        Sql.Append("(");

        if (window.Function is MySqlWindowFunction.Count or MySqlWindowFunction.LongCount)
        {
            Sql.Append("*");
        }
        else
        {
            var count = !includeDefault && window.Arguments.Count == 3
                ? 2
                : window.Arguments.Count;

            // WHY: The immutable read-only argument wrapper allocates an interface enumerator.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    Sql.Append(", ");
                }

                if (presence && index == 0)
                {
                    Sql.Append("1");
                }
                else if (IsWindowPosition(window.Function, index))
                {
                    GenerateWindowPosition(window.Arguments[index], integral: true);
                }
                else
                {
                    Visit(window.Arguments[index]);
                }
            }
        }

        Sql.Append(") OVER (");
        GenerateWindowSpecification(window);
        Sql.Append(")");
    }

    private void GenerateWindowSpecification(
        MySqlWindowExpression window
    )
    {
        if (window.Partitions.Count != 0)
        {
            Sql.Append("PARTITION BY ");
            GenerateWindowList(window.Partitions);
        }

        if (window.Orderings.Count != 0)
        {
            if (window.Partitions.Count != 0)
            {
                Sql.Append(" ");
            }

            Sql.Append("ORDER BY ");
            GenerateWindowList(window.Orderings);
        }

        if (window.Frame is { } frame)
        {
            Sql.Append(frame.Rows
                ? " ROWS BETWEEN "
                : " RANGE BETWEEN ");

            GenerateWindowBound(frame.StartKind, frame.StartOffset, frame.Rows);
            Sql.Append(" AND ");
            GenerateWindowBound(frame.EndKind, frame.EndOffset, frame.Rows);
        }
    }

    private void GenerateWindowList<T>(
        IReadOnlyList<T> expressions
    )
        where T : Expression
    {
        // WHY: Indexed emission avoids both interface enumerators and a delegate per window child list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < expressions.Count; index++)
        {
            if (index > 0)
            {
                Sql.Append(", ");
            }

            Visit(expressions[index]);
        }
    }

    private void GenerateWindowBound(
        MySqlWindowBoundKind kind,
        SqlExpression? offset,
        bool rows
    )
    {
        switch (kind)
        {
            case MySqlWindowBoundKind.UnboundedPreceding:
                Sql.Append("UNBOUNDED PRECEDING");
                break;

            case MySqlWindowBoundKind.UnboundedFollowing:
                Sql.Append("UNBOUNDED FOLLOWING");
                break;

            case MySqlWindowBoundKind.CurrentRow:
                Sql.Append("CURRENT ROW");
                break;

            case MySqlWindowBoundKind.Preceding:
            case MySqlWindowBoundKind.Following:
                GenerateWindowPosition(offset!, rows);
                Sql.Append(kind == MySqlWindowBoundKind.Preceding
                    ? " PRECEDING"
                    : " FOLLOWING");

                break;

            default:
                throw new InvalidOperationException("The window frame endpoint is not supported.");
        }
    }

    private void GenerateWindowPosition(
        SqlExpression expression,
        bool integral
    )
    {
        // WHY: The constraint collector permits only range-preserving numeric conversions;
        // native position/frame grammars require the literal or parameter itself, not a CAST expression.
        while (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion)
        {
            expression = conversion.Operand;
        }

        if (integral && expression is SqlConstantExpression { Value: not null } constant)
        {
            // WHY: Frame factories accept decimal offsets, but ROWS needs an integer SQL literal.
            // Validation has already excluded fractional and out-of-range inputs.
            expression = new SqlConstantExpression(Convert.ToInt64(constant.Value, CultureInfo.InvariantCulture),
                LongTypeMapping.Default);
        }

        else if (integral
            && expression is SqlParameterExpression parameter
            && (Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type) == typeof(decimal))
        {
            // WHY: MySQL rejects a CLR decimal frame parameter even with DbType.Int64.
            // The pre-I/O policy validates integrality/range; this mapping then binds an actual long.
            expression = parameter.ApplyTypeMapping(WindowRowsParameterMapping.Decimal);
        }

        Visit(expression);
    }

    private static bool IsWindowPosition(
        MySqlWindowFunction function,
        int argumentIndex
    ) => (function == MySqlWindowFunction.Ntile && argumentIndex == 0)
        || (function is MySqlWindowFunction.Lag or MySqlWindowFunction.Lead or MySqlWindowFunction.NthValue
            && argumentIndex == 1);

    private static class WindowRowsParameterMapping
    {
        // WHY: The decimal-to-long mapping is initialized only for this opt-in frame path.
        public static readonly RelationalTypeMapping Decimal =
            (RelationalTypeMapping)LongTypeMapping.Default.WithComposedConverter(
                new ValueConverter<decimal, long>(value => MySqlWindowParameterConstraints.ConvertRowsOffset(value),
                    value => value));
    }
}
