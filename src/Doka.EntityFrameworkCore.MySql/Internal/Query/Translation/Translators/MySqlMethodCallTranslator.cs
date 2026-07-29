namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMethodCallTranslator : IMethodCallTranslator
{
    private static readonly RelationalTypeMapping s_intTypeMapping = new IntTypeMapping("int", DbType.Int32);
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_roundTwoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private static readonly bool[] s_threeArgumentNullPropagation =
    [
        true,
        true,
        true,
    ];

    private static readonly bool[] s_truncateNullPropagation =
    [
        true,
        false,
    ];

    private static readonly MethodInfo s_stringIsNullOrEmptyMethod = typeof(string).GetRuntimeMethod(
        nameof(string.IsNullOrEmpty),
        [typeof(string)])!;

    private static readonly MethodInfo s_stringContainsMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Contains),
        [typeof(string)])!;

    private static readonly MethodInfo s_stringStartsWithMethod = typeof(string).GetRuntimeMethod(
        nameof(string.StartsWith),
        [typeof(string)])!;

    private static readonly MethodInfo s_stringEndsWithMethod = typeof(string).GetRuntimeMethod(
        nameof(string.EndsWith),
        [typeof(string)])!;

    private static readonly MethodInfo s_stringEqualsInstanceMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Equals),
        [typeof(string)])!;

    private static readonly MethodInfo s_stringEqualsStaticMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Equals),
        [
            typeof(string),
            typeof(string),
        ])!;

    // String instance methods.
    private static readonly MethodInfo s_substringOneArgMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;

    private static readonly MethodInfo s_substringTwoArgMethod =
        typeof(string).GetRuntimeMethod(
            nameof(string.Substring),
            [
                typeof(int),
                typeof(int),
            ])!;

    private static readonly MethodInfo s_replaceMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Replace),
        [
            typeof(string),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_toLowerMethod =
        typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo s_toUpperMethod =
        typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

    private static readonly MethodInfo s_trimMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Trim),
        Type.EmptyTypes)!;

    private static readonly MethodInfo s_trimStartMethod =
        typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;

    private static readonly MethodInfo s_trimEndMethod =
        typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;

    private static readonly MethodInfo s_indexOfMethod =
        typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string)])!;

    private static readonly MethodInfo s_padLeftMethod =
        typeof(string).GetRuntimeMethod(
            nameof(string.PadLeft),
            [
                typeof(int),
                typeof(char),
            ])!;

    private static readonly MethodInfo s_padRightMethod =
        typeof(string).GetRuntimeMethod(
            nameof(string.PadRight),
            [
                typeof(int),
                typeof(char),
            ])!;

    private static readonly MethodInfo s_stringConcatTwoArgMethod =
        typeof(string).GetRuntimeMethod(
            nameof(string.Concat),
            [
                typeof(string),
                typeof(string),
            ])!;

    private static readonly MethodInfo s_stringConcatThreeArgMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Concat),
        [
            typeof(string),
            typeof(string),
            typeof(string),
        ])!;

    // DateTime instance methods.
    private static readonly MethodInfo s_dateTimeAddYearsMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), [typeof(int)])!;

    private static readonly MethodInfo s_dateTimeAddMonthsMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), [typeof(int)])!;

    private static readonly MethodInfo s_dateTimeAddDaysMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), [typeof(double)])!;

    private static readonly MethodInfo s_dateTimeAddHoursMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), [typeof(double)])!;

    private static readonly MethodInfo s_dateTimeAddMinutesMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), [typeof(double)])!;

    private static readonly MethodInfo s_dateTimeAddSecondsMethod =
        typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), [typeof(double)])!;

    // DateOnly instance methods.
    private static readonly MethodInfo s_dateOnlyAddDaysMethod =
        typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddDays), [typeof(int)])!;

    private static readonly MethodInfo s_dateOnlyAddMonthsMethod =
        typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddMonths), [typeof(int)])!;

    private static readonly MethodInfo s_dateOnlyAddYearsMethod =
        typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.AddYears), [typeof(int)])!;

    // TimeOnly instance methods.
    private static readonly MethodInfo s_timeOnlyAddHoursMethod =
        typeof(TimeOnly).GetRuntimeMethod(nameof(TimeOnly.AddHours), [typeof(double)])!;

    private static readonly MethodInfo s_timeOnlyAddMinutesMethod =
        typeof(TimeOnly).GetRuntimeMethod(nameof(TimeOnly.AddMinutes), [typeof(double)])!;

    private static readonly MethodInfo s_timeOnlyAddMethod =
        typeof(TimeOnly).GetRuntimeMethod(nameof(TimeOnly.Add), [typeof(TimeSpan)])!;

    // EF.Functions extension methods.
    private static readonly MethodInfo s_regexpMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.Regexp),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_matchMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.Match),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_matchBooleanMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.MatchInBooleanMode),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
        ])!;

    // JSON function extension methods.
    private static readonly MethodInfo s_jsonSetMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonSet),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
            typeof(object),
        ])!;

    private static readonly MethodInfo s_jsonReplaceMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonReplace),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
            typeof(object),
        ])!;

    private static readonly MethodInfo s_jsonRemoveMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonRemove),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_jsonArrayMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonArray),
        [
            typeof(DbFunctions),
            typeof(object[]),
        ])!;

    private static readonly MethodInfo s_jsonObjectMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonObject),
        [
            typeof(DbFunctions),
            typeof(object[]),
        ])!;

    private static readonly MethodInfo s_jsonDepthMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonDepth),
        [
            typeof(DbFunctions),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_jsonLengthMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonLength),
        [
            typeof(DbFunctions),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_jsonTypeMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonType),
        [
            typeof(DbFunctions),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_jsonKeysMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonKeys),
        [
            typeof(DbFunctions),
            typeof(string),
        ])!;

    private static readonly MethodInfo s_jsonContainsMethod = typeof(MySqlDbFunctionsExtensions).GetRuntimeMethod(
        nameof(MySqlDbFunctionsExtensions.JsonContains),
        [
            typeof(DbFunctions),
            typeof(string),
            typeof(string),
        ])!;

    // Math methods.
    private static readonly HashSet<MethodInfo> s_absMethods = CreateMathMethodSet(nameof(Math.Abs));
    private static readonly HashSet<MethodInfo> s_ceilingMethods = CreateMathMethodSet(nameof(Math.Ceiling));
    private static readonly HashSet<MethodInfo> s_floorMethods = CreateMathMethodSet(nameof(Math.Floor));
    private static readonly HashSet<MethodInfo> s_roundMethods = CreateMathMethodSet(nameof(Math.Round));
    private static readonly HashSet<MethodInfo> s_truncateMethods = CreateMathMethodSet(nameof(Math.Truncate));
    private static readonly HashSet<MethodInfo> s_powMethods = CreateMathMethodSet(nameof(Math.Pow));
    private static readonly HashSet<MethodInfo> s_sqrtMethods = CreateMathMethodSet(nameof(Math.Sqrt));
    private static readonly HashSet<MethodInfo> s_logMethods = CreateMathMethodSet(nameof(Math.Log));
    private static readonly HashSet<MethodInfo> s_log10Methods = CreateMathMethodSet(nameof(Math.Log10));
    private static readonly HashSet<MethodInfo> s_expMethods = CreateMathMethodSet(nameof(Math.Exp));
    private static readonly HashSet<MethodInfo> s_signMethods = CreateMathMethodSet(nameof(Math.Sign));
    private static readonly HashSet<MethodInfo> s_sinMethods = CreateMathMethodSet(nameof(Math.Sin));
    private static readonly HashSet<MethodInfo> s_cosMethods = CreateMathMethodSet(nameof(Math.Cos));
    private static readonly HashSet<MethodInfo> s_tanMethods = CreateMathMethodSet(nameof(Math.Tan));
    private static readonly HashSet<MethodInfo> s_atan2Methods = CreateMathMethodSet(nameof(Math.Atan2));
    private static readonly HashSet<MethodInfo> s_maxMethods = CreateMathMethodSet(nameof(Math.Max));
    private static readonly HashSet<MethodInfo> s_minMethods = CreateMathMethodSet(nameof(Math.Min));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlMethodCallTranslator(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(logger);

        if (method == s_stringIsNullOrEmptyMethod)
        {
            return TranslateStringIsNullOrEmpty(arguments[0]);
        }

        if (method == s_stringContainsMethod
            && instance is not null)
        {
            return TranslateContains(instance, arguments[0]);
        }

        if (method == s_stringStartsWithMethod
            && instance is not null)
        {
            return TranslateStartsWith(instance, arguments[0]);
        }

        if (method == s_stringEndsWithMethod
            && instance is not null)
        {
            return TranslateEndsWith(instance, arguments[0]);
        }

        if (method == s_stringEqualsInstanceMethod
            && instance is not null)
        {
            return _sqlExpressionFactory.Equal(instance, arguments[0]);
        }

        if (method == s_stringEqualsStaticMethod)
        {
            return _sqlExpressionFactory.Equal(arguments[0], arguments[1]);
        }

        // String instance methods.
        if (method == s_substringOneArgMethod
            && instance is not null)
        {
            return TranslateSubstring(instance, arguments[0], null);
        }

        if (method == s_substringTwoArgMethod
            && instance is not null)
        {
            return TranslateSubstring(instance, arguments[0], arguments[1]);
        }

        if (method == s_replaceMethod
            && instance is not null)
        {
            return TranslateThreeArgumentFunction(
                "REPLACE",
                instance,
                arguments[0],
                arguments[1],
                typeof(string),
                instance.TypeMapping);
        }

        if (method == s_toUpperMethod
            && instance is not null)
        {
            return TranslateSingleArgumentFunction("UPPER", instance, typeof(string));
        }

        if (method == s_toLowerMethod
            && instance is not null)
        {
            return TranslateSingleArgumentFunction("LOWER", instance, typeof(string));
        }

        if (method == s_trimMethod
            && instance is not null)
        {
            return TranslateSingleArgumentFunction("TRIM", instance, typeof(string));
        }

        if (method == s_trimStartMethod
            && instance is not null)
        {
            return TranslateSingleArgumentFunction("LTRIM", instance, typeof(string));
        }

        if (method == s_trimEndMethod
            && instance is not null)
        {
            return TranslateSingleArgumentFunction("RTRIM", instance, typeof(string));
        }

        if (method == s_indexOfMethod
            && instance is not null)
        {
            return TranslateIndexOf(instance, arguments[0]);
        }

        if (method == s_padLeftMethod
            && instance is not null)
        {
            return TranslateThreeArgumentFunction(
                "LPAD",
                instance,
                arguments[0],
                arguments[1],
                typeof(string),
                instance.TypeMapping);
        }

        if (method == s_padRightMethod
            && instance is not null)
        {
            return TranslateThreeArgumentFunction(
                "RPAD",
                instance,
                arguments[0],
                arguments[1],
                typeof(string),
                instance.TypeMapping);
        }

        // String static methods.
        if (method == s_stringConcatTwoArgMethod)
        {
            return TranslateTwoArgumentFunction("CONCAT", arguments[0], arguments[1], typeof(string));
        }

        if (method == s_stringConcatThreeArgMethod)
        {
            return TranslateThreeArgumentFunction(
                "CONCAT",
                arguments[0],
                arguments[1],
                arguments[2],
                typeof(string),
                null);
        }

        // EF.Functions extension methods.
        if (method == s_regexpMethod)
        {
            // EF.Functions.Regexp(input, pattern)
            // MySQL 8.0+: REGEXP_LIKE(input, pattern) -- scalar function
            // MariaDB: input REGEXP pattern -- infix operator (no REGEXP_LIKE function)
            // Use a sentinel name; MySqlQuerySqlGenerator rewrites to engine-appropriate SQL.
            return _sqlExpressionFactory.Function(
                "__mysql_regexp",
                [
                    arguments[1],
                    arguments[2],
                ],
                nullable: true,
                argumentsPropagateNullability:
                [
                    true,
                    true,
                ],
                typeof(bool));
        }

        if (method == s_matchMethod)
        {
            // EF.Functions.Match(column, term) -> MATCH(column) AGAINST(term)
            // Use a sentinel function name that MySqlQuerySqlGenerator recognizes and rewrites.
            return _sqlExpressionFactory.Function(
                "__mysql_match",
                [
                    arguments[1],
                    arguments[2],
                ],
                nullable: false,
                argumentsPropagateNullability:
                [
                    false,
                    false,
                ],
                typeof(bool));
        }

        if (method == s_matchBooleanMethod)
        {
            // EF.Functions.MatchInBooleanMode(column, term) -> MATCH(column) AGAINST(term IN BOOLEAN MODE)
            return _sqlExpressionFactory.Function(
                "__mysql_match_boolean",
                [
                    arguments[1],
                    arguments[2],
                ],
                nullable: false,
                argumentsPropagateNullability:
                [
                    false,
                    false,
                ],
                typeof(bool));
        }

        // JSON manipulation functions.
        if (method == s_jsonSetMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_SET",
                [
                    arguments[1],
                    arguments[2],
                    arguments[3],
                ],
                nullable: true,
                argumentsPropagateNullability: s_threeArgumentNullPropagation,
                typeof(string));
        }

        if (method == s_jsonReplaceMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_REPLACE",
                [
                    arguments[1],
                    arguments[2],
                    arguments[3],
                ],
                nullable: true,
                argumentsPropagateNullability: s_threeArgumentNullPropagation,
                typeof(string));
        }

        if (method == s_jsonRemoveMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_REMOVE",
                [
                    arguments[1],
                    arguments[2],
                ],
                nullable: true,
                argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
                typeof(string));
        }

        // JSON construction functions.
        if (method == s_jsonArrayMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_ARRAY",
                arguments
                    .Skip(1)
                    .ToArray(),
                nullable: false,
                argumentsPropagateNullability: arguments
                    .Skip(1)
                    .Select(_ => true)
                    .ToArray(),
                typeof(string));
        }

        if (method == s_jsonObjectMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_OBJECT",
                arguments
                    .Skip(1)
                    .ToArray(),
                nullable: false,
                argumentsPropagateNullability: arguments
                    .Skip(1)
                    .Select(_ => true)
                    .ToArray(),
                typeof(string));
        }

        // JSON inspection functions.
        if (method == s_jsonDepthMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_DEPTH",
                [
                    arguments[1],
                ],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                typeof(int),
                s_intTypeMapping);
        }

        if (method == s_jsonLengthMethod)
        {
            return _sqlExpressionFactory.Function(
                "JSON_LENGTH",
                [
                    arguments[1],
                ],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                typeof(int),
                s_intTypeMapping);
        }

        if (method == s_jsonTypeMethod)
        {
            return TranslateSingleArgumentFunction("JSON_TYPE", arguments[1], typeof(string));
        }

        if (method == s_jsonKeysMethod)
        {
            return TranslateSingleArgumentFunction("JSON_KEYS", arguments[1], typeof(string));
        }

        if (method == s_jsonContainsMethod)
        {
            return TranslateTwoArgumentFunction("JSON_CONTAINS", arguments[1], arguments[2], typeof(bool));
        }

        // DateTime methods -> DATE_ADD with INTERVAL.
        if (instance is not null)
        {
            if (method == s_dateTimeAddYearsMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "YEAR");
            }

            if (method == s_dateTimeAddMonthsMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "MONTH");
            }

            if (method == s_dateTimeAddDaysMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "DAY");
            }

            if (method == s_dateTimeAddHoursMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "HOUR");
            }

            if (method == s_dateTimeAddMinutesMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "MINUTE");
            }

            if (method == s_dateTimeAddSecondsMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "SECOND");
            }

            // DateOnly methods -> DATE_ADD with INTERVAL.
            if (method == s_dateOnlyAddDaysMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "DAY");
            }

            if (method == s_dateOnlyAddMonthsMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "MONTH");
            }

            if (method == s_dateOnlyAddYearsMethod)
            {
                return TranslateDateAdd(instance, arguments[0], "YEAR");
            }

            // TimeOnly methods -> ADDTIME.
            if (method == s_timeOnlyAddHoursMethod)
            {
                return TranslateTimeOnlyAdd(instance, arguments[0], "HOUR");
            }

            if (method == s_timeOnlyAddMinutesMethod)
            {
                return TranslateTimeOnlyAdd(instance, arguments[0], "MINUTE");
            }

            if (method == s_timeOnlyAddMethod)
            {
                return TranslateAddTime(instance, arguments[0]);
            }
        }

        // Math methods.
        if (s_absMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("ABS", arguments[0], method.ReturnType);
        }

        if (s_ceilingMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("CEILING", arguments[0], method.ReturnType);
        }

        if (s_floorMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("FLOOR", arguments[0], method.ReturnType);
        }

        if (s_roundMethods.Contains(method))
        {
            return TranslateRound(arguments, method.ReturnType);
        }

        if (s_truncateMethods.Contains(method))
        {
            return TranslateTruncate(arguments[0], method.ReturnType);
        }

        if (s_powMethods.Contains(method)
            && arguments.Count == 2)
        {
            return TranslateTwoArgumentFunction("POWER", arguments[0], arguments[1], method.ReturnType);
        }

        if (s_sqrtMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("SQRT", arguments[0], method.ReturnType);
        }

        if (s_logMethods.Contains(method))
        {
            return arguments.Count == 1
                ? TranslateSingleArgumentFunction("LN", arguments[0], method.ReturnType)
                : TranslateTwoArgumentFunction("LOG", arguments[1], arguments[0], method.ReturnType);
        }

        if (s_log10Methods.Contains(method))
        {
            return TranslateSingleArgumentFunction("LOG10", arguments[0], method.ReturnType);
        }

        if (s_expMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("EXP", arguments[0], method.ReturnType);
        }

        if (s_signMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("SIGN", arguments[0], method.ReturnType);
        }

        if (s_sinMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("SIN", arguments[0], method.ReturnType);
        }

        if (s_cosMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("COS", arguments[0], method.ReturnType);
        }

        if (s_tanMethods.Contains(method))
        {
            return TranslateSingleArgumentFunction("TAN", arguments[0], method.ReturnType);
        }

        if (s_atan2Methods.Contains(method)
            && arguments.Count == 2)
        {
            return TranslateTwoArgumentFunction("ATAN2", arguments[0], arguments[1], method.ReturnType);
        }

        if (s_maxMethods.Contains(method)
            && arguments.Count == 2)
        {
            return TranslateTwoArgumentFunction("GREATEST", arguments[0], arguments[1], method.ReturnType);
        }

        if (s_minMethods.Contains(method)
            && arguments.Count == 2)
        {
            return TranslateTwoArgumentFunction("LEAST", arguments[0], arguments[1], method.ReturnType);
        }

        return null;
    }

    private SqlExpression TranslateStringIsNullOrEmpty(
        SqlExpression argument
    ) => _sqlExpressionFactory.OrElse(
        _sqlExpressionFactory.IsNull(argument),
        _sqlExpressionFactory.Equal(argument, _sqlExpressionFactory.Constant(string.Empty)));

    private SqlExpression TranslateContains(
        SqlExpression instance,
        SqlExpression argument
    )
    {
        var locateExpression = _sqlExpressionFactory.Function(
            "LOCATE",
            [
                argument,
                instance,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            typeof(int),
            s_intTypeMapping);

        return _sqlExpressionFactory.GreaterThan(locateExpression, _sqlExpressionFactory.Constant(0));
    }

    private SqlExpression TranslateStartsWith(
        SqlExpression instance,
        SqlExpression argument
    )
    {
        var lengthExpression = CreateCharacterLengthExpression(argument);
        var leftExpression = _sqlExpressionFactory.Function(
            "LEFT",
            [
                instance,
                lengthExpression,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            typeof(string),
            instance.TypeMapping);

        return _sqlExpressionFactory.Equal(leftExpression, argument);
    }

    private SqlExpression TranslateEndsWith(
        SqlExpression instance,
        SqlExpression argument
    )
    {
        var lengthExpression = CreateCharacterLengthExpression(argument);
        var rightExpression = _sqlExpressionFactory.Function(
            "RIGHT",
            [
                instance,
                lengthExpression,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            typeof(string),
            instance.TypeMapping);

        return _sqlExpressionFactory.Equal(rightExpression, argument);
    }

    private SqlExpression TranslateSingleArgumentFunction(
        string functionName,
        SqlExpression argument,
        Type resultType
    ) => _sqlExpressionFactory.Function(
        functionName,
        [
            argument,
        ],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        resultType,
        argument.TypeMapping);

    private SqlExpression CreateCharacterLengthExpression(
        SqlExpression argument
    ) => _sqlExpressionFactory.Function(
        "CHAR_LENGTH",
        [
            argument,
        ],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        typeof(int),
        s_intTypeMapping);

    private SqlExpression? TranslateRound(
        IReadOnlyList<SqlExpression> arguments,
        Type resultType
    ) => arguments.Count switch
    {
        1 => _sqlExpressionFactory.Function(
            "ROUND",
            [
                arguments[0],
            ],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            resultType,
            arguments[0].TypeMapping),
        2 => _sqlExpressionFactory.Function(
            "ROUND",
            [
                arguments[0],
                arguments[1],
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            resultType,
            arguments[0].TypeMapping),
        _ => null,
    };

    private SqlExpression TranslateTruncate(
        SqlExpression argument,
        Type resultType
    ) => _sqlExpressionFactory.Function(
        "TRUNCATE",
        [
            argument,
            _sqlExpressionFactory.Constant(0),
        ],
        nullable: true,
        argumentsPropagateNullability: s_truncateNullPropagation,
        resultType,
        argument.TypeMapping);

    /// <summary>
    /// Translates DateTime.AddX(n) to the MySQL DATE_ADD function with INTERVAL syntax.
    /// Both constant and parametrized intervals route through the sentinel function name
    /// <c>__mysql_date_add_UNIT</c>; the QuerySqlGenerator rewrites the sentinel to
    /// <c>DATE_ADD(arg0, INTERVAL arg1 UNIT)</c> so a parametrized interval keeps its
    /// server-side evaluation path instead of falling back to client-side enumeration.
    /// </summary>
    private SqlExpression TranslateDateAdd(
        SqlExpression instance,
        SqlExpression interval,
        string unit
    ) => _sqlExpressionFactory.Function(
        $"__mysql_date_add_{unit}",
        [
            instance,
            interval,
        ],
        nullable: true,
        argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
        instance.Type,
        instance.TypeMapping);

    private SqlExpression TranslateSubstring(
        SqlExpression instance,
        SqlExpression startIndex,
        SqlExpression? length
    )
    {
        // MySQL SUBSTRING is 1-based; .NET string.Substring is 0-based.
        var oneBasedStart = _sqlExpressionFactory.Add(startIndex, _sqlExpressionFactory.Constant(1));

        if (length is null)
        {
            return _sqlExpressionFactory.Function(
                "SUBSTRING",
                [
                    instance,
                    oneBasedStart,
                ],
                nullable: true,
                argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
                typeof(string),
                instance.TypeMapping);
        }

        return _sqlExpressionFactory.Function(
            "SUBSTRING",
            [
                instance,
                oneBasedStart,
                length,
            ],
            nullable: true,
            argumentsPropagateNullability: s_threeArgumentNullPropagation,
            typeof(string),
            instance.TypeMapping);
    }

    private SqlExpression TranslateIndexOf(
        SqlExpression instance,
        SqlExpression argument
    )
    {
        // LOCATE returns 1-based position; .NET IndexOf returns 0-based. Returns -1 if not found.
        var locateExpression = _sqlExpressionFactory.Function(
            "LOCATE",
            [
                argument,
                instance,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            typeof(int),
            s_intTypeMapping);

        return _sqlExpressionFactory.Subtract(locateExpression, _sqlExpressionFactory.Constant(1));
    }

    private SqlExpression TranslateTwoArgumentFunction(
        string functionName,
        SqlExpression arg1,
        SqlExpression arg2,
        Type resultType
    )
    {
        return _sqlExpressionFactory.Function(
            functionName,
            [
                arg1,
                arg2,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            resultType,
            arg1.TypeMapping);
    }

    private SqlExpression TranslateThreeArgumentFunction(
        string functionName,
        SqlExpression arg1,
        SqlExpression arg2,
        SqlExpression arg3,
        Type resultType,
        RelationalTypeMapping? typeMapping
    )
    {
        return _sqlExpressionFactory.Function(
            functionName,
            [
                arg1,
                arg2,
                arg3,
            ],
            nullable: true,
            argumentsPropagateNullability: s_threeArgumentNullPropagation,
            resultType,
            typeMapping ?? arg1.TypeMapping);
    }

    /// <summary>
    /// Translates TimeOnly.AddHours / AddMinutes / AddSeconds to MySQL DATE_ADD with the
    /// INTERVAL keyword. Both constant and parametrized intervals route through the
    /// sentinel function name <c>__mysql_time_add_UNIT</c>; the QuerySqlGenerator rewrites
    /// the sentinel to <c>DATE_ADD(arg0, INTERVAL arg1 UNIT)</c>.
    /// </summary>
    private SqlExpression TranslateTimeOnlyAdd(
        SqlExpression instance,
        SqlExpression interval,
        string unit
    ) => _sqlExpressionFactory.Function(
        $"__mysql_time_add_{unit}",
        [
            instance,
            interval,
        ],
        nullable: true,
        argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
        instance.Type,
        instance.TypeMapping);

    /// <summary>
    /// Translates TimeOnly.Add(TimeSpan) to MySQL ADDTIME(time, timespan).
    /// </summary>
    private SqlExpression TranslateAddTime(
        SqlExpression instance,
        SqlExpression timeSpanArgument
    )
    {
        return _sqlExpressionFactory.Function(
            "ADDTIME",
            [
                instance,
                timeSpanArgument,
            ],
            nullable: true,
            argumentsPropagateNullability: s_roundTwoArgumentNullPropagation,
            instance.Type,
            instance.TypeMapping);
    }

    private static HashSet<MethodInfo> CreateMathMethodSet(
        string methodName
    )
    {
        var methods = typeof(Math)
            .GetRuntimeMethods()
            .Where(method => method.Name == methodName)
            .Concat(
                typeof(MathF)
                    .GetRuntimeMethods()
                    .Where(method => method.Name == methodName))
            .Where(method => method.GetParameters()
                .Length is 1 or 2);

        return [.. methods];
    }
}
