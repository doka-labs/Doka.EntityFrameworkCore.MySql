namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates the scalar one-argument <see cref="Convert"/> methods supported by
/// EF Core's relational basic-types contract.
/// </summary>
internal sealed class MySqlConvertMethodTranslator : IMethodCallTranslator
{
    private static readonly FrozenSet<MethodInfo> s_supportedMethods = new[]
        {
            nameof(Convert.ToBoolean),
            nameof(Convert.ToByte),
            nameof(Convert.ToDecimal),
            nameof(Convert.ToDouble),
            nameof(Convert.ToInt16),
            nameof(Convert.ToInt32),
            nameof(Convert.ToInt64),
            nameof(Convert.ToString),
        }
        .SelectMany(methodName => typeof(Convert)
            .GetRuntimeMethods()
            .Where(method => method.Name == methodName
                && method.GetParameters().Length == 1))
        .ToFrozenSet();

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly MySqlObjectToStringTranslator _objectToStringTranslator;

    public MySqlConvertMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        MySqlObjectToStringTranslator objectToStringTranslator
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
        _objectToStringTranslator = objectToStringTranslator;
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (!s_supportedMethods.Contains(method)
            || arguments.Count != 1)
        {
            return null;
        }

        if (method.ReturnType == typeof(string))
        {
            return _objectToStringTranslator.TranslateInstance(arguments[0]);
        }

        if (arguments[0].Type == method.ReturnType)
        {
            return arguments[0];
        }

        var typeMapping = _typeMappingSource.FindMapping(method.ReturnType);

        return typeMapping is null
            ? null
            : _sqlExpressionFactory.Convert(arguments[0], method.ReturnType, typeMapping);
    }
}
