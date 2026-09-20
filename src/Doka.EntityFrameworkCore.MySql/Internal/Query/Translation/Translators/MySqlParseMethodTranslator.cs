namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates the invariant one-argument numeric <c>Parse</c> methods supported
/// by EF Core's relational basic-types contract.
/// </summary>
internal sealed class MySqlParseMethodTranslator : IMethodCallTranslator
{
    private static readonly FrozenSet<MethodInfo> s_supportedMethods = new MethodInfo[]
    {
        typeof(byte).GetRuntimeMethod(nameof(byte.Parse), [typeof(string)])!,
        typeof(decimal).GetRuntimeMethod(nameof(decimal.Parse), [typeof(string)])!,
        typeof(double).GetRuntimeMethod(nameof(double.Parse), [typeof(string)])!,
        typeof(float).GetRuntimeMethod(nameof(float.Parse), [typeof(string)])!,
        typeof(short).GetRuntimeMethod(nameof(short.Parse), [typeof(string)])!,
        typeof(int).GetRuntimeMethod(nameof(int.Parse), [typeof(string)])!,
        typeof(long).GetRuntimeMethod(nameof(long.Parse), [typeof(string)])!,
    }.ToFrozenSet();

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public MySqlParseMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
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

        var typeMapping = _typeMappingSource.FindMapping(method.ReturnType);

        return typeMapping is null ? null : _sqlExpressionFactory.Convert(arguments[0], method.ReturnType, typeMapping);
    }
}
