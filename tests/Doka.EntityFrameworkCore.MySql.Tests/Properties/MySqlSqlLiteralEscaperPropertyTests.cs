using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// Property-style coverage for <see cref="MySqlSqlLiteralEscaper"/>. The escaper is
/// the single chokepoint every SQL-literal-emitting path (JSON type mapping, raw
/// migrations text, scaffolding strings) routes through; its quote + backslash
/// doubling invariants are the structural defense against quote-injection and the
/// silent-corruption-on-NO_BACKSLASH_ESCAPES failure mode.
/// </summary>
public sealed class MySqlSqlLiteralEscaperPropertyTests
{
    [Property(MaxTest = 1000)]
    public bool Escape_doubles_every_backslash_and_single_quote(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        var escaped = MySqlSqlLiteralEscaper.Escape(raw);

        var inputBackslashes = raw.Count(c => c == '\\');
        var escapedBackslashes = escaped.Count(c => c == '\\');
        var inputQuotes = raw.Count(c => c == '\'');
        var escapedQuotes = escaped.Count(c => c == '\'');

        return escapedBackslashes == inputBackslashes * 2 && escapedQuotes == inputQuotes * 2;
    }

    [Property(MaxTest = 1000)]
    public bool EscapeAndQuote_wraps_with_exactly_two_outer_quotes_plus_doubled_interior(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        var literal = MySqlSqlLiteralEscaper.EscapeAndQuote(raw);

        if (!literal.StartsWith('\'')
            || !literal.EndsWith('\''))
        {
            return false;
        }

        var totalQuotes = literal.Count(c => c == '\'');
        var inputQuotes = raw.Count(c => c == '\'');
        return totalQuotes == 2 + (inputQuotes * 2);
    }

    [Property(MaxTest = 1000)]
    public bool EscapeAndQuote_round_trips_via_reverse_unescape(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        var literal = MySqlSqlLiteralEscaper.EscapeAndQuote(raw);
        var inner = literal[1..^1];

        // Reverse order of Escape: undo doubled quotes first, then doubled backslashes
        // (Escape applies backslash-doubling first, then quote-doubling).
        var recovered = inner
            .Replace("''", "'", StringComparison.Ordinal)
            .Replace(@"\\", "\\", StringComparison.Ordinal);

        return recovered == raw;
    }

    [Property(MaxTest = 1000)]
    public bool Escape_is_identity_for_inputs_without_special_characters(
        string? raw
    )
    {
        if (raw is null
            || raw.Contains('\\', StringComparison.Ordinal)
            || raw.Contains('\'', StringComparison.Ordinal))
        {
            return true;
        }

        var escaped = MySqlSqlLiteralEscaper.Escape(raw);
        return escaped == raw;
    }
}
