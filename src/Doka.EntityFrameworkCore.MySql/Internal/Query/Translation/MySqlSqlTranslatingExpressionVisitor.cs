namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds MySQL-family scalar translations that require access to the relational
/// expression visitor rather than the method-call translator pipeline.
/// </summary>
/// <remarks>
/// MySQL and MariaDB both provide native variadic <c>LEAST</c> and
/// <c>GREATEST</c> functions, and both return <see langword="null"/> when any
/// argument is <see langword="null"/>. Sources retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/comparison-operators.html">
/// MySQL 8.4 comparison functions</see>,
/// <see href="https://mariadb.com/docs/server/reference/sql-structure/operators/comparison-operators/least">
/// MariaDB LEAST</see>, and
/// <see href="https://mariadb.com/docs/server/reference/sql-structure/operators/comparison-operators/greatest">
/// MariaDB GREATEST</see>.
///
/// DateTime subtraction is translated through a private tick-valued SQL sentinel.
/// The SQL generator emits signed <c>TIMESTAMPDIFF(MICROSECOND) * 10</c>, which avoids
/// the database <c>TIME</c> range while preserving the engines' six-digit temporal
/// precision.
/// </remarks>
internal sealed class MySqlSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    private static readonly MethodInfo s_stringJoinArrayMethod = typeof(string).GetRuntimeMethod(
        nameof(string.Join),
        [
            typeof(string),
            typeof(string[]),
        ])!;

    private static readonly MethodInfo s_convertObjectToStringMethod = typeof(Convert).GetRuntimeMethod(
        nameof(Convert.ToString),
        [typeof(object)])!;

    private static readonly MethodInfo s_enumerableElementAtMethod = typeof(Enumerable)
        .GetRuntimeMethods()
        .Single(
            method => method.Name == nameof(Enumerable.ElementAt)
                && method.IsGenericMethodDefinition
                && method.GetParameters() is
                [
                    _,
                { ParameterType: var indexType, },
                ]
                && indexType == typeof(int));

    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly bool[] s_twoArgumentNullPropagation =
    [
        true,
        true,
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public MySqlSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor
    ) : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
        _typeMappingSource = dependencies.TypeMappingSource;
    }

    /// <summary>
    /// Translates relational byte-array length before the base visitor treats it as
    /// an unsupported CLR array operation.
    /// </summary>
    protected override Expression VisitUnary(
        UnaryExpression unaryExpression
    )
    {
        if (unaryExpression.NodeType is ExpressionType.Not or ExpressionType.OnesComplement
            && IsSignedIntegralType(unaryExpression.Type))
        {
            if (Visit(unaryExpression.Operand) is not SqlExpression operand)
            {
                return QueryCompilationContext.NotTranslatedExpression;
            }

            return _sqlExpressionFactory.Function(
                "__mysql_ones_complement",
                [operand],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                unaryExpression.Type,
                operand.TypeMapping);
        }

        if (unaryExpression.NodeType == ExpressionType.ArrayLength
            && unaryExpression.Operand.Type == typeof(byte[]))
        {
            if (Visit(unaryExpression.Operand) is not SqlExpression operand)
            {
                return QueryCompilationContext.NotTranslatedExpression;
            }

            return _sqlExpressionFactory.Function(
                "LENGTH",
                [operand],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                typeof(int));
        }

        return base.VisitUnary(unaryExpression);
    }

    /// <inheritdoc />
    public override SqlExpression? GenerateGreatest(
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    ) => GenerateComparisonFunction("GREATEST", expressions, resultType);

    /// <inheritdoc />
    public override SqlExpression? GenerateLeast(
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    ) => GenerateComparisonFunction("LEAST", expressions, resultType);

    /// <summary>
    /// Preserves promoted coalesce result mappings, translates <see cref="DateTime"/>
    /// subtraction to the provider's tick-valued SQL sentinel, and delegates every
    /// other binary expression to EF Core.
    /// </summary>
    protected override Expression VisitBinary(
        BinaryExpression binaryExpression
    )
    {
        if (binaryExpression.NodeType == ExpressionType.ArrayIndex
            && binaryExpression.Left.Type == typeof(byte[]))
        {
            return TranslateByteArrayElementAccess(
                binaryExpression.Left,
                binaryExpression.Right);
        }

        if (binaryExpression.NodeType is ExpressionType.LeftShift or ExpressionType.RightShift)
        {
            return TranslateShift(binaryExpression);
        }

        if (HasPromotedCoalesceOperand(binaryExpression)
            && TranslatePromotedCoalesce(binaryExpression) is { } coalesce)
        {
            return coalesce;
        }

        if (!IsDateTimeDifference(binaryExpression)
            && !IsTimeOnlyDifference(binaryExpression))
        {
            return base.VisitBinary(binaryExpression);
        }

        var left = Visit(binaryExpression.Left);
        var right = Visit(binaryExpression.Right);

        if (left is not SqlExpression leftSql
            || right is not SqlExpression rightSql)
        {
            return QueryCompilationContext.NotTranslatedExpression;
        }

        if (IsTimeOnlyDifference(binaryExpression))
        {
            return _sqlExpressionFactory.Function(
                "__mysql_time_diff_ticks",
                [
                    leftSql,
                    rightSql,
                ],
                nullable: true,
                argumentsPropagateNullability: s_twoArgumentNullPropagation,
                typeof(TimeSpan),
                MySqlTimeSpanTicksTypeMapping.Default);
        }

        return _sqlExpressionFactory.Function(
            "__mysql_datetime_diff_ticks",
            [
                rightSql,
                leftSql,
            ],
            nullable: true,
            argumentsPropagateNullability: s_twoArgumentNullPropagation,
            binaryExpression.Type,
            MySqlTimeSpanTicksTypeMapping.Default);
    }

    /// <summary>
    /// Translates non-aggregate <see cref="string.Join(string?, string?[])"/> calls
    /// whose inline array contains columns or parameters. EF Core cannot represent
    /// such an array as a scalar SQL value, so each element is translated separately.
    /// </summary>
    protected override Expression VisitMethodCall(
        MethodCallExpression methodCallExpression
    )
    {
        if (methodCallExpression.Method.IsGenericMethod
            && methodCallExpression.Method.GetGenericMethodDefinition() == s_enumerableElementAtMethod
            && methodCallExpression.Arguments[0].Type == typeof(byte[]))
        {
            return TranslateByteArrayElementAccess(
                methodCallExpression.Arguments[0],
                methodCallExpression.Arguments[1]);
        }

        if (methodCallExpression.Method == s_convertObjectToStringMethod
            && methodCallExpression.Arguments[0] is UnaryExpression
            {
                NodeType: ExpressionType.Convert, Type: { } type,
            } conversion
            && type == typeof(object))
        {
            return Visit(conversion.Operand);
        }

        if (methodCallExpression.Method != s_stringJoinArrayMethod
            || methodCallExpression.Arguments[1] is not NewArrayExpression newArray)
        {
            return base.VisitMethodCall(methodCallExpression);
        }

        if (Visit(methodCallExpression.Arguments[0]) is not SqlExpression separator)
        {
            return QueryCompilationContext.NotTranslatedExpression;
        }

        var arguments = new List<SqlExpression>(newArray.Expressions.Count + 1)
        {
            separator,
        };

        foreach (var element in newArray.Expressions)
        {
            if (Visit(element) is not SqlExpression translatedElement)
            {
                return QueryCompilationContext.NotTranslatedExpression;
            }

            arguments.Add(
                _sqlExpressionFactory.Coalesce(translatedElement, _sqlExpressionFactory.Constant(string.Empty)));
        }

        var nullPropagation = new bool[arguments.Count];
        nullPropagation[0] = true;

        return _sqlExpressionFactory.Function(
            "CONCAT_WS",
            arguments,
            nullable: true,
            argumentsPropagateNullability: nullPropagation,
            typeof(string),
            _typeMappingSource.FindMapping(typeof(string)));
    }

    private Expression TranslateByteArrayElementAccess(
        Expression arrayExpression,
        Expression indexExpression
    )
    {
        if (Visit(arrayExpression) is not SqlExpression array
            || Visit(indexExpression) is not SqlExpression index)
        {
            return QueryCompilationContext.NotTranslatedExpression;
        }

        var element = _sqlExpressionFactory.Function(
            "SUBSTRING",
            [
                array,
                _sqlExpressionFactory.Add(index, _sqlExpressionFactory.Constant(1)),
                _sqlExpressionFactory.Constant(1),
            ],
            nullable: true,
            argumentsPropagateNullability:
            [
                true,
                true,
                false,
            ],
            typeof(byte[]),
            array.TypeMapping);

        return _sqlExpressionFactory.Function(
            "ASCII",
            [element],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(byte));
    }

    private Expression TranslateShift(
        BinaryExpression binaryExpression
    )
    {
        if (Visit(binaryExpression.Left) is not SqlExpression left
            || Visit(binaryExpression.Right) is not SqlExpression right)
        {
            return QueryCompilationContext.NotTranslatedExpression;
        }

        return _sqlExpressionFactory.Function(
            binaryExpression.NodeType == ExpressionType.LeftShift ? "__mysql_left_shift" : "__mysql_right_shift",
            [
                left,
                right,
            ],
            nullable: true,
            argumentsPropagateNullability: s_twoArgumentNullPropagation,
            binaryExpression.Type,
            left.TypeMapping);
    }

    private static bool IsSignedIntegralType(
        Type type
    )
    {
        type = type.UnwrapNullableType();

        return type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long);
    }

    /// <summary>
    /// Preserves the explicit CLR conversion carried by a coalesce expression
    /// instead of letting EF Core infer the result mapping from its left operand.
    /// </summary>
    /// <remarks>
    /// A coalesce such as <c>uint? ?? double</c> stores its numeric promotion in a
    /// conversion around the nullable left operand. The relational base visitor removes
    /// that promotion before creating <c>COALESCE</c>, which would otherwise make the
    /// result reader use the unsigned-integer mapping and truncate fractional values.
    /// </remarks>
    private SqlExpression? TranslatePromotedCoalesce(
        BinaryExpression binaryExpression
    )
    {
        if (Visit(binaryExpression.Left) is not SqlExpression left
            || Visit(binaryExpression.Right) is not SqlExpression right)
        {
            return null;
        }

        var resultType = binaryExpression.Type.UnwrapNullableType();
        var resultTypeMapping = _typeMappingSource.FindMapping(resultType);
        if (resultTypeMapping is null)
        {
            return null;
        }

        var convertedLeft = _sqlExpressionFactory.Convert(left, resultType, resultTypeMapping);
        var convertedRight = right.Type == resultType
            ? _sqlExpressionFactory.ApplyTypeMapping(right, resultTypeMapping)
            : _sqlExpressionFactory.Convert(right, resultType, resultTypeMapping);

        return _sqlExpressionFactory.Coalesce(convertedLeft, convertedRight, resultTypeMapping);
    }

    private static bool HasPromotedCoalesceOperand(
        BinaryExpression binaryExpression
    )
    {
        if (binaryExpression.NodeType != ExpressionType.Coalesce)
        {
            return false;
        }

        var resultType = binaryExpression.Type.UnwrapNullableType();
        var leftSourceType = binaryExpression.Left.Type.UnwrapNullableType();

        if (binaryExpression.Left is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            } conversion)
        {
            leftSourceType = conversion.Operand.Type.UnwrapNullableType();
        }

        return leftSourceType != resultType;
    }

    private SqlExpression GenerateComparisonFunction(
        string functionName,
        IReadOnlyList<SqlExpression> expressions,
        Type resultType
    )
    {
        var resultTypeMapping = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions.InferTypeMapping(expressions);

        return _sqlExpressionFactory.Function(
            functionName,
            expressions,
            nullable: true,
            argumentsPropagateNullability: Enumerable.Repeat(true, expressions.Count),
            resultType,
            resultTypeMapping);
    }

    private static bool IsDateTimeDifference(
        BinaryExpression binaryExpression
    ) => binaryExpression.NodeType == ExpressionType.Subtract
        && binaryExpression.Left.Type.UnwrapNullableType() == typeof(DateTime)
        && binaryExpression.Right.Type.UnwrapNullableType() == typeof(DateTime)
        && binaryExpression.Type.UnwrapNullableType() == typeof(TimeSpan);

    private static bool IsTimeOnlyDifference(
        BinaryExpression binaryExpression
    ) => binaryExpression.NodeType == ExpressionType.Subtract
        && binaryExpression.Left.Type.UnwrapNullableType() == typeof(TimeOnly)
        && binaryExpression.Right.Type.UnwrapNullableType() == typeof(TimeOnly)
        && binaryExpression.Type.UnwrapNullableType() == typeof(TimeSpan);
}
