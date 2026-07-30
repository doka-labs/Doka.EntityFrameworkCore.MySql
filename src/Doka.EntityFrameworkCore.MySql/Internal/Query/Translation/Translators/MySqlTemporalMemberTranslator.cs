namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates members of the .NET temporal types according to their concrete
/// MySQL-family storage representations.
/// </summary>
internal sealed class MySqlTemporalMemberTranslator : IMemberTranslator
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly FrozenDictionary<string, string> s_datePartFunctions =
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
    private readonly RelationalTypeMapping _dateTimeOffsetTypeMapping;
    private readonly RelationalTypeMapping _dateTimeTypeMapping;
    private readonly RelationalTypeMapping _doubleTypeMapping;
    private readonly RelationalTypeMapping _intTypeMapping;
    private readonly RelationalTypeMapping _longTypeMapping;
    private readonly RelationalTypeMapping _timeSpanTypeMapping;

    public MySqlTemporalMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _dateTimeOffsetTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(DateTimeOffset));
        _dateTimeTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(DateTime));
        _doubleTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(double));
        _intTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(int));
        _longTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(long));
        _timeSpanTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(TimeSpan));
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        var declaringType = member.DeclaringType;

        if (instance is null)
        {
            return TranslateStaticMember(declaringType, member.Name, returnType);
        }

        if (declaringType == typeof(DateTimeOffset))
        {
            return TranslateDateTimeOffsetMember(instance, member.Name, returnType);
        }

        if (declaringType == typeof(TimeSpan))
        {
            return TranslateTimeSpanMember(instance, member.Name, returnType);
        }

        if (declaringType != typeof(DateTime)
            && declaringType != typeof(DateOnly)
            && declaringType != typeof(TimeOnly))
        {
            return null;
        }

        if (s_datePartFunctions.TryGetValue(member.Name, out var functionName))
        {
            return TranslateFunction(functionName, instance, returnType, _intTypeMapping);
        }

        return member.Name switch
        {
            nameof(DateTime.Millisecond) => TranslateMillisecond(instance),
            nameof(DateTime.Microsecond) => TranslateSubMillisecondPart(instance, nanoseconds: false),
            nameof(DateTime.Nanosecond) => TranslateSubMillisecondPart(instance, nanoseconds: true),
            nameof(DateTime.DayOfYear) => TranslateFunction("DAYOFYEAR", instance, returnType, _intTypeMapping),
            nameof(DateTime.DayOfWeek) => _sqlExpressionFactory.Subtract(
                TranslateFunction("DAYOFWEEK", instance, returnType, _intTypeMapping),
                _sqlExpressionFactory.Constant(1, _intTypeMapping)),
            nameof(DateTime.Date) => TranslateFunction("DATE", instance, returnType, instance.TypeMapping),
            nameof(DateTime.TimeOfDay) => _sqlExpressionFactory.Function(
                MySqlSentinelContract.GetName(MySqlSentinelKind.TimeOfDayTicks),
                [instance],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                returnType,
                MySqlTimeSpanTicksTypeMapping.Default),
            nameof(DateOnly.DayNumber) => _sqlExpressionFactory.Subtract(
                TranslateFunction("TO_DAYS", instance, returnType, _intTypeMapping),
                _sqlExpressionFactory.Constant(366, _intTypeMapping)),
            _ => null,
        };
    }

    private SqlExpression? TranslateStaticMember(
        Type? declaringType,
        string memberName,
        Type returnType
    )
    {
        if (declaringType == typeof(DateTimeOffset))
        {
            return memberName switch
            {
                nameof(DateTimeOffset.Now) => TranslateDateTimeOffsetNow(utc: false),
                nameof(DateTimeOffset.UtcNow) => TranslateDateTimeOffsetNow(utc: true),
                _ => null,
            };
        }

        if (declaringType != typeof(DateTime))
        {
            return null;
        }

        return memberName switch
        {
            nameof(DateTime.Now) => TranslateCurrentDateTime("NOW", returnType),
            nameof(DateTime.UtcNow) => TranslateCurrentDateTime("UTC_TIMESTAMP", returnType),
            nameof(DateTime.Today) => _sqlExpressionFactory.Function(
                "CURDATE",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                returnType,
                _dateTimeTypeMapping),
            _ => null,
        };
    }

    private SqlExpression TranslateDateTimeOffsetNow(
        bool utc
    ) => _sqlExpressionFactory.Function(
        MySqlSentinelContract.GetName(
            utc
                ? MySqlSentinelKind.DateTimeOffsetUtcNow
                : MySqlSentinelKind.DateTimeOffsetNow),
        Array.Empty<SqlExpression>(),
        nullable: false,
        argumentsPropagateNullability: Array.Empty<bool>(),
        typeof(DateTimeOffset),
        _dateTimeOffsetTypeMapping);

    private SqlExpression TranslateCurrentDateTime(
        string functionName,
        Type returnType
    ) => _sqlExpressionFactory.Function(
        functionName,
        [_sqlExpressionFactory.Constant(6)],
        nullable: false,
        argumentsPropagateNullability: [false],
        returnType,
        _dateTimeTypeMapping);

    /// <summary>
    /// Reads DateTimeOffset components from the provider's lossless sortable text
    /// representation. This preserves offsets, year 0001, and the seventh fractional
    /// digit that the engines' native temporal types cannot store.
    /// </summary>
    private SqlExpression? TranslateDateTimeOffsetMember(
        SqlExpression instance,
        string memberName,
        Type returnType
    )
    {
        var (start, length, scale) = memberName switch
        {
            nameof(DateTimeOffset.Year) => (Start: 1, Length: 4, Scale: 1),
            nameof(DateTimeOffset.Month) => (Start: 6, Length: 2, Scale: 1),
            nameof(DateTimeOffset.Day) => (Start: 9, Length: 2, Scale: 1),
            nameof(DateTimeOffset.Hour) => (Start: 12, Length: 2, Scale: 1),
            nameof(DateTimeOffset.Minute) => (Start: 15, Length: 2, Scale: 1),
            nameof(DateTimeOffset.Second) => (Start: 18, Length: 2, Scale: 1),
            nameof(DateTimeOffset.Millisecond) => (Start: 21, Length: 3, Scale: 1),
            nameof(DateTimeOffset.Microsecond) => (Start: 24, Length: 3, Scale: 1),
            nameof(DateTimeOffset.Nanosecond) => (Start: 27, Length: 1, Scale: 100),
            _ => default,
        };

        if (start != 0)
        {
            var value = ReadDateTimeOffsetComponent(instance, start, length);

            return scale == 1
                ? value
                : _sqlExpressionFactory.Multiply(
                    value,
                    _sqlExpressionFactory.Constant(scale, _intTypeMapping));
        }

        return memberName switch
        {
            nameof(DateTimeOffset.Date) => _sqlExpressionFactory.Function(
                "LEFT",
                [
                    instance,
                    _sqlExpressionFactory.Constant(10),
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    false,
                ],
                returnType,
                _dateTimeTypeMapping),
            nameof(DateTimeOffset.DayOfYear) => TranslateDateTimeOffsetDayOfYear(instance),
            _ => null,
        };
    }

    private SqlExpression TranslateDateTimeOffsetDayOfYear(
        SqlExpression instance
    )
    {
        var year = ReadDateTimeOffsetComponent(instance, 1, 4);
        var month = ReadDateTimeOffsetComponent(instance, 6, 2);
        var day = ReadDateTimeOffsetComponent(instance, 9, 2);
        var daysBeforeMonth = _sqlExpressionFactory.Case(
            month,
            [
                new CaseWhenClause(_sqlExpressionFactory.Constant(2), _sqlExpressionFactory.Constant(31)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(3), _sqlExpressionFactory.Constant(59)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(4), _sqlExpressionFactory.Constant(90)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(5), _sqlExpressionFactory.Constant(120)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(6), _sqlExpressionFactory.Constant(151)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(7), _sqlExpressionFactory.Constant(181)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(8), _sqlExpressionFactory.Constant(212)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(9), _sqlExpressionFactory.Constant(243)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(10), _sqlExpressionFactory.Constant(273)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(11), _sqlExpressionFactory.Constant(304)),
                new CaseWhenClause(_sqlExpressionFactory.Constant(12), _sqlExpressionFactory.Constant(334)),
            ],
            _sqlExpressionFactory.Constant(0));

        var divisibleByFour = IsDivisibleBy(year, 4);
        var divisibleByOneHundred = IsDivisibleBy(year, 100);
        var divisibleByFourHundred = IsDivisibleBy(year, 400);
        var leapYear = _sqlExpressionFactory.AndAlso(
            divisibleByFour,
            _sqlExpressionFactory.OrElse(_sqlExpressionFactory.Not(divisibleByOneHundred), divisibleByFourHundred));

        var leapDay = _sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.AndAlso(
                        _sqlExpressionFactory.GreaterThan(month, _sqlExpressionFactory.Constant(2)),
                        leapYear),
                    _sqlExpressionFactory.Constant(1)),
            ],
            _sqlExpressionFactory.Constant(0));

        return _sqlExpressionFactory.Add(_sqlExpressionFactory.Add(daysBeforeMonth, day), leapDay);
    }

    private SqlExpression ReadDateTimeOffsetComponent(
        SqlExpression instance,
        int start,
        int length
    )
    {
        var substring = _sqlExpressionFactory.Function(
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

        return _sqlExpressionFactory.Convert(substring, typeof(int), _intTypeMapping);
    }

    private SqlExpression? TranslateTimeSpanMember(
        SqlExpression instance,
        string memberName,
        Type returnType
    )
    {
        if (instance.TypeMapping is MySqlTimeSpanTicksTypeMapping)
        {
            return TranslateTickTimeSpanMember(instance, memberName, returnType);
        }

        var totalSeconds = TranslateFunction("TIME_TO_SEC", instance, typeof(double), _doubleTypeMapping);

        return memberName switch
        {
            nameof(TimeSpan.Days) => TranslateTimeSpanComponent(totalSeconds, TimeSpan.SecondsPerDay, 0),
            nameof(TimeSpan.Hours) => TranslateTimeSpanComponent(totalSeconds, TimeSpan.SecondsPerHour, 24),
            nameof(TimeSpan.Minutes) => TranslateTimeSpanComponent(totalSeconds, TimeSpan.SecondsPerMinute, 60),
            nameof(TimeSpan.Seconds) => TranslateTimeSpanComponent(totalSeconds, 1, 60),
            nameof(TimeSpan.Milliseconds) => TranslateFractionalTimeSpanComponent(instance, 1000, 1000),
            nameof(TimeSpan.Microseconds) => TranslateFractionalTimeSpanComponent(instance, 1, 1000),
            nameof(TimeSpan.Nanoseconds) => _sqlExpressionFactory.Constant(0, _intTypeMapping),
            nameof(TimeSpan.TotalDays) => DivideTotal(totalSeconds, TimeSpan.SecondsPerDay),
            nameof(TimeSpan.TotalHours) => DivideTotal(totalSeconds, TimeSpan.SecondsPerHour),
            nameof(TimeSpan.TotalMinutes) => DivideTotal(totalSeconds, TimeSpan.SecondsPerMinute),
            nameof(TimeSpan.TotalSeconds) => totalSeconds,
            nameof(TimeSpan.TotalMilliseconds) => MultiplyTotal(totalSeconds, 1000),
            nameof(TimeSpan.TotalMicroseconds) => MultiplyTotal(totalSeconds, 1_000_000),
            nameof(TimeSpan.TotalNanoseconds) => MultiplyTotal(totalSeconds, 1_000_000_000),
            _ => null,
        };
    }

    private SqlExpression? TranslateTickTimeSpanMember(
        SqlExpression ticks,
        string memberName,
        Type returnType
    )
    {
        // DateTime subtraction yields numeric ticks that can retain a temporal
        // mapping. Normalize the mapping before applying numeric operations.
        var tickValue = _sqlExpressionFactory.Convert(ticks, typeof(long), _longTypeMapping);

        if (memberName == nameof(TimeSpan.TotalNanoseconds))
        {
            return _sqlExpressionFactory.Multiply(
                _sqlExpressionFactory.Convert(tickValue, typeof(double), _doubleTypeMapping),
                _sqlExpressionFactory.Constant(100.0));
        }

        var divisor = memberName switch
        {
            nameof(TimeSpan.TotalDays) => TimeSpan.TicksPerDay,
            nameof(TimeSpan.TotalHours) => TimeSpan.TicksPerHour,
            nameof(TimeSpan.TotalMinutes) => TimeSpan.TicksPerMinute,
            nameof(TimeSpan.TotalSeconds) => TimeSpan.TicksPerSecond,
            nameof(TimeSpan.TotalMilliseconds) => TimeSpan.TicksPerMillisecond,
            nameof(TimeSpan.TotalMicroseconds) => TimeSpan.TicksPerMicrosecond,
            _ => 0L,
        };

        if (divisor != 0)
        {
            return _sqlExpressionFactory.Divide(
                _sqlExpressionFactory.Convert(tickValue, typeof(double), _doubleTypeMapping),
                _sqlExpressionFactory.Constant((double)divisor));
        }

        return memberName switch
        {
            nameof(TimeSpan.Days) => TranslateTickComponent(tickValue, TimeSpan.TicksPerDay, 0),
            nameof(TimeSpan.Hours) => TranslateTickComponent(tickValue, TimeSpan.TicksPerHour, 24),
            nameof(TimeSpan.Minutes) => TranslateTickComponent(tickValue, TimeSpan.TicksPerMinute, 60),
            nameof(TimeSpan.Seconds) => TranslateTickComponent(tickValue, TimeSpan.TicksPerSecond, 60),
            nameof(TimeSpan.Milliseconds) => TranslateTickComponent(tickValue, TimeSpan.TicksPerMillisecond, 1000),
            nameof(TimeSpan.Microseconds) => TranslateTickComponent(tickValue, TimeSpan.TicksPerMicrosecond, 1000),
            nameof(TimeSpan.Nanoseconds) => _sqlExpressionFactory.Multiply(
                _sqlExpressionFactory.Modulo(tickValue, _sqlExpressionFactory.Constant(TimeSpan.TicksPerMicrosecond)),
                _sqlExpressionFactory.Constant(100)),
            _ => null,
        };
    }

    private SqlExpression TranslateTimeSpanComponent(
        SqlExpression totalSeconds,
        long divisor,
        int modulus
    )
    {
        var component = TranslateTruncate(
            _sqlExpressionFactory.Divide(totalSeconds, _sqlExpressionFactory.Constant((double)divisor)));

        if (modulus > 0)
        {
            component = _sqlExpressionFactory.Modulo(component, _sqlExpressionFactory.Constant(modulus));
        }

        return _sqlExpressionFactory.Convert(component, typeof(int), _intTypeMapping);
    }

    private SqlExpression TranslateFractionalTimeSpanComponent(
        SqlExpression instance,
        int divisor,
        int modulus
    )
    {
        var microseconds = TranslateFunction("MICROSECOND", instance, typeof(int), _intTypeMapping);
        var unsignedComponent = divisor == 1
            ? microseconds
            : TranslateTruncate(_sqlExpressionFactory.Divide(microseconds, _sqlExpressionFactory.Constant(divisor)));

        var component = _sqlExpressionFactory.Modulo(unsignedComponent, _sqlExpressionFactory.Constant(modulus));
        var sign = TranslateFunction(
            "SIGN",
            TranslateFunction("TIME_TO_SEC", instance, typeof(double), _doubleTypeMapping),
            typeof(int),
            _intTypeMapping);

        return _sqlExpressionFactory.Multiply(sign, component);
    }

    private SqlExpression TranslateTickComponent(
        SqlExpression ticks,
        long divisor,
        int modulus
    )
    {
        var component = _sqlExpressionFactory.Divide(ticks, _sqlExpressionFactory.Constant(divisor));

        if (modulus > 0)
        {
            component = _sqlExpressionFactory.Modulo(component, _sqlExpressionFactory.Constant(modulus));
        }

        return _sqlExpressionFactory.Convert(component, typeof(int), _intTypeMapping);
    }

    private SqlExpression TranslateMillisecond(
        SqlExpression instance
    ) => _sqlExpressionFactory.Convert(
        TranslateTruncate(
            _sqlExpressionFactory.Divide(
                TranslateFunction("MICROSECOND", instance, typeof(int), _intTypeMapping),
                _sqlExpressionFactory.Constant(1000))),
        typeof(int),
        _intTypeMapping);

    private SqlExpression TranslateSubMillisecondPart(
        SqlExpression instance,
        bool nanoseconds
    )
    {
        var microseconds = TranslateFunction("MICROSECOND", instance, typeof(int), _intTypeMapping);
        var precisionValue = nanoseconds
            ? _sqlExpressionFactory.Multiply(microseconds, _sqlExpressionFactory.Constant(1000, _intTypeMapping))
            : microseconds;

        return _sqlExpressionFactory.Modulo(precisionValue, _sqlExpressionFactory.Constant(1000, _intTypeMapping));
    }

    private SqlExpression DivideTotal(
        SqlExpression expression,
        long divisor
    ) => _sqlExpressionFactory.Divide(expression, _sqlExpressionFactory.Constant((double)divisor));

    private SqlExpression MultiplyTotal(
        SqlExpression expression,
        long multiplier
    ) => _sqlExpressionFactory.Multiply(expression, _sqlExpressionFactory.Constant((double)multiplier));

    private SqlExpression TranslateTruncate(
        SqlExpression expression
    ) => _sqlExpressionFactory.Function(
        "TRUNCATE",
        [
            expression,
            _sqlExpressionFactory.Constant(0),
        ],
        nullable: true,
        argumentsPropagateNullability:
        [
            true,
            false,
        ],
        expression.Type,
        expression.TypeMapping);

    private SqlExpression TranslateFunction(
        string functionName,
        SqlExpression argument,
        Type returnType,
        RelationalTypeMapping? typeMapping
    ) => _sqlExpressionFactory.Function(
        functionName,
        [argument],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        returnType,
        typeMapping);

    private SqlExpression IsDivisibleBy(
        SqlExpression value,
        int divisor
    ) => _sqlExpressionFactory.Equal(
        _sqlExpressionFactory.Modulo(value, _sqlExpressionFactory.Constant(divisor)),
        _sqlExpressionFactory.Constant(0));
}
