namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps CLR <see cref="char"/> values to MySQL-family character columns.
/// </summary>
public sealed class MySqlCharTypeMapping : CharTypeMapping
{
    private static readonly MethodInfo s_getCharMethod = RelationalTypeMapping.GetDataReaderMethod(typeof(char));

    private static readonly MethodInfo s_readCharColumnMethod =
        typeof(MySqlCharTypeMapping).GetMethod(nameof(ReadCharColumn), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The char reader method could not be resolved.");

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlCharTypeMapping Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlCharTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    public MySqlCharTypeMapping(
        string storeType = "char(1)"
    ) : base(storeType, System.Data.DbType.StringFixedLength) { }

    private MySqlCharTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => MySqlSqlLiteralGenerator.Generate(
        Convert
            .ToChar(value, CultureInfo.InvariantCulture)
            .ToString());

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlCharTypeMapping(parameters);

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression is MethodCallExpression { Method: { } method, Object: { } reader, Arguments: [{ } ordinal], }
            && method == s_getCharMethod)
        {
            return Expression.Call(s_readCharColumnMethod, reader, ordinal);
        }

        return expression;
    }

    private static char ReadCharColumn(
        DbDataReader reader,
        int ordinal
    )
    {
        ArgumentNullException.ThrowIfNull(reader);

        var value = reader.GetValue(ordinal);

        // MySqlConnector exposes textual projections as strings. Empty strings
        // must retain Enumerable.FirstOrDefault/LastOrDefault's default(char)
        // semantics instead of flowing through DbDataReader.GetChar, which throws.
        return value switch
        {
            char character => character,
            string { Length: > 0 } text => text[0],
            string => '\0',
            _ => throw new InvalidOperationException(
                $"The data reader returned unsupported type '{value?.GetType().FullName ?? "<null>"}' for a char column."),
        };
    }
}
