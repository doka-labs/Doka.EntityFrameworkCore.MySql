namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates LINQ operations over relational <see cref="byte"/> arrays.
/// </summary>
internal sealed class MySqlByteArrayMethodTranslator : IMethodCallTranslator
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_twoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private static readonly MethodInfo s_containsMethod = typeof(Enumerable)
        .GetRuntimeMethods()
        .Single(method => method.Name == nameof(Enumerable.Contains)
            && method.GetParameters().Length == 2);

    private static readonly MethodInfo s_firstMethod = typeof(Enumerable)
        .GetRuntimeMethods()
        .Single(method => method.Name == nameof(Enumerable.First)
            && method.GetParameters().Length == 1);

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlByteArrayMethodTranslator(
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
        if (!method.IsGenericMethod)
        {
            return null;
        }

        var genericMethod = method.GetGenericMethodDefinition();

        if (genericMethod != s_containsMethod
            && genericMethod != s_firstMethod)
        {
            return null;
        }

        // Supported method signatures guarantee the source argument. Dispatching
        // first avoids indexing arguments for unrelated zero-argument methods.
        var source = arguments[0];

        if (source.Type != typeof(byte[])
            || source.TypeMapping is not { } byteArrayTypeMapping)
        {
            return null;
        }

        if (genericMethod == s_containsMethod)
        {
            var value = CreateSingleByteValue(arguments[1], byteArrayTypeMapping);
            var locate = _sqlExpressionFactory.Function(
                "LOCATE",
                [
                    value,
                    source,
                ],
                nullable: true,
                argumentsPropagateNullability: s_twoArgumentNullPropagation,
                typeof(int));

            return _sqlExpressionFactory.GreaterThan(locate, _sqlExpressionFactory.Constant(0));
        }

        return _sqlExpressionFactory.Function(
            "ASCII",
            [source],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(byte));
    }

    /// <summary>
    /// Produces the one-byte binary value expected by <c>LOCATE</c> without
    /// formatting runtime byte values as their decimal text.
    /// </summary>
    private SqlExpression CreateSingleByteValue(
        SqlExpression value,
        RelationalTypeMapping byteArrayTypeMapping
    )
    {
        if (value is SqlConstantExpression { Value: byte constant })
        {
            return _sqlExpressionFactory.Constant(new[] { constant }, byteArrayTypeMapping);
        }

        var hexadecimal = _sqlExpressionFactory.Function(
            "HEX",
            [value],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(string));

        var padded = _sqlExpressionFactory.Function(
            "LPAD",
            [
                hexadecimal,
                _sqlExpressionFactory.Constant(2),
                _sqlExpressionFactory.Constant("0"),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                false,
                false,
            ],
            typeof(string));

        return _sqlExpressionFactory.Function(
            "UNHEX",
            [padded],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(byte[]),
            byteArrayTypeMapping);
    }
}
