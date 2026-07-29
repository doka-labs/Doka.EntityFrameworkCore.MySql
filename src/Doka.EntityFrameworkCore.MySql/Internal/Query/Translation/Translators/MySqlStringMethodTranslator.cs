namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates string and character overloads that require MySQL-specific
/// indexing, trimming, regular-expression, or LINQ-element semantics.
/// </summary>
internal sealed class MySqlStringMethodTranslator : IMethodCallTranslator
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_twoArgumentNullPropagation =
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

    private static readonly MethodInfo s_containsCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Contains),
        [typeof(char)])!;

    private static readonly MethodInfo s_startsWithCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.StartsWith),
        [typeof(char)])!;

    private static readonly MethodInfo s_endsWithCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.EndsWith),
        [typeof(char)])!;

    private static readonly MethodInfo s_replaceCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Replace),
        [
            typeof(char),
            typeof(char),
        ])!;

    private static readonly MethodInfo s_indexOfStringWithStartMethod = typeof(string).GetRuntimeMethod(
        nameof(string.IndexOf),
        [
            typeof(string),
            typeof(int),
        ])!;

    private static readonly MethodInfo s_indexOfCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.IndexOf),
        [typeof(char)])!;

    private static readonly MethodInfo s_indexOfCharWithStartMethod = typeof(string).GetRuntimeMethod(
        nameof(string.IndexOf),
        [
            typeof(char),
            typeof(int),
        ])!;

    private static readonly MethodInfo s_isNullOrWhiteSpaceMethod = typeof(string).GetRuntimeMethod(
        nameof(string.IsNullOrWhiteSpace),
        [typeof(string)])!;

    private static readonly MethodInfo s_trimCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Trim),
        [typeof(char)])!;

    private static readonly MethodInfo s_trimCharsMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Trim),
        [typeof(char[])])!;

    private static readonly MethodInfo s_trimStartCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.TrimStart),
        [typeof(char)])!;

    private static readonly MethodInfo s_trimStartCharsMethod = typeof(string).GetRuntimeMethod(
        nameof(string.TrimStart),
        [typeof(char[])])!;

    private static readonly MethodInfo s_trimEndCharMethod = typeof(string).GetRuntimeMethod(
        nameof(string.TrimEnd),
        [typeof(char)])!;

    private static readonly MethodInfo s_trimEndCharsMethod = typeof(string).GetRuntimeMethod(
        nameof(string.TrimEnd),
        [typeof(char[])])!;

    private static readonly MethodInfo s_firstOrDefaultMethod = typeof(Enumerable)
        .GetRuntimeMethods()
        .Single(method => method.Name == nameof(Enumerable.FirstOrDefault)
            && method.GetParameters().Length == 1);

    private static readonly MethodInfo s_lastOrDefaultMethod = typeof(Enumerable)
        .GetRuntimeMethods()
        .Single(method => method.Name == nameof(Enumerable.LastOrDefault)
            && method.GetParameters().Length == 1);

    private static readonly MethodInfo s_regexIsMatchMethod = typeof(Regex).GetRuntimeMethod(
        nameof(Regex.IsMatch),
        [
            typeof(string),
            typeof(string),
        ])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlStringMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (method == s_isNullOrWhiteSpaceMethod)
        {
            var trimmed = TranslateSingleArgumentFunction(
                "TRIM",
                arguments[0],
                typeof(string),
                arguments[0].TypeMapping);

            return _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.IsNull(arguments[0]),
                _sqlExpressionFactory.Equal(trimmed, _sqlExpressionFactory.Constant(string.Empty)));
        }

        if (method == s_regexIsMatchMethod)
        {
            return _sqlExpressionFactory.Function(
                "__mysql_regexp",
                arguments,
                nullable: true,
                argumentsPropagateNullability: s_twoArgumentNullPropagation,
                typeof(bool));
        }

        if (IsCharacterEnumerableMethod(method, s_firstOrDefaultMethod))
        {
            return TranslateCharacterAt(arguments[0], _sqlExpressionFactory.Constant(1));
        }

        if (IsCharacterEnumerableMethod(method, s_lastOrDefaultMethod))
        {
            var lastIndex = TranslateSingleArgumentFunction(
                "CHAR_LENGTH",
                arguments[0],
                typeof(int),
                typeMapping: null);

            return TranslateCharacterAt(arguments[0], lastIndex);
        }

        if (instance is null)
        {
            return null;
        }

        if (method == s_containsCharMethod)
        {
            var locate = TranslateLocate(arguments[0], instance, startIndex: null);
            return _sqlExpressionFactory.GreaterThan(locate, _sqlExpressionFactory.Constant(0));
        }

        if (method == s_startsWithCharMethod)
        {
            return _sqlExpressionFactory.Equal(
                TranslateCharacterAt(instance, _sqlExpressionFactory.Constant(1)),
                arguments[0]);
        }

        if (method == s_endsWithCharMethod)
        {
            var lastIndex = TranslateSingleArgumentFunction("CHAR_LENGTH", instance, typeof(int), typeMapping: null);

            return _sqlExpressionFactory.Equal(TranslateCharacterAt(instance, lastIndex), arguments[0]);
        }

        if (method == s_replaceCharMethod)
        {
            return TranslateThreeArgumentFunction(
                "REPLACE",
                instance,
                arguments[0],
                arguments[1],
                typeof(string),
                instance.TypeMapping);
        }

        if (method == s_indexOfStringWithStartMethod
            || method == s_indexOfCharWithStartMethod)
        {
            return _sqlExpressionFactory.Subtract(
                TranslateLocate(arguments[0], instance, arguments[1]),
                _sqlExpressionFactory.Constant(1));
        }

        if (method == s_indexOfCharMethod)
        {
            return _sqlExpressionFactory.Subtract(
                TranslateLocate(arguments[0], instance, startIndex: null),
                _sqlExpressionFactory.Constant(1));
        }

        if (method == s_trimCharMethod
            || method == s_trimCharsMethod)
        {
            return TranslateTrim(instance, arguments[0], TrimMode.Both);
        }

        if (method == s_trimStartCharMethod
            || method == s_trimStartCharsMethod)
        {
            return TranslateTrim(instance, arguments[0], TrimMode.Start);
        }

        if (method == s_trimEndCharMethod
            || method == s_trimEndCharsMethod)
        {
            return TranslateTrim(instance, arguments[0], TrimMode.End);
        }

        return null;
    }

    private SqlExpression TranslateLocate(
        SqlExpression value,
        SqlExpression instance,
        SqlExpression? startIndex
    )
    {
        if (startIndex is null)
        {
            return _sqlExpressionFactory.Function(
                "LOCATE",
                [
                    value,
                    instance,
                ],
                nullable: true,
                argumentsPropagateNullability: s_twoArgumentNullPropagation,
                typeof(int));
        }

        return _sqlExpressionFactory.Function(
            "LOCATE",
            [
                value,
                instance,
                _sqlExpressionFactory.Add(startIndex, _sqlExpressionFactory.Constant(1)),
            ],
            nullable: true,
            argumentsPropagateNullability: s_threeArgumentNullPropagation,
            typeof(int));
    }

    private SqlExpression TranslateCharacterAt(
        SqlExpression instance,
        SqlExpression oneBasedIndex
    ) => _sqlExpressionFactory.Function(
        "SUBSTRING",
        [
            instance,
            oneBasedIndex,
            _sqlExpressionFactory.Constant(1),
        ],
        nullable: true,
        argumentsPropagateNullability:
        [
            true,
            true,
            false,
        ],
        typeof(char));

    /// <summary>
    /// Uses a character-class regular expression because MySQL's native
    /// <c>TRIM(remstr FROM value)</c> removes a repeated substring, whereas .NET
    /// removes any run of the supplied characters.
    /// </summary>
    private SqlExpression? TranslateTrim(
        SqlExpression instance,
        SqlExpression trimCharacters,
        TrimMode mode
    )
    {
        var characters = trimCharacters switch
        {
            SqlConstantExpression { Value: char character } => [character],
            SqlConstantExpression { Value: char[] array } => array,
            _ => null,
        };

        if (characters is null)
        {
            return null;
        }

        if (characters.Length == 0)
        {
            return TranslateSingleArgumentFunction("TRIM", instance, typeof(string), instance.TypeMapping);
        }

        var characterClass = EscapeCharacterClass(characters);
        var pattern = mode switch
        {
            TrimMode.Start => $"^[{characterClass}]+",
            TrimMode.End => $"[{characterClass}]+$",
            _ => $"^[{characterClass}]+|[{characterClass}]+$",
        };

        return TranslateThreeArgumentFunction(
            "REGEXP_REPLACE",
            instance,
            _sqlExpressionFactory.Constant(pattern),
            _sqlExpressionFactory.Constant(string.Empty),
            typeof(string),
            instance.TypeMapping);
    }

    private SqlExpression TranslateSingleArgumentFunction(
        string functionName,
        SqlExpression argument,
        Type resultType,
        RelationalTypeMapping? typeMapping
    ) => _sqlExpressionFactory.Function(
        functionName,
        [argument],
        nullable: true,
        argumentsPropagateNullability: s_singleArgumentNullPropagation,
        resultType,
        typeMapping);

    private SqlExpression TranslateThreeArgumentFunction(
        string functionName,
        SqlExpression first,
        SqlExpression second,
        SqlExpression third,
        Type resultType,
        RelationalTypeMapping? typeMapping
    ) => _sqlExpressionFactory.Function(
        functionName,
        [
            first,
            second,
            third,
        ],
        nullable: true,
        argumentsPropagateNullability: s_threeArgumentNullPropagation,
        resultType,
        typeMapping);

    private static string EscapeCharacterClass(
        IEnumerable<char> characters
    )
    {
        var builder = new StringBuilder();

        foreach (var character in characters)
        {
            if (character is '\\' or ']' or '^' or '-')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsCharacterEnumerableMethod(
        MethodInfo method,
        MethodInfo genericMethodDefinition
    ) => method.IsGenericMethod
        && method.GetGenericMethodDefinition() == genericMethodDefinition
        && method.GetGenericArguments() is [var elementType]
        && elementType == typeof(char);

    private enum TrimMode
    {
        Both,
        Start,
        End,
    }
}
