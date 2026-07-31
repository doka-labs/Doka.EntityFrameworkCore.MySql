using System.Text;
using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// Property coverage for the mode-independent UTF-8 SQL literal generator.
/// </summary>
public sealed class MySqlSqlLiteralGeneratorPropertyTests
{
    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [Property(MaxTest = 1000)]
    public bool Generate_matches_the_mode_independent_literal_form(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        try
        {
            var expected = raw.Any(character => character == '\\' || char.IsControl(character))
                ? $"_utf8mb4 X'{Convert.ToHexString(s_strictUtf8.GetBytes(raw))}'"
                : $"'{raw.Replace("'", "''", StringComparison.Ordinal)}'";

            return MySqlSqlLiteralGenerator.Generate(raw) == expected;
        }
        catch (EncoderFallbackException)
        {
            return ThrowsEncoderFallback(raw, MySqlSqlLiteralGenerator.Generate);
        }
    }

    [Property(MaxTest = 1000)]
    public bool Generate_round_trips_every_valid_utf16_value_via_utf8(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        try
        {
            var literal = MySqlSqlLiteralGenerator.Generate(raw);
            var recovered = literal.StartsWith("_utf8mb4 X'", StringComparison.Ordinal)
                ? s_strictUtf8.GetString(Convert.FromHexString(literal[11..^1]))
                : literal[1..^1]
                    .Replace("''", "'", StringComparison.Ordinal);

            return recovered == raw;
        }
        catch (EncoderFallbackException)
        {
            return ThrowsEncoderFallback(raw, MySqlSqlLiteralGenerator.Generate);
        }
    }

    [Property(MaxTest = 1000)]
    public bool Generate_never_exposes_an_undoubled_quote(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        try
        {
            var literal = MySqlSqlLiteralGenerator.Generate(raw);
            if (literal.StartsWith("_utf8mb4 X'", StringComparison.Ordinal))
            {
                var hex = literal[11..^1];
                return literal.EndsWith('\'') && hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit);
            }

            var body = literal[1..^1];
            for (var index = 0; index < body.Length; index++)
            {
                if (body[index] != '\'')
                {
                    continue;
                }

                if (index + 1 >= body.Length
                    || body[index + 1] != '\'')
                {
                    return false;
                }

                index++;
            }

            return literal.StartsWith('\'') && literal.EndsWith('\'');
        }
        catch (EncoderFallbackException)
        {
            return ThrowsEncoderFallback(raw, MySqlSqlLiteralGenerator.Generate);
        }
    }

    [Property(MaxTest = 1000)]
    public bool GenerateDdlComment_matches_the_quoted_literal_form(
        string? raw
    )
    {
        if (raw is null)
        {
            return true;
        }

        try
        {
            var expected = $"'{raw.Replace("'", "''", StringComparison.Ordinal)}'";

            return MySqlSqlLiteralGenerator.GenerateDdlComment(raw) == expected;
        }
        catch (EncoderFallbackException)
        {
            return ThrowsEncoderFallback(raw, MySqlSqlLiteralGenerator.GenerateDdlComment);
        }
    }

    [Fact]
    public void Generate_rejects_unpaired_utf16_surrogates()
    {
        var invalid = new string(['\uD800']);

        Assert.Throws<EncoderFallbackException>(() => MySqlSqlLiteralGenerator.Generate(invalid));
    }

    private static bool ThrowsEncoderFallback(
        string raw,
        Func<string, string> generator
    )
    {
        try
        {
            _ = generator(raw);
            return false;
        }
        catch (EncoderFallbackException)
        {
            return true;
        }
    }
}
