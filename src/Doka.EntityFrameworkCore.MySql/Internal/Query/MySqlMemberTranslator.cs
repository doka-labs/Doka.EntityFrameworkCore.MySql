namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMemberTranslator : IMemberTranslator
{
    private static readonly RelationalTypeMapping s_intTypeMapping = new IntTypeMapping("int", DbType.Int32);
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly FrozenDictionary<string, string> s_datePartFunctionNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(DateTime.Year)] = "YEAR",
            [nameof(DateTime.Month)] = "MONTH",
            [nameof(DateTime.Day)] = "DAY",
            [nameof(DateTime.Hour)] = "HOUR",
            [nameof(DateTime.Minute)] = "MINUTE",
            [nameof(DateTime.Second)] = "SECOND",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(logger);

        // Static members (no instance required).
        if (instance is null)
        {
            if (member.DeclaringType != typeof(DateTime))
            {
                return null;
            }

            return member.Name switch
            {
                nameof(DateTime.Now) => _sqlExpressionFactory.Function(
                    "NOW",
                    Array.Empty<SqlExpression>(),
                    nullable: false,
                    argumentsPropagateNullability: Array.Empty<bool>(),
                    returnType),
                nameof(DateTime.UtcNow) => _sqlExpressionFactory.Function(
                    "UTC_TIMESTAMP",
                    Array.Empty<SqlExpression>(),
                    nullable: false,
                    argumentsPropagateNullability: Array.Empty<bool>(),
                    returnType),
                _ => null,
            };
        }

        if (member.DeclaringType == typeof(string)
            && member.Name == nameof(string.Length))
        {
            return _sqlExpressionFactory.Function(
                "CHAR_LENGTH",
                new[] { instance },
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                returnType,
                s_intTypeMapping);
        }

        if (member.DeclaringType == typeof(DateTime)
            && member.Name == nameof(DateTime.Date))
        {
            return _sqlExpressionFactory.Function(
                "DATE",
                new[] { instance },
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                returnType,
                instance.TypeMapping);
        }

        if (member.DeclaringType == typeof(DateTime)
            && member.Name is nameof(DateTime.Microsecond) or nameof(DateTime.Nanosecond))
        {
            return TranslateSubMillisecondPart(member.Name, instance);
        }

        if (s_datePartFunctionNames.TryGetValue(member.Name, out var functionName))
        {
            return TranslateDatePart(functionName, instance, returnType);
        }

        // TimeSpan member translations.
        if (member.DeclaringType == typeof(TimeSpan))
        {
            switch (member.Name)
            {
                case nameof(TimeSpan.TotalSeconds):
                    return _sqlExpressionFactory.Function(
                        "TIME_TO_SEC",
                        new[] { instance },
                        nullable: true,
                        argumentsPropagateNullability: s_singleArgumentNullPropagation,
                        returnType);

                case nameof(TimeSpan.TotalMinutes):
                    return _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function(
                            "TIME_TO_SEC",
                            new[] { instance },
                            nullable: true,
                            argumentsPropagateNullability: s_singleArgumentNullPropagation,
                            typeof(double)),
                        _sqlExpressionFactory.Constant(60.0));

                case nameof(TimeSpan.TotalHours):
                    return _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function(
                            "TIME_TO_SEC",
                            new[] { instance },
                            nullable: true,
                            argumentsPropagateNullability: s_singleArgumentNullPropagation,
                            typeof(double)),
                        _sqlExpressionFactory.Constant(3600.0));
            }
        }

        return null;
    }

    private SqlExpression TranslateDatePart(
        string functionName,
        SqlExpression instance,
        Type returnType
    ) => _sqlExpressionFactory.Function(
        functionName,
        new[] { instance },
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        returnType,
        s_intTypeMapping);

    /// <summary>
    /// Derives the .NET sub-millisecond component from the engines' full
    /// microseconds-within-second value.
    /// </summary>
    /// <remarks>
    /// MySQL and MariaDB temporal values expose at most six fractional digits.
    /// Sources retrieved 2026-07-28:
    /// <see href="https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html">
    /// MySQL fractional seconds</see> and
    /// <see href="https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb">
    /// MariaDB microseconds</see>.
    /// </remarks>
    private SqlExpression TranslateSubMillisecondPart(
        string memberName,
        SqlExpression instance
    )
    {
        var microseconds = _sqlExpressionFactory.Function(
            "MICROSECOND",
            new[] { instance },
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(int),
            s_intTypeMapping);

        var enginePrecisionValue = memberName == nameof(DateTime.Nanosecond)
            ? _sqlExpressionFactory.Multiply(
                microseconds,
                _sqlExpressionFactory.Constant(1000, s_intTypeMapping))
            : microseconds;

        return _sqlExpressionFactory.Modulo(
            enginePrecisionValue,
            _sqlExpressionFactory.Constant(1000, s_intTypeMapping));
    }
}
