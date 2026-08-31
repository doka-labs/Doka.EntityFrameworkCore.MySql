namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A MySQL JSON type mapping that preserves the native CLR type (<see cref="JsonElement"/>,
/// <see cref="JsonDocument"/>, <see cref="JsonNode"/>, etc.) through the EF Core pipeline
/// instead of collapsing to <c>string</c>. AOT and trimming suppressions are scoped per
/// factory method so the trimmer's audit window stays limited to the call sites that
/// genuinely touch JsonNode / JsonDocument surfaces.
/// </summary>
internal sealed class MySqlJsonTypeMapping : RelationalTypeMapping, IMySqlProviderOwnedModelTypeMapping
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

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonElement"/>.
    /// </summary>
    public static MySqlJsonTypeMapping CreateJsonElementMapping() => new(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonElement),
                new ValueConverter<JsonElement, string>(
                    v => v.GetRawText(),
                    v => JsonElement.Parse(v)),
                MySqlJsonValueComparers.JsonElementComparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String));

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonDocument"/>.
    /// </summary>
    public static MySqlJsonTypeMapping CreateJsonDocumentMapping() => new(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonDocument),
                new ValueConverter<JsonDocument?, string>(
                    v => v != null ? v.RootElement.GetRawText() : "null",
                    v => JsonDocument.Parse(v, default)),
                MySqlJsonValueComparers.JsonDocumentComparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String));

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
    public static MySqlJsonTypeMapping CreateJsonNodeMapping() => new(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonNode),
                new ValueConverter<JsonNode?, string>(
                    v => v != null ? v.ToJsonString() : "null",
                    v => JsonNode.Parse(v, default(JsonNodeOptions?))),
                MySqlJsonValueComparers.JsonNodeComparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String));

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
    public static MySqlJsonTypeMapping CreateJsonObjectMapping() => new(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonObject),
                new ValueConverter<JsonObject?, string>(
                    v => v != null ? v.ToJsonString() : "null",
                    v => (JsonObject?)JsonNode.Parse(v, default(JsonNodeOptions?))),
                MySqlJsonValueComparers.JsonNodeComparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String));

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
    public static MySqlJsonTypeMapping CreateJsonArrayMapping() => new MySqlJsonTypeMapping(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonArray),
                new ValueConverter<JsonArray?, string>(
                    v => v != null ? v.ToJsonString() : "null",
                    v => (JsonArray?)JsonNode.Parse(v, default(JsonNodeOptions?))),
                MySqlJsonValueComparers.JsonNodeComparer),
            "json",
            StoreTypePostfix.None,
            System.Data.DbType.String));

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlJsonTypeMapping(parameters);

    /// <inheritdoc />
    public override Expression GenerateCodeLiteral(
        object value
    )
    {
        var json = GetJson(value);
        var jsonLiteral = Expression.Constant(json);
        var documentOptions = Expression.New(typeof(JsonDocumentOptions));

        if (ClrType == typeof(JsonElement))
        {
            return Expression.Call(
                s_jsonElementParseMethod,
                jsonLiteral,
                documentOptions);
        }

        if (ClrType == typeof(JsonDocument))
        {
            return Expression.Call(
                s_jsonDocumentParseMethod,
                jsonLiteral,
                documentOptions);
        }

        if (ClrType == typeof(JsonNode))
        {
            return Expression.Call(
                s_jsonNodeParseMethod,
                jsonLiteral,
                Expression.Constant(null, typeof(JsonNodeOptions?)),
                documentOptions);
        }

        if (ClrType == typeof(JsonObject)
            || ClrType == typeof(JsonArray))
        {
            return Expression.Convert(
                Expression.Call(
                    s_jsonNodeParseMethod,
                    jsonLiteral,
                    Expression.Constant(null, typeof(JsonNodeOptions?)),
                    documentOptions),
                ClrType);
        }

        throw new InvalidOperationException(
            $"Cannot generate a JSON code literal for CLR type '{ClrType.FullName}'.");
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
