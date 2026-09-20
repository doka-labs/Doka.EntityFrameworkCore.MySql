namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Type mapping used for the JSON container column of a <c>ToJson()</c>-mapped
/// structural type. EF Core consumes the column as UTF-8 JSON through a
/// <see cref="MemoryStream"/>, while MySqlConnector exposes the value as a
/// <see cref="string"/>.
/// </summary>
public sealed class MySqlJsonContainerTypeMapping : StructuralJsonTypeMapping
{
    private static readonly MethodInfo s_createUtf8StreamMethod = typeof(MySqlJsonContainerTypeMapping)
        .GetMethod(nameof(CreateUtf8Stream), [typeof(string)])
        ?? throw new InvalidOperationException(
            "CreateUtf8Stream(string) method not found; the JSON container mapping needs it to wrap reads as MemoryStream.");

    private static readonly MethodInfo s_getStringMethod = typeof(DbDataReader)
        .GetRuntimeMethod(nameof(DbDataReader.GetString), [typeof(int)])
        ?? throw new InvalidOperationException(
            "DbDataReader.GetString(int) method not found; the JSON container mapping needs it to read JSON text.");

    /// <summary>
    /// Initializes the JSON container mapping for <paramref name="storeType"/>.
    /// </summary>
    /// <param name="storeType">The MySQL JSON store type.</param>
    public MySqlJsonContainerTypeMapping(
        string storeType
    ) : base(storeType, typeof(JsonTypePlaceholder), System.Data.DbType.String) { }

    private MySqlJsonContainerTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod() => s_getStringMethod;

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(
        Expression expression
    ) => Expression.Call(s_createUtf8StreamMethod, expression);

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlJsonContainerTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => value is string json
        ? MySqlSqlLiteralGenerator.Generate(json)
        : throw new InvalidOperationException(
            $"Cannot generate a JSON container SQL literal from '{value.GetType().FullName}'.");

    /// <summary>
    /// Creates the UTF-8 stream consumed by EF Core generated materializers.
    /// </summary>
    /// <param name="json">The JSON document returned by MySQL.</param>
    /// <returns>A non-writable stream over the UTF-8 representation of <paramref name="json"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="json"/> is empty and therefore is not a valid JSON document.
    /// </exception>
    public static MemoryStream CreateUtf8Stream(
        string json
    )
    {
        if (json.Length == 0)
        {
            throw new InvalidOperationException(RelationalStrings.JsonEmptyString);
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        return new MemoryStream(bytes, index: 0, bytes.Length, writable: false, publiclyVisible: true);
    }
}
