namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps textual GUID provider values to MySQL-family character columns.
/// </summary>
public sealed class MySqlGuidStringTypeMapping : StringTypeMapping
{
    private static readonly CaseInsensitiveValueComparer s_caseInsensitiveComparer = new();

    private static readonly MethodInfo s_getStringMethod = RelationalTypeMapping.GetDataReaderMethod(typeof(string));

    private static readonly MethodInfo s_readGuidStringColumnMethod =
        typeof(MySqlGuidStringTypeMapping).GetMethod(
            nameof(ReadGuidStringColumn),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The Guid string reader method could not be resolved.");

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlGuidStringTypeMapping Default { get; } = new(
        "char(36)",
        System.Data.DbType.StringFixedLength,
        36,
        useKeyComparison: false);

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlGuidStringTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="dbType">The ADO.NET parameter type.</param>
    /// <param name="size">The fixed textual GUID length.</param>
    /// <param name="useKeyComparison">Whether key comparison follows case-insensitive GUID text semantics.</param>
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

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlGuidStringTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => MySqlSqlLiteralGenerator.Generate((string)value);

    /// <inheritdoc />
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
