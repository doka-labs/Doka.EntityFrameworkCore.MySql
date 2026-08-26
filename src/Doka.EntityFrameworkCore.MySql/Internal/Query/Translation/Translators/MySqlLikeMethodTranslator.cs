namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates Doka's generic scalar LIKE overloads without introducing CLR
/// formatting or client evaluation.
/// </summary>
internal sealed class MySqlLikeMethodTranslator : IMethodCallTranslator
{
    private static readonly FrozenSet<Type> s_supportedScalarTypes = new[]
    {
        typeof(byte),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(int),
        typeof(long),
        typeof(sbyte),
        typeof(short),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(DateTime),
        typeof(Guid),
        typeof(string),
    }.ToFrozenSet();

    private static readonly MethodInfo s_likeMethod = ResolveMethod(parameterCount: 3);
    private static readonly MethodInfo s_likeWithEscapeMethod = ResolveMethod(parameterCount: 4);

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly MySqlGuidTextExpressionFactory _guidTextExpressionFactory;
    private readonly RelationalTypeMapping _stringTypeMapping;

    public MySqlLikeMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource,
        MySqlGuidTextExpressionFactory guidTextExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _guidTextExpressionFactory = guidTextExpressionFactory;
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
        if (!method.IsGenericMethod)
        {
            return null;
        }

        var genericMethod = method.GetGenericMethodDefinition();

        if (genericMethod != s_likeMethod
            && genericMethod != s_likeWithEscapeMethod)
        {
            return null;
        }

        var genericType = method.GetGenericArguments()[0];
        var scalarType = Nullable.GetUnderlyingType(genericType) ?? genericType;

        if (!s_supportedScalarTypes.Contains(scalarType))
        {
            throw new InvalidOperationException(
                $"The generic LIKE translation does not support CLR type '{genericType.FullName}'.");
        }

        var matchExpression = _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[1]);

        if (scalarType == typeof(Guid))
        {
            matchExpression = _guidTextExpressionFactory.Create(matchExpression, preserveTextMapping: true);
        }

        var pattern = _sqlExpressionFactory.ApplyTypeMapping(arguments[2], _stringTypeMapping);
        var escapeCharacter = genericMethod == s_likeWithEscapeMethod
            ? _sqlExpressionFactory.ApplyTypeMapping(arguments[3], _stringTypeMapping)
            : null;

        return _sqlExpressionFactory.Like(matchExpression, pattern, escapeCharacter);
    }

    private static MethodInfo ResolveMethod(
        int parameterCount
    ) => typeof(MySqlDbFunctionsExtensions)
        .GetRuntimeMethods()
        .Single(method => method is { Name: nameof(MySqlDbFunctionsExtensions.Like), IsGenericMethodDefinition: true }
            && method.GetParameters().Length == parameterCount);
}
