using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides <see cref="ValueComparer"/> instances for JSON CLR types used in MySQL column mappings.
/// These comparers use deep-equality semantics so that EF Core's change tracker correctly detects
/// mutations inside JSON documents.
/// </summary>
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "All JsonNode / JsonDocument / JsonElement operations in this type use well-known JSON primitive types that remain rooted under trimming.")]
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
    "AOT",
    "IL3050",
    Justification = "JsonNode cloning and equality use well-known JSON primitives; no runtime code generation is triggered at the call sites we exercise.")]
internal static class MySqlJsonValueComparers
{
    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonElement"/> that compares by raw JSON text.
    /// </summary>
    public static ValueComparer<JsonElement> JsonElementComparer { get; } = new(
        (
            a,
            b
        ) => JsonElementEquals(a, b),
        v => v
            .GetRawText()
            .GetHashCode(StringComparison.Ordinal),
        v => v.Clone());

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonDocument"/> that compares by raw JSON text.
    /// </summary>
    public static ValueComparer<JsonDocument?> JsonDocumentComparer { get; } = new(
        (
            a,
            b
        ) => JsonDocumentEquals(a, b),
        v => v != null
            ? v
                .RootElement.GetRawText()
                .GetHashCode(StringComparison.Ordinal)
            : 0,
        v => v != null ? JsonDocument.Parse(v.RootElement.GetRawText()) : null);

    /// <summary>
    /// Explicit backing field for <see cref="JsonNodeComparer"/>. Declared separately so the
    /// trimming suppression lives on the initializer site without being attached to the
    /// auto-generated property getter, where the ILLinker otherwise re-reports IL2026.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Delegates to CreateJsonNodeComparer which uses well-known JSON types that are preserved.")]
    private static readonly ValueComparer<JsonNode?> s_jsonNodeComparer = CreateJsonNodeComparer();

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonNode"/> that compares by serialized JSON text.
    /// </summary>
    public static ValueComparer<JsonNode?> JsonNodeComparer => s_jsonNodeComparer;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.ToJsonString and JsonNode.Parse use well-known JSON types.")]
    private static ValueComparer<JsonNode?> CreateJsonNodeComparer()
    {
        return new ValueComparer<JsonNode?>(
            (
                a,
                b
            ) => JsonNodeEquals(a, b),
            v => v != null
                ? v
                    .ToJsonString()
                    .GetHashCode(StringComparison.Ordinal)
                : 0,
            v => CloneJsonNode(v));
    }

    private static bool JsonElementEquals(
        JsonElement a,
        JsonElement b
    ) => string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);

    private static bool JsonDocumentEquals(
        JsonDocument? a,
        JsonDocument? b
    )
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null
            || b is null)
        {
            return false;
        }

        return string.Equals(a.RootElement.GetRawText(), b.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JSON node cloning uses well-known JSON types that are preserved.")]
    private static JsonNode? CloneJsonNode(
        JsonNode? source
    )
    {
        if (source is null)
        {
            return null;
        }

        return JsonNode.Parse(source.ToJsonString());
    }

    private static bool JsonNodeEquals(
        JsonNode? a,
        JsonNode? b
    )
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null
            || b is null)
        {
            return false;
        }

        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }
}
