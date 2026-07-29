namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates parameterless scalar <see cref="object.ToString"/> calls to a
/// MySQL-compatible string conversion.
/// </summary>
internal sealed class MySqlObjectToStringTranslator : IMethodCallTranslator
{
    private static readonly FrozenSet<Type> s_supportedTypes = new[]
    {
        typeof(byte),
        typeof(byte[]),
        typeof(char),
        typeof(DateOnly),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(int),
        typeof(long),
        typeof(sbyte),
        typeof(short),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
    }.ToFrozenSet();

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _stringTypeMapping;

    /// <summary>
    /// Creates the scalar conversion translator.
    /// </summary>
    public MySqlObjectToStringTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _stringTypeMapping = MySqlTranslationTypeMapping.GetRequired(
            typeMappingSource,
            typeof(string));
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (instance is null
            || method.Name != nameof(ToString)
            || arguments.Count != 0)
        {
            return null;
        }

        return TranslateInstance(instance);
    }

    /// <summary>
    /// Creates the provider's scalar string representation for an already translated
    /// value. Shared with the static <see cref="Convert.ToString(object?)"/> path so
    /// boolean and null semantics have one implementation.
    /// </summary>
    internal SqlExpression? TranslateInstance(
        SqlExpression instance
    )
    {
        if (instance.TypeMapping?.ClrType == typeof(string))
        {
            return instance;
        }

        if (instance.Type == typeof(bool))
        {
            return TranslateBoolean(instance);
        }

        // EF Core's EnumMethodTranslator owns enum formatting.
        return s_supportedTypes.Contains(instance.Type)
            ? _sqlExpressionFactory.Coalesce(
                _sqlExpressionFactory.Convert(instance, typeof(string), _stringTypeMapping),
                _sqlExpressionFactory.Constant(string.Empty, _stringTypeMapping))
            : null;
    }

    private SqlExpression TranslateBoolean(
        SqlExpression instance
    )
    {
        if (instance is ColumnExpression { IsNullable: false })
        {
            return _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(instance, _sqlExpressionFactory.Constant(true.ToString())),
                ],
                _sqlExpressionFactory.Constant(false.ToString()));
        }

        return _sqlExpressionFactory.Case(
            instance,
            [
                new CaseWhenClause(
                    _sqlExpressionFactory.Constant(false),
                    _sqlExpressionFactory.Constant(false.ToString())),
                new CaseWhenClause(
                    _sqlExpressionFactory.Constant(true),
                    _sqlExpressionFactory.Constant(true.ToString())),
            ],
            _sqlExpressionFactory.Constant(string.Empty));
    }
}
