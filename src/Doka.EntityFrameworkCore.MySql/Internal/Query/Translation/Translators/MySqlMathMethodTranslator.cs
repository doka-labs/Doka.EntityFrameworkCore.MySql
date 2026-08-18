namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates the numeric functions exposed by <see cref="Math"/>,
/// <see cref="MathF"/>, <see cref="double"/>, and <see cref="float"/>.
/// </summary>
/// <remarks>
/// MySQL and MariaDB expose the shared elementary function set used here.
/// Hyperbolic functions that are not common to both engines are expressed through
/// equivalent <c>EXP</c>, <c>LOG</c>, and <c>SQRT</c> identities. Sources retrieved
/// 2026-07-29:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/mathematical-functions.html">
/// MySQL mathematical functions</see> and
/// <see href="https://mariadb.com/docs/server/reference/sql-functions/numeric-functions">
/// MariaDB numeric functions</see>.
/// </remarks>
internal sealed class MySqlMathMethodTranslator : IMethodCallTranslator
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_twoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlMathMethodTranslator(
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
        if (!IsSupportedDeclaringType(method.DeclaringType)
            || arguments.Count is < 1 or > 2)
        {
            return null;
        }

        return method.Name switch
        {
            nameof(Math.Abs) => TranslateFunction("ABS", arguments, method.ReturnType),
            nameof(Math.Acos) => TranslateFunction("ACOS", arguments, method.ReturnType),
            nameof(Math.Acosh) => TranslateAcosh(arguments[0], method.ReturnType),
            nameof(Math.Asin) => TranslateFunction("ASIN", arguments, method.ReturnType),
            nameof(Math.Asinh) => TranslateAsinh(arguments[0], method.ReturnType),
            nameof(Math.Atan) => TranslateFunction("ATAN", arguments, method.ReturnType),
            nameof(Math.Atan2) => TranslateFunction("ATAN2", arguments, method.ReturnType),
            nameof(Math.Atanh) => TranslateAtanh(arguments[0], method.ReturnType),
            nameof(Math.Ceiling) => TranslateFunction("CEILING", arguments, method.ReturnType),
            nameof(Math.Cos) => TranslateFunction("COS", arguments, method.ReturnType),
            nameof(Math.Cosh) => TranslateCosh(arguments[0], method.ReturnType),
            nameof(double.DegreesToRadians) => TranslateFunction("RADIANS", arguments, method.ReturnType),
            nameof(Math.Exp) => TranslateFunction("EXP", arguments, method.ReturnType),
            nameof(Math.Floor) => TranslateFunction("FLOOR", arguments, method.ReturnType),
            nameof(Math.Log) => TranslateLog(arguments, method.ReturnType),
            nameof(Math.Log10) => TranslateFunction("LOG10", arguments, method.ReturnType),
            nameof(Math.Log2) => TranslateFunction("LOG2", arguments, method.ReturnType),
            nameof(Math.Max) => TranslateFunction("GREATEST", arguments, method.ReturnType),
            nameof(Math.Min) => TranslateFunction("LEAST", arguments, method.ReturnType),
            nameof(Math.Pow) => TranslateFunction("POWER", arguments, method.ReturnType),
            nameof(double.RadiansToDegrees) => TranslateFunction("DEGREES", arguments, method.ReturnType),
            nameof(Math.Round) => TranslateFunction("ROUND", arguments, method.ReturnType),
            nameof(Math.Sign) => TranslateFunction("SIGN", arguments, method.ReturnType),
            nameof(Math.Sin) => TranslateFunction("SIN", arguments, method.ReturnType),
            nameof(Math.Sinh) => TranslateSinh(arguments[0], method.ReturnType),
            nameof(Math.Sqrt) => TranslateFunction("SQRT", arguments, method.ReturnType),
            nameof(Math.Tan) => TranslateFunction("TAN", arguments, method.ReturnType),
            nameof(Math.Tanh) => TranslateTanh(arguments[0], method.ReturnType),
            nameof(Math.Truncate) => TranslateTruncate(arguments[0], method.ReturnType),
            _ => null,
        };
    }

    private SqlExpression TranslateFunction(
        string functionName,
        IReadOnlyList<SqlExpression> arguments,
        Type resultType
    )
    {
        var nullPropagation = arguments.Count == 1 ? s_singleArgumentNullPropagation : s_twoArgumentNullPropagation;
        var functionArguments = arguments.Count == 1
            ? [arguments[0]]
            : new[]
            {
                arguments[0],
                arguments[1],
            };

        return _sqlExpressionFactory.Function(
            functionName,
            functionArguments,
            nullable: true,
            argumentsPropagateNullability: nullPropagation,
            resultType,
            arguments[0].TypeMapping);
    }

    private SqlExpression TranslateLog(
        IReadOnlyList<SqlExpression> arguments,
        Type resultType
    ) => arguments.Count == 1
        ? TranslateFunction("LN", arguments, resultType)
        : TranslateFunction(
            "LOG",
            [
                arguments[1],
                arguments[0],
            ],
            resultType);

    private SqlExpression TranslateTruncate(
        SqlExpression argument,
        Type resultType
    ) => TranslateFunction(
        "TRUNCATE",
        [
            argument,
            _sqlExpressionFactory.Constant(0),
        ],
        resultType);

    private SqlExpression TranslateSinh(
        SqlExpression argument,
        Type resultType
    )
    {
        var positive = TranslateFunction("EXP", [argument], resultType);
        var negative = TranslateFunction("EXP", [_sqlExpressionFactory.Negate(argument)], resultType);

        return _sqlExpressionFactory.Divide(
            _sqlExpressionFactory.Subtract(positive, negative),
            _sqlExpressionFactory.Constant(2.0));
    }

    private SqlExpression TranslateCosh(
        SqlExpression argument,
        Type resultType
    )
    {
        var positive = TranslateFunction("EXP", [argument], resultType);
        var negative = TranslateFunction("EXP", [_sqlExpressionFactory.Negate(argument)], resultType);

        return _sqlExpressionFactory.Divide(
            _sqlExpressionFactory.Add(positive, negative),
            _sqlExpressionFactory.Constant(2.0));
    }

    private SqlExpression TranslateTanh(
        SqlExpression argument,
        Type resultType
    )
    {
        var doubled = _sqlExpressionFactory.Multiply(argument, _sqlExpressionFactory.Constant(2.0));
        var exponential = TranslateFunction("EXP", [doubled], resultType);
        var one = _sqlExpressionFactory.Constant(1.0);

        return _sqlExpressionFactory.Divide(
            _sqlExpressionFactory.Subtract(exponential, one),
            _sqlExpressionFactory.Add(exponential, one));
    }

    private SqlExpression TranslateAsinh(
        SqlExpression argument,
        Type resultType
    )
    {
        var square = _sqlExpressionFactory.Multiply(argument, argument);
        var root = TranslateFunction(
            "SQRT",
            [_sqlExpressionFactory.Add(square, _sqlExpressionFactory.Constant(1.0))],
            resultType);

        return TranslateFunction("LOG", [_sqlExpressionFactory.Add(argument, root)], resultType);
    }

    private SqlExpression TranslateAcosh(
        SqlExpression argument,
        Type resultType
    )
    {
        var square = _sqlExpressionFactory.Multiply(argument, argument);
        var root = TranslateFunction(
            "SQRT",
            [_sqlExpressionFactory.Subtract(square, _sqlExpressionFactory.Constant(1.0))],
            resultType);

        return TranslateFunction("LOG", [_sqlExpressionFactory.Add(argument, root)], resultType);
    }

    private SqlExpression TranslateAtanh(
        SqlExpression argument,
        Type resultType
    )
    {
        var one = _sqlExpressionFactory.Constant(1.0);
        var quotient = _sqlExpressionFactory.Divide(
            _sqlExpressionFactory.Add(one, argument),
            _sqlExpressionFactory.Subtract(one, argument));

        var logarithm = TranslateFunction("LOG", [quotient], resultType);

        return _sqlExpressionFactory.Multiply(_sqlExpressionFactory.Constant(0.5), logarithm);
    }

    private static bool IsSupportedDeclaringType(
        Type? declaringType
    ) => declaringType == typeof(Math)
        || declaringType == typeof(MathF)
        || declaringType == typeof(double)
        || declaringType == typeof(float);
}
