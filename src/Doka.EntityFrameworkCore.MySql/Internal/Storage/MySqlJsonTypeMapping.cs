namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A MySQL JSON type mapping that preserves the native CLR type (<see cref="JsonElement"/>,
/// <see cref="JsonDocument"/>, <see cref="JsonNode"/>, etc.) through the EF Core pipeline
/// instead of collapsing to <c>string</c>. AOT and trimming suppressions are scoped per
/// factory method so the trimmer's audit window stays limited to the call sites that
/// genuinely touch JsonNode / JsonDocument surfaces.
/// </summary>
internal sealed class MySqlJsonTypeMapping : RelationalTypeMapping
{
    private MySqlJsonTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <summary>
    /// Creates a JSON type mapping for <see cref="JsonElement"/>.
    /// </summary>
    public static MySqlJsonTypeMapping CreateJsonElementMapping() => new(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(JsonElement),
                new ValueConverter<JsonElement, string>(
                    v => v.GetRawText(),
                    v => JsonDocument.Parse(v, default)
                        .RootElement),
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.Parse and ToJsonString use well-known JSON types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
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
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var json = value switch
        {
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            JsonNode node => node.ToJsonString(),
            string s => s,
            _ => throw new InvalidOperationException(
                $"Cannot generate SQL literal for JSON value of type '{value.GetType().FullName}'."),
        };

        return MySqlSqlLiteralEscaper.EscapeAndQuote(json);
    }
}
