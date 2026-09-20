namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlJsonTypeMapping
{
    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonElement"/>.
    /// </summary>
    public static MySqlJsonTypeMapping<JsonElement> CreateJsonElementMapping() => new(
        new ValueConverter<JsonElement, string>(
            v => v.GetRawText(),
            v => JsonElement.Parse(v)),
        MySqlJsonValueComparers.JsonElementComparer);

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonDocument"/>.
    /// </summary>
    public static MySqlJsonTypeMapping<JsonDocument> CreateJsonDocumentMapping() => new(
        new ValueConverter<JsonDocument?, string>(
            v => v != null ? v.RootElement.GetRawText() : "null",
            v => JsonDocument.Parse(v, default)),
        MySqlJsonValueComparers.JsonDocumentComparer);

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonNode"/>.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.Parse / ToJsonString do not trigger runtime code generation for the JSON primitives this mapping handles.")]
    public static MySqlJsonTypeMapping<JsonNode> CreateJsonNodeMapping() => new(
        new ValueConverter<JsonNode?, string>(
            v => v != null ? v.ToJsonString() : "null",
            v => JsonNode.Parse(v, default(JsonNodeOptions?))),
        MySqlJsonValueComparers.JsonNodeComparer);

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonObject"/>.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.Parse / ToJsonString do not trigger runtime code generation for the JSON primitives this mapping handles.")]
    public static MySqlJsonTypeMapping<JsonObject> CreateJsonObjectMapping() => new(
        new ValueConverter<JsonObject?, string>(
            v => v != null ? v.ToJsonString() : "null",
            v => (JsonObject?)JsonNode.Parse(v, default(JsonNodeOptions?))),
        MySqlJsonValueComparers.JsonNodeComparer);

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonArray"/>.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.Parse / ToJsonString do not trigger runtime code generation for the JSON primitives this mapping handles.")]
    public static MySqlJsonTypeMapping<JsonArray> CreateJsonArrayMapping() => new(
        new ValueConverter<JsonArray?, string>(
            v => v != null ? v.ToJsonString() : "null",
            v => (JsonArray?)JsonNode.Parse(v, default(JsonNodeOptions?))),
        MySqlJsonValueComparers.JsonNodeComparer);
}

/// <summary>
/// A MySQL JSON type mapping that preserves the statically known CLR type through the EF Core
/// pipeline instead of collapsing it to <see cref="string"/>.
/// </summary>
/// <typeparam name="T">The JSON CLR type used in the EF model.</typeparam>
internal sealed class MySqlJsonTypeMapping<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods
        | DynamicallyAccessedMemberTypes.PublicProperties)] T>
    : RelationalTypeMapping<T>, IMySqlProviderOwnedModelTypeMapping
{
    private static readonly MethodInfo s_jsonElementParseMethod = typeof(JsonElement).GetRuntimeMethod(
        nameof(JsonElement.Parse),
        [typeof(string), typeof(JsonDocumentOptions)])!;

    private static readonly MethodInfo s_jsonDocumentParseMethod = typeof(JsonDocument).GetRuntimeMethod(
        nameof(JsonDocument.Parse),
        [typeof(string), typeof(JsonDocumentOptions)])!;

    private static readonly MethodInfo s_jsonNodeParseMethod = typeof(JsonNode).GetRuntimeMethod(
        nameof(JsonNode.Parse),
        [typeof(string), typeof(JsonNodeOptions?), typeof(JsonDocumentOptions)])!;

    public MySqlJsonTypeMapping(
        ValueConverter converter,
        ValueComparer comparer
    ) : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(T), converter, comparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String)) { }

    private MySqlJsonTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    Type IMySqlProviderOwnedModelTypeMapping.ProviderClrType =>
        Converter?.ProviderClrType
        ?? throw new InvalidOperationException("The JSON mapping does not expose its required value converter.");

    object IMySqlProviderOwnedModelTypeMapping.ConvertToModelValue(
        object providerValue
    ) => Converter?.ConvertFromProvider(providerValue)
        ?? throw new InvalidOperationException("The JSON mapping does not expose its required value converter.");

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlJsonTypeMapping<T>(parameters);

    /// <inheritdoc />
    public override Expression GenerateCodeLiteral(
        object value
    )
    {
        var json = GetJson(value);
        var jsonLiteral = Expression.Constant(json);
        var documentOptions = Expression.New(typeof(JsonDocumentOptions));

        if (typeof(T) == typeof(JsonElement))
        {
            return Expression.Call(
                s_jsonElementParseMethod,
                jsonLiteral,
                documentOptions);
        }

        if (typeof(T) == typeof(JsonDocument))
        {
            return Expression.Call(
                s_jsonDocumentParseMethod,
                jsonLiteral,
                documentOptions);
        }

        if (typeof(T) == typeof(JsonNode))
        {
            return Expression.Call(
                s_jsonNodeParseMethod,
                jsonLiteral,
                Expression.Constant(null, typeof(JsonNodeOptions?)),
                documentOptions);
        }

        if (typeof(T) == typeof(JsonObject)
            || typeof(T) == typeof(JsonArray))
        {
            return Expression.Convert(
                Expression.Call(
                    s_jsonNodeParseMethod,
                    jsonLiteral,
                    Expression.Constant(null, typeof(JsonNodeOptions?)),
                    documentOptions),
                typeof(T));
        }

        throw new InvalidOperationException(
            $"Cannot generate a JSON code literal for CLR type '{typeof(T).FullName}'.");
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => MySqlSqlLiteralGenerator.Generate(GetJson(value));

    private static string GetJson(
        object value
    )
    {
        return value switch
        {
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            JsonNode node => node.ToJsonString(),
            string s => s,
            _ => throw new InvalidOperationException(
                $"Cannot generate SQL literal for JSON value of type '{value.GetType().FullName}'."),
        };
    }
}
