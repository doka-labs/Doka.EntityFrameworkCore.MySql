namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates temporal factory, composition, range, parsing, and Unix-time methods.
/// </summary>
internal sealed class MySqlTemporalMethodCallTranslator : IMethodCallTranslator
{
    private const string DateTimeParseFormat = "%c/%e/%Y %H:%i:%s";
    private const string DateTimeOffsetParseFormat = "%Y-%m-%d %H:%i:%s.%f";

    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_twoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private static readonly bool[] s_threeArgumentNullPropagation =
    [
        false,
        true,
        true,
    ];

    private static readonly MethodInfo s_dateOnlyFromDateTimeMethod = typeof(DateOnly).GetRuntimeMethod(
        nameof(DateOnly.FromDateTime),
        [typeof(DateTime)])!;

    private static readonly MethodInfo s_dateOnlyToDateTimeMethod = typeof(DateOnly).GetRuntimeMethod(
        nameof(DateOnly.ToDateTime),
        [typeof(TimeOnly)])!;

    private static readonly MethodInfo s_timeOnlyFromDateTimeMethod = typeof(TimeOnly).GetRuntimeMethod(
        nameof(TimeOnly.FromDateTime),
        [typeof(DateTime)])!;

    private static readonly MethodInfo s_timeOnlyFromTimeSpanMethod = typeof(TimeOnly).GetRuntimeMethod(
        nameof(TimeOnly.FromTimeSpan),
        [typeof(TimeSpan)])!;

    private static readonly MethodInfo s_timeOnlyIsBetweenMethod = typeof(TimeOnly).GetRuntimeMethod(
        nameof(TimeOnly.IsBetween),
        [
            typeof(TimeOnly),
            typeof(TimeOnly),
        ])!;

    private static readonly MethodInfo s_dateTimeParseMethod = typeof(DateTime).GetRuntimeMethod(
        nameof(DateTime.Parse),
        [typeof(string)])!;

    private static readonly MethodInfo s_toUnixTimeSecondsMethod = typeof(DateTimeOffset).GetRuntimeMethod(
        nameof(DateTimeOffset.ToUnixTimeSeconds),
        Type.EmptyTypes)!;

    private static readonly MethodInfo s_toUnixTimeMillisecondsMethod = typeof(DateTimeOffset).GetRuntimeMethod(
        nameof(DateTimeOffset.ToUnixTimeMilliseconds),
        Type.EmptyTypes)!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _dateOnlyTypeMapping;
    private readonly RelationalTypeMapping _dateTimeTypeMapping;
    private readonly RelationalTypeMapping _longTypeMapping;
    private readonly RelationalTypeMapping _timeOnlyTypeMapping;

    public MySqlTemporalMethodCallTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _dateOnlyTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(DateOnly));
        _dateTimeTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(DateTime));
        _longTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(long));
        _timeOnlyTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(TimeOnly));
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (method == s_dateOnlyFromDateTimeMethod)
        {
            return TranslateSingleArgumentFunction("DATE", arguments[0], typeof(DateOnly), _dateOnlyTypeMapping);
        }

        if (method == s_dateOnlyToDateTimeMethod
            && instance is not null)
        {
            var dateTime = _sqlExpressionFactory.Convert(instance, typeof(DateTime), _dateTimeTypeMapping);

            return _sqlExpressionFactory.Function(
                "ADDTIME",
                [
                    dateTime,
                    arguments[0],
                ],
                nullable: true,
                argumentsPropagateNullability: s_twoArgumentNullPropagation,
                typeof(DateTime),
                _dateTimeTypeMapping);
        }

        if (method == s_timeOnlyFromDateTimeMethod)
        {
            return TranslateSingleArgumentFunction("TIME", arguments[0], typeof(TimeOnly), _timeOnlyTypeMapping);
        }

        if (method == s_timeOnlyFromTimeSpanMethod)
        {
            return _sqlExpressionFactory.Convert(arguments[0], typeof(TimeOnly), _timeOnlyTypeMapping);
        }

        if (method == s_timeOnlyIsBetweenMethod
            && instance is not null)
        {
            var startBeforeEnd = _sqlExpressionFactory.LessThanOrEqual(arguments[0], arguments[1]);
            var nonWrappingRange = _sqlExpressionFactory.AndAlso(
                _sqlExpressionFactory.GreaterThanOrEqual(instance, arguments[0]),
                _sqlExpressionFactory.LessThan(instance, arguments[1]));
            var wrappingRange = _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.GreaterThanOrEqual(instance, arguments[0]),
                _sqlExpressionFactory.LessThan(instance, arguments[1]));

            return _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(startBeforeEnd, nonWrappingRange),
                ],
                wrappingRange);
        }

        if (method == s_dateTimeParseMethod)
        {
            return _sqlExpressionFactory.Function(
                "STR_TO_DATE",
                [
                    arguments[0],
                    _sqlExpressionFactory.Constant(DateTimeParseFormat),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                ],
                typeof(DateTime),
                _dateTimeTypeMapping);
        }

        if (instance is not null
            && method == s_toUnixTimeSecondsMethod)
        {
            return TranslateUnixTime(instance, milliseconds: false);
        }

        if (instance is not null
            && method == s_toUnixTimeMillisecondsMethod)
        {
            return TranslateUnixTime(instance, milliseconds: true);
        }

        return null;
    }

    /// <summary>
    /// Interprets the lossless DateTimeOffset text mapping as a local timestamp and
    /// subtracts its explicit offset before computing Unix time.
    /// </summary>
    private SqlExpression TranslateUnixTime(
        SqlExpression instance,
        bool milliseconds
    )
    {
        var localDateTime = ParseDateTimeOffsetLocalValue(instance);
        var unit = milliseconds ? "MICROSECOND" : "SECOND";
        var difference = _sqlExpressionFactory.Function(
            "TIMESTAMPDIFF",
            [
                _sqlExpressionFactory.Fragment(unit),
                _sqlExpressionFactory.Constant(DateTime.UnixEpoch, _dateTimeTypeMapping),
                localDateTime,
            ],
            nullable: true,
            argumentsPropagateNullability: s_threeArgumentNullPropagation,
            typeof(long),
            _longTypeMapping);

        var normalizedDifference = milliseconds ? IntegerDivide(difference, 1000) : difference;
        var offsetSeconds = ReadDateTimeOffsetSeconds(instance);
        var normalizedOffset = milliseconds
            ? _sqlExpressionFactory.Multiply(offsetSeconds, _sqlExpressionFactory.Constant(1000L))
            : offsetSeconds;

        return _sqlExpressionFactory.Subtract(normalizedDifference, normalizedOffset);
    }

    private SqlExpression ParseDateTimeOffsetLocalValue(
        SqlExpression instance
    )
    {
        var fractionalSeparator = _sqlExpressionFactory.Function(
            "LOCATE",
            [
                _sqlExpressionFactory.Constant("."),
                instance,
            ],
            nullable: true,
            argumentsPropagateNullability: s_twoArgumentNullPropagation,
            typeof(int));

        var length = _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.GreaterThan(fractionalSeparator, _sqlExpressionFactory.Constant(0)),
                    _sqlExpressionFactory.Constant(26)),
            ],
            _sqlExpressionFactory.Constant(19));

        var localValue = _sqlExpressionFactory.Function(
            "LEFT",
            [
                instance,
                length,
            ],
            nullable: true,
            argumentsPropagateNullability: s_twoArgumentNullPropagation,
            typeof(string));

        return _sqlExpressionFactory.Function(
            "STR_TO_DATE",
            [
                localValue,
                _sqlExpressionFactory.Constant(DateTimeOffsetParseFormat),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
            ],
            typeof(DateTime),
            _dateTimeTypeMapping);
    }

    private SqlExpression ReadDateTimeOffsetSeconds(
        SqlExpression instance
    )
    {
        var sign = _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.Equal(Substring(instance, -6, 1), _sqlExpressionFactory.Constant("-")),
                    _sqlExpressionFactory.Constant(-1L)),
            ],
            _sqlExpressionFactory.Constant(1L));

        var hours = _sqlExpressionFactory.Convert(Substring(instance, -5, 2), typeof(long), _longTypeMapping);
        var minutes = _sqlExpressionFactory.Convert(Substring(instance, -2, 2), typeof(long), _longTypeMapping);
        var totalSeconds = _sqlExpressionFactory.Add(
            _sqlExpressionFactory.Multiply(hours, _sqlExpressionFactory.Constant(3600L)),
            _sqlExpressionFactory.Multiply(minutes, _sqlExpressionFactory.Constant(60L)));

        return _sqlExpressionFactory.Multiply(sign, totalSeconds);
    }

    private SqlExpression Substring(
        SqlExpression instance,
        int start,
        int length
    ) => _sqlExpressionFactory.Function(
        "SUBSTRING",
        [
            instance,
            _sqlExpressionFactory.Constant(start),
            _sqlExpressionFactory.Constant(length),
        ],
        nullable: true,
        argumentsPropagateNullability:
        [
            true,
            false,
            false,
        ],
        typeof(string));

    private SqlExpression IntegerDivide(
        SqlExpression expression,
        long divisor
    ) => _sqlExpressionFactory.Function(
        "FLOOR",
        [
            _sqlExpressionFactory.Divide(expression, _sqlExpressionFactory.Constant(divisor)),
        ],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        typeof(long),
        _longTypeMapping);

    private SqlExpression TranslateSingleArgumentFunction(
        string functionName,
        SqlExpression argument,
        Type resultType,
        RelationalTypeMapping typeMapping
    ) => _sqlExpressionFactory.Function(
        functionName,
        [argument],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        resultType,
        typeMapping);
}
