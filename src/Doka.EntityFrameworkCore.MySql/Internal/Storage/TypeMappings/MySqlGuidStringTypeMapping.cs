namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlGuidStringTypeMapping : StringTypeMapping
{
    private static readonly CaseInsensitiveValueComparer s_caseInsensitiveComparer = new();

    private static readonly MethodInfo s_getStringMethod = RelationalTypeMapping.GetDataReaderMethod(typeof(string));

    private static readonly MethodInfo s_readGuidStringColumnMethod =
        typeof(MySqlGuidStringTypeMapping).GetMethod(
            nameof(ReadGuidStringColumn),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The Guid string reader method could not be resolved.");

    public MySqlGuidStringTypeMapping(
        string storeType,
        DbType dbType,
        int size,
        bool useKeyComparison
    ) : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(string),
                comparer: useKeyComparison ? s_caseInsensitiveComparer : null,
                keyComparer: useKeyComparison ? s_caseInsensitiveComparer : null,
                jsonValueReaderWriter: JsonStringReaderWriter.Instance),
            storeType,
            StoreTypePostfix.None,
            dbType,
            unicode: false,
            size)) { }

    private MySqlGuidStringTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlGuidStringTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => MySqlSqlLiteralGenerator.Generate((string)value);

    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression is MethodCallExpression { Method: { } method, Object: { } reader, Arguments: [{ } ordinal], }
            && method == s_getStringMethod)
        {
            return Expression.Call(s_readGuidStringColumnMethod, reader, ordinal);
        }

        return expression;
    }

    private static string ReadGuidStringColumn(
        DbDataReader reader,
        int ordinal
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var value = reader.GetValue(ordinal);

        return value switch
        {
            string text => text,
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"The data reader returned unsupported type '{value?.GetType().FullName ?? "<null>"}' for a Guid text column."),
        };
    }
}
