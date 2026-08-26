namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates GUID generation and formatting according to the mapped binary
/// or textual storage representation.
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
    private readonly MySqlGuidTextExpressionFactory _guidTextExpressionFactory;

    public MySqlGuidMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        MySqlGuidTextExpressionFactory guidTextExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _guidTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(Guid));
        _stringTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(string));
        _guidTextExpressionFactory = guidTextExpressionFactory;
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
            if (_guidTypeMapping.Converter is GuidToStringConverter)
            {
                return _sqlExpressionFactory.Function(
                    "UUID",
                    Array.Empty<SqlExpression>(),
                    nullable: false,
                    argumentsPropagateNullability: Array.Empty<bool>(),
                    typeof(Guid),
                    _guidTypeMapping);
            }

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

        return _guidTextExpressionFactory.Create(instance);
    }
}
