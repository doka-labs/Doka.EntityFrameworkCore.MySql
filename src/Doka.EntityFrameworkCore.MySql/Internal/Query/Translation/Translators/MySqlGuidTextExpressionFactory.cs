namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Builds GUID text from known storage representations without interpreting
/// arbitrary application converters as the provider's canonical format.
/// </summary>
internal sealed class MySqlGuidTextExpressionFactory
{
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _stringTypeMapping;

    public MySqlGuidTextExpressionFactory(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _stringTypeMapping = MySqlTranslationTypeMapping.GetRequired(typeMappingSource, typeof(string));
    }

    public SqlExpression Create(
        SqlExpression expression,
        bool preserveTextMapping = false
    )
    {
        expression = _sqlExpressionFactory.ApplyDefaultTypeMapping(expression);
        var mapping = expression.TypeMapping;

        if (mapping?.Converter?.GetType() == typeof(GuidToStringConverter))
        {
            return preserveTextMapping
                ? expression
                : _sqlExpressionFactory.Function(
                    "LOWER",
                    [expression],
                    nullable: true,
                    argumentsPropagateNullability: s_singleArgumentNullPropagation,
                    typeof(string),
                    _stringTypeMapping);
        }

        if (mapping is not MySqlGuidBinaryTypeMapping { Converter: null })
        {
            throw new InvalidOperationException(
                $"GUID text translation does not support store type '{mapping?.StoreType}' "
                + $"with converter '{mapping?.Converter?.GetType().FullName ?? "<none>"}'. "
                + "Use the provider's Binary16 or Char36 GUID mapping, or GuidToStringConverter.");
        }

        var hexadecimal = _sqlExpressionFactory.Function(
            "HEX",
            [expression],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(string),
            _stringTypeMapping);

        return _sqlExpressionFactory.Function(
            MySqlSentinelContract.GetName(MySqlSentinelKind.GuidToString),
            [hexadecimal],
            nullable: true,
            argumentsPropagateNullability: s_singleArgumentNullPropagation,
            typeof(string),
            _stringTypeMapping);
    }
}
