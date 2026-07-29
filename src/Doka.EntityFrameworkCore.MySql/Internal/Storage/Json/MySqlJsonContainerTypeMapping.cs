namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Type mapping resolved by EF Core 10's
/// <c>RelationalShapedQueryCompilingExpressionVisitor.GenerateJsonReader</c> when it constructs
/// the read expression for the JSON container column of a <c>ToJson()</c>-mapped owned entity.
/// The visitor calls <c>RelationalTypeMapping.GetDataReaderMethod()</c> on this mapping and
/// assigns the result into a <see cref="MemoryStream"/> variable that a
/// <c>Utf8JsonReaderManager</c> then walks. The base
/// <see cref="MySqlJsonStringTypeMapping"/> claims <see cref="string"/> as the CLR type, which
/// makes the read expression return a <see cref="string"/> and trip
/// <see cref="InvalidOperationException"/> ("No coercion operator is defined between
/// <c>string</c> and <c>MemoryStream</c>") at the assignment step.
/// This mapping keeps <see cref="string"/> as the CLR type so MySqlConnector's standard
/// <c>GetString</c> path handles the read and the write path stays compatible with EF Core's
/// owned-JSON serializer (which produces a JSON string), but overrides
/// <see cref="CustomizeDataReaderExpression"/> to wrap the raw string with a
/// <c>new MemoryStream(Encoding.UTF8.GetBytes(...))</c> call so the expression type matches the
/// shaper's <see cref="MemoryStream"/> target.
/// </summary>
internal sealed class MySqlJsonContainerTypeMapping : JsonTypeMapping
{
    private static readonly MethodInfo s_encodingGetBytesMethod = typeof(Encoding)
        .GetMethod(nameof(Encoding.GetBytes), [typeof(string)])
        ?? throw new InvalidOperationException(
            "Encoding.GetBytes(string) method not found; the JSON container mapping needs it to wrap reads as MemoryStream.");

    private static readonly ConstructorInfo s_memoryStreamCtor = typeof(MemoryStream)
        .GetConstructor([typeof(byte[])])
        ?? throw new InvalidOperationException(
            "MemoryStream(byte[]) constructor not found; the JSON container mapping needs it to wrap reads as MemoryStream.");

    public MySqlJsonContainerTypeMapping(
        string storeType
    ) : base(storeType, typeof(string), System.Data.DbType.String) { }

    private MySqlJsonContainerTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlJsonContainerTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => value is string json
        ? MySqlSqlLiteralEscaper.EscapeAndQuote(json)
        : throw new InvalidOperationException(
            $"Cannot generate a JSON container SQL literal from '{value.GetType().FullName}'.");

    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        // expression is the raw `reader.GetString(ordinal)` call; wrap as
        // `new MemoryStream(Encoding.UTF8.GetBytes(expression))` so EF Core's shaper can
        // hand the MemoryStream to a Utf8JsonReaderManager without a string -> MemoryStream
        // coercion that the generated expression tree cannot express.
        var utf8 = Expression.Property(null, typeof(Encoding), nameof(Encoding.UTF8));
        var bytes = Expression.Call(utf8, s_encodingGetBytesMethod, expression);
        return Expression.New(s_memoryStreamCtor, bytes);
    }
}
