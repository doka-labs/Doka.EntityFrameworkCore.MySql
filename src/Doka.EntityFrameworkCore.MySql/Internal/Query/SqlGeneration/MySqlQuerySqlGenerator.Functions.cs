namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>
    /// Intercepts typed provider sentinels for MySQL-specific compound SQL
    /// expressions such as <c>MATCH(...) AGAINST(...)</c>.
    /// </summary>
    protected override Expression VisitSqlFunction(
        SqlFunctionExpression sqlFunctionExpression
    )
    {
        ArgumentNullException.ThrowIfNull(sqlFunctionExpression);

        if (!MySqlSentinelContract.IsSentinelName(sqlFunctionExpression.Name))
        {
            return base.VisitSqlFunction(sqlFunctionExpression);
        }

        var arguments = sqlFunctionExpression.Arguments ?? Array.Empty<SqlExpression>();
        var sentinel = MySqlSentinelContract.Resolve(sqlFunctionExpression.Name, arguments.Count);

        switch (sentinel.Kind)
        {
            case MySqlSentinelKind.JsonSet:
                {
                    EmitJsonSet(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.RegularExpression:
                {
                    EmitRegularExpression(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.Match:
            case MySqlSentinelKind.MatchBoolean:
                {
                    Sql.Append("MATCH(");
                    Visit(arguments[0]);
                    Sql.Append(") AGAINST(");
                    Visit(arguments[1]);

                    if (sentinel.Kind == MySqlSentinelKind.MatchBoolean)
                    {
                        Sql.Append(" IN BOOLEAN MODE");
                    }

                    Sql.Append(")");

                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.GroupConcat:
                {
                    EmitGroupConcat(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.OrderAscending:
            case MySqlSentinelKind.OrderDescending:
                {
                    Visit(arguments[0]);
                    Sql.Append(sentinel.Kind == MySqlSentinelKind.OrderAscending ? " ASC" : " DESC");
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.GuidToString:
                {
                    EmitGuidToString(arguments[0]);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.DateTimeOffsetNow:
            case MySqlSentinelKind.DateTimeOffsetUtcNow:
                {
                    EmitDateTimeOffsetNow(utc: sentinel.Kind == MySqlSentinelKind.DateTimeOffsetUtcNow);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.DateTimeOffsetSubtractTimeSpan:
                {
                    EmitDateTimeOffsetSubtractTimeSpan(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.DateTimeDifferenceTicks:
                {
                    EmitDateTimeDifferenceTicks(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.TimeDifferenceTicks:
                {
                    EmitTimeDifferenceTicks(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.TimeOfDayTicks:
                {
                    EmitTimeOfDayTicks(sqlFunctionExpression);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.LeftShift:
            case MySqlSentinelKind.RightShift:
                {
                    EmitShift(
                        sqlFunctionExpression,
                        isRightShift: sentinel.Kind == MySqlSentinelKind.RightShift);
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.OnesComplement:
                {
                    Sql.Append("CAST((~");
                    Visit(arguments[0]);
                    Sql.Append(") AS SIGNED)");
                    return sqlFunctionExpression;
                }

            case MySqlSentinelKind.DateAdd:
            case MySqlSentinelKind.TimeAdd:
                {
                    EmitDateAdd(
                        sqlFunctionExpression,
                        sentinel.IntervalUnit
                        ?? throw new InvalidOperationException("Date/time addition sentinel has no interval unit."));
                    return sqlFunctionExpression;
                }

            default:
                throw new UnreachableException();
        }
    }

    private void EmitRegularExpression(
        SqlFunctionExpression expression
    )
    {
        var arguments = GetRequiredArguments(expression, 2);
        var input = arguments[0];
        var pattern = arguments[1];

        Sql.Append("(CASE WHEN ");
        Visit(pattern);
        Sql.Append(" = '' THEN TRUE ELSE ");

        if (!Profile.Engine.Has(EngineCapability.RegexpLikeFunction))
        {
            Visit(input);
            Sql.Append(" REGEXP ");
            Visit(pattern);
        }
        else
        {
            Sql.Append("REGEXP_LIKE(");
            Visit(input);
            Sql.Append(", ");
            Visit(pattern);
            Sql.Append(")");
        }

        Sql.Append(" END)");
    }

    /// <summary>
    /// Emits MySQL's ordered aggregate grammar:
    /// <c>GROUP_CONCAT(value ORDER BY ... SEPARATOR separator)</c>.
    /// </summary>
    private void EmitGroupConcat(
        SqlFunctionExpression expression
    )
    {
        var arguments = expression.Arguments
            ?? throw new InvalidOperationException(
                $"The {expression.Name} sentinel requires at least two arguments.");

        Sql.Append("GROUP_CONCAT(");
        Visit(arguments[0]);

        if (arguments.Count > 2)
        {
            Sql.Append(" ORDER BY ");

            for (var index = 2; index < arguments.Count; index++)
            {
                if (index > 2)
                {
                    Sql.Append(", ");
                }

                Visit(arguments[index]);
            }
        }

        Sql.Append(" SEPARATOR ");
        Visit(arguments[1]);
        Sql.Append(")");
    }

    private void EmitGuidToString(
        SqlExpression hexadecimal
    )
    {
        Sql.Append("LOWER(CONCAT(");
        EmitSubstring(hexadecimal, 1, 8);
        Sql.Append(", '-', ");
        EmitSubstring(hexadecimal, 9, 4);
        Sql.Append(", '-', ");
        EmitSubstring(hexadecimal, 13, 4);
        Sql.Append(", '-', ");
        EmitSubstring(hexadecimal, 17, 4);
        Sql.Append(", '-', ");
        EmitSubstring(hexadecimal, 21, 12);
        Sql.Append("))");
    }

    private void EmitTimeDifferenceTicks(
        SqlFunctionExpression expression
    )
    {
        var arguments = GetRequiredArguments(expression, 2);

        Sql.Append("CAST((TIMESTAMPDIFF(MICROSECOND, TIMESTAMP('2000-01-01', ");
        Visit(arguments[1]);
        Sql.Append("), TIMESTAMP('2000-01-01', ");
        Visit(arguments[0]);
        Sql.Append(")) * 10) AS SIGNED)");
    }

    private void EmitTimeOfDayTicks(
        SqlFunctionExpression expression
    )
    {
        var arguments = GetRequiredArguments(expression, 1);

        Sql.Append("CAST((TIMESTAMPDIFF(MICROSECOND, DATE(");
        Visit(arguments[0]);
        Sql.Append("), ");
        Visit(arguments[0]);
        Sql.Append(") * 10) AS SIGNED)");
    }

    private void EmitShift(
        SqlFunctionExpression expression,
        bool isRightShift
    )
    {
        var arguments = GetRequiredArguments(expression, 2);
        var resultType = expression.Type.UnwrapNullableType();
        var bitWidth = resultType == typeof(long) || resultType == typeof(ulong) ? 64 : 32;

        if (isRightShift)
        {
            if (resultType == typeof(uint)
                || resultType == typeof(ulong))
            {
                EmitUnsignedRightShift(arguments[0], arguments[1], bitWidth);
            }
            else
            {
                EmitSignedRightShift(arguments[0], arguments[1], bitWidth);
            }

            return;
        }

        if (resultType == typeof(uint))
        {
            Sql.Append("((");
            Visit(arguments[0]);
            Sql.Append(" << ");
            EmitMaskedShift(arguments[1], bitWidth);
            Sql.Append(") & 4294967295)");
            return;
        }

        if (resultType == typeof(ulong))
        {
            Sql.Append("(");
            Visit(arguments[0]);
            Sql.Append(" << ");
            EmitMaskedShift(arguments[1], bitWidth);
            Sql.Append(")");
            return;
        }

        if (bitWidth == 32)
        {
            EmitSignedInt32LeftShift(arguments[0], arguments[1]);
            return;
        }

        Sql.Append("CAST((CAST(");
        Visit(arguments[0]);
        Sql.Append(" AS SIGNED) << ");
        EmitMaskedShift(arguments[1], bitWidth);
        Sql.Append(") AS SIGNED)");
    }

    /// <summary>
    /// Preserves CLR arithmetic right-shift semantics for negative values while
    /// applying the CLR shift-count mask. MySQL-family bitwise operators otherwise
    /// interpret the operand as unsigned and zero-fill the high bits.
    /// </summary>
    private void EmitSignedRightShift(
        SqlExpression value,
        SqlExpression shift,
        int bitWidth
    )
    {
        Sql.Append("(CASE WHEN ");
        Visit(value);
        Sql.Append(" < 0 THEN CAST((~((~");
        Visit(value);
        Sql.Append(") >> ");
        EmitMaskedShift(shift, bitWidth);
        Sql.Append(")) AS SIGNED) ELSE CAST((");
        Visit(value);
        Sql.Append(" >> ");
        EmitMaskedShift(shift, bitWidth);
        Sql.Append(") AS SIGNED) END)");
    }

    private void EmitUnsignedRightShift(
        SqlExpression value,
        SqlExpression shift,
        int bitWidth
    )
    {
        Sql.Append("(");

        if (bitWidth == 32)
        {
            Sql.Append("(");
            Visit(value);
            Sql.Append(" & 4294967295)");
        }
        else
        {
            Visit(value);
        }

        Sql.Append(" >> ");
        EmitMaskedShift(shift, bitWidth);
        Sql.Append(")");
    }

    /// <summary>
    /// Narrows the engine's unsigned 64-bit shift result back to the signed
    /// 32-bit domain required by CLR integer promotion.
    /// </summary>
    private void EmitSignedInt32LeftShift(
        SqlExpression value,
        SqlExpression shift
    )
    {
        Sql.Append("CAST((CAST((((CAST(");
        Visit(value);
        Sql.Append(" AS SIGNED) << ");
        EmitMaskedShift(shift, 32);
        Sql.Append(") & 4294967295) ^ 2147483648) AS SIGNED) - 2147483648) AS SIGNED)");
    }

    private void EmitMaskedShift(
        SqlExpression shift,
        int bitWidth
    )
    {
        Sql.Append("(");
        Visit(shift);
        Sql.Append(bitWidth == 64 ? " & 63)" : " & 31)");
    }

    private static IReadOnlyList<SqlExpression> GetRequiredArguments(
        SqlFunctionExpression expression,
        int expectedCount
    )
    {
        var arguments = expression.Arguments;

        if (arguments is null
            || arguments.Count != expectedCount)
        {
            throw new InvalidOperationException($"The {expression.Name} sentinel requires {expectedCount} arguments.");
        }

        return arguments;
    }

    private void EmitSubstring(
        SqlExpression expression,
        int start,
        int length
    )
    {
        Sql.Append("SUBSTRING(");
        Visit(expression);
        Sql.Append(", ");
        Sql.Append(start.ToString(CultureInfo.InvariantCulture));
        Sql.Append(", ");
        Sql.Append(length.ToString(CultureInfo.InvariantCulture));
        Sql.Append(")");
    }

    /// <summary>
    /// Produces the provider's sortable DateTimeOffset text shape, including the
    /// current session offset for <see cref="DateTimeOffset.Now"/>.
    /// </summary>
    private void EmitDateTimeOffsetNow(
        bool utc
    )
    {
        Sql.Append("CONCAT(DATE_FORMAT(");
        Sql.Append(utc ? "UTC_TIMESTAMP(6)" : "NOW(6)");
        Sql.Append(", '%Y-%m-%d %H:%i:%s.%f'), '0', ");

        if (utc)
        {
            Sql.Append("'+00:00')");
            return;
        }

        Sql.Append("CASE WHEN TIMEDIFF(NOW(), UTC_TIMESTAMP()) < 0 ");
        Sql.Append("THEN TIME_FORMAT(TIMEDIFF(NOW(), UTC_TIMESTAMP()), '%H:%i') ");
        Sql.Append("ELSE CONCAT('+', TIME_FORMAT(TIMEDIFF(NOW(), UTC_TIMESTAMP()), '%H:%i')) END)");
    }

    /// <summary>
    /// Subtracts a native <c>TIME</c> value from the local timestamp portion of
    /// the provider's sortable <see cref="DateTimeOffset"/> text and retains its
    /// seventh fractional digit and offset.
    /// </summary>
    private void EmitDateTimeOffsetSubtractTimeSpan(
        SqlFunctionExpression expression
    )
    {
        var arguments = GetRequiredArguments(expression, 2);

        Sql.Append("CONCAT(DATE_FORMAT(SUBTIME(STR_TO_DATE(LEFT(");
        Visit(arguments[0]);
        Sql.Append(", 26), '%Y-%m-%d %H:%i:%s.%f'), ");
        Visit(arguments[1]);
        Sql.Append("), '%Y-%m-%d %H:%i:%s.%f'), SUBSTRING(");
        Visit(arguments[0]);
        Sql.Append(", 27, 1), RIGHT(");
        Visit(arguments[0]);
        Sql.Append(", 6))");
    }

    /// <summary>
    /// Emits a signed, range-preserving <see cref="TimeSpan"/> value as
    /// 100-nanosecond ticks.
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPDIFF(MICROSECOND, start, end)</c> returns <c>end - start</c>
    /// as an integer on both engines. Multiplying by ten converts microseconds
    /// to .NET ticks. Sources retrieved 2026-07-28:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/date-and-time-functions.html#function_timestampdiff">
    /// MySQL TIMESTAMPDIFF</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/timestampdiff">
    /// MariaDB TIMESTAMPDIFF</see>.
    /// </remarks>
    private void EmitDateTimeDifferenceTicks(
        SqlFunctionExpression expression
    )
    {
        var arguments = GetRequiredArguments(expression, 2);

        Sql.Append("(TIMESTAMPDIFF(MICROSECOND, ");
        Visit(arguments[0]);
        Sql.Append(", ");
        Visit(arguments[1]);
        Sql.Append(") * 10)");
    }

    /// <summary>
    /// Emits <c>DATE_ADD(arg0, INTERVAL arg1 UNIT)</c> for the parametrized-interval
    /// translation path. The interval keyword sits between the comma and the value, so
    /// the standard function-arguments comma-separator path cannot express the shape;
    /// the sentinel-function-name pattern lets the translator stay inside the
    /// SqlExpression tree and lets this writer hand-roll the syntax.
    /// </summary>
    private void EmitDateAdd(
        SqlFunctionExpression expression,
        MySqlIntervalUnit intervalUnit
    )
    {
        var arguments = expression.Arguments
            ?? throw new InvalidOperationException($"Sentinel function '{expression.Name}' must carry arguments.");

        Sql.Append("DATE_ADD(");
        Visit(arguments[0]);
        Sql.Append(", INTERVAL ");
        Visit(arguments[1]);
        Sql.Append(" ");
        Sql.Append(MySqlSentinelContract.GetIntervalSql(intervalUnit));
        Sql.Append(")");
    }
}
