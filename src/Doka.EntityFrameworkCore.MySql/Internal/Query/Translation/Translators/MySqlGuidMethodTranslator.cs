namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates GUID generation and binary GUID formatting without routing the
/// provider's <c>BINARY(16)</c> representation through a text cast.
/// </summary>
internal sealed class MySqlGuidMethodTranslator : IMethodCallTranslator
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private static readonly MethodInfo s_newGuidMethod = typeof(Guid).GetRuntimeMethod(
        nameof(Guid.NewGuid),
        Type.EmptyTypes)!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _guidTypeMapping;
    private readonly RelationalTypeMapping _stringTypeMapping;

    public MySqlGuidMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _guidTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(Guid));
        _stringTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(string));
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (method == s_newGuidMethod)
        {
            var uuid = _sqlExpressionFactory.Function(
                "UUID",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                typeof(string),
                _stringTypeMapping);

            var hexadecimal = _sqlExpressionFactory.Function(
                "REPLACE",
                [
                    uuid,
                    _sqlExpressionFactory.Constant("-", _stringTypeMapping),
                    _sqlExpressionFactory.Constant(string.Empty, _stringTypeMapping),
                ],
                nullable: false,
                argumentsPropagateNullability:
                [
                    false,
                    false,
                    false,
                ],
                typeof(string),
                _stringTypeMapping);

            return _sqlExpressionFactory.Function(
                "UNHEX",
                [hexadecimal],
                nullable: false,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                typeof(Guid),
                _guidTypeMapping);
        }

        // EF can expose Guid.ToString() through the inherited object method.
        // The instance type and argument count identify the supported overload.
        if (instance?.Type != typeof(Guid)
            || method.Name != nameof(Guid.ToString)
            || arguments.Count != 0)
        {
            return null;
        }

        var hex = _sqlExpressionFactory.Function(
            "HEX",
            [instance],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(string),
            _stringTypeMapping);

        return _sqlExpressionFactory.Function(
            MySqlSentinelContract.GetName(MySqlSentinelKind.GuidToString),
            [hex],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(string),
            _stringTypeMapping);
    }
}
