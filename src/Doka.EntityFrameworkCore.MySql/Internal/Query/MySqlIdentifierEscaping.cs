namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// The single source of truth for MySQL/MariaDB identifier escaping: every backtick
/// inside an identifier is doubled before the identifier is wrapped in backticks.
/// Two callers share the rule:
/// <see cref="MySqlSqlGenerationHelper"/> (DI-provided, hot-path span-optimized) and
/// <see cref="MySqlSequenceValueGenerator"/> (static, runtime sequence reader without
/// access to the DI helper). Centralizing the rule removes the drift risk that a
/// future escape-rule change could land in one place but not the other.
/// </summary>
internal static class MySqlIdentifierEscaping
{
    /// <summary>
    /// Returns the identifier with every backtick doubled but without the
    /// surrounding backtick delimiters. The no-backtick fast path returns the
    /// input unchanged so the common case incurs no allocation.
    /// </summary>
    public static string EscapeBackticks(
        string identifier
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return identifier.AsSpan().IndexOf('`') < 0
            ? identifier
            : identifier.Replace("`", "``", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the identifier wrapped in backticks with any embedded backtick
    /// doubled. Allocates one new string per call; the
    /// <see cref="MySqlSqlGenerationHelper"/> hot path keeps its own
    /// <c>string.Create</c> implementation for the no-backtick case where the
    /// allocation cost is the dominant signal.
    /// </summary>
    public static string DelimitIdentifier(
        string identifier
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return identifier.AsSpan().IndexOf('`') < 0
            ? "`" + identifier + "`"
            : "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
    }
}
