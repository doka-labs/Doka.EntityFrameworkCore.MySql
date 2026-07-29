namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Escapes free-form text for safe embedding in a MySQL/MariaDB single-quoted SQL
/// literal. Backslashes are doubled first (so a stray <c>\</c> in the source cannot
/// escape a following quote before it is doubled), then single quotes are doubled
/// per the SQL standard. The doubled-quote form survives every supported
/// <c>sql_mode</c> setting including <c>NO_BACKSLASH_ESCAPES=ON</c>; the
/// alternative <c>\'</c> form silently produces invalid SQL on hardened servers.
/// </summary>
internal static class MySqlSqlLiteralEscaper
{
    /// <summary>
    /// Returns the input with every backslash doubled and every single quote
    /// doubled, but without surrounding quotes. Caller responsible for the
    /// outer <c>'...'</c> delimiters when emitting the final literal.
    /// </summary>
    public static string Escape(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns <c>'{escaped}'</c>: the escaped value wrapped in single quotes,
    /// ready to splice into generated SQL.
    /// </summary>
    public static string EscapeAndQuote(
        string value
    ) => $"'{Escape(value)}'";
}
