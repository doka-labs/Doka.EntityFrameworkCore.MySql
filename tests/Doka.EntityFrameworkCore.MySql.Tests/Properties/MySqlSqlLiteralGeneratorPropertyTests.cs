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

    /// <summary>Verifies each generated value against the independent mode-neutral SQL literal form.</summary>
    [Property(MaxTest = 1000)]
    public bool Generate_matches_the_mode_independent_literal_form(
        string? raw
    )
    {
        // Arrange
        if (raw is null)
        {
            return true;
        }

        var expected = IsValidUtf16(raw)
            ? raw.Any(character => character == '\\' || char.IsControl(character))
                ? $"_utf8mb4 X'{Convert.ToHexString(s_strictUtf8.GetBytes(raw))}'"
                : $"'{raw.Replace("'", "''", StringComparison.Ordinal)}'"
            : null;

        // Act
        var (literal, rejected) = CaptureGeneratedLiteral(raw, MySqlSqlLiteralGenerator.Generate);

        // Assert
        return expected is null ? rejected : !rejected && literal == expected;
    }

    /// <summary>Verifies generated text roundtrips and malformed UTF-16 inputs are rejected.</summary>
    [Property(MaxTest = 1000)]
    public bool Generate_round_trips_every_valid_utf16_value_via_utf8(
        string? raw
    )
    {
        // Arrange
        if (raw is null)
        {
            return true;
        }

        var validInput = IsValidUtf16(raw);

        // Act
        var (literal, rejected) = CaptureGeneratedLiteral(raw, MySqlSqlLiteralGenerator.Generate);

        // Assert
        if (rejected)
        {
            return !validInput;
        }

        if (!validInput || literal is null)
        {
            return false;
        }

        var recovered = literal.StartsWith("_utf8mb4 X'", StringComparison.Ordinal)
            ? s_strictUtf8.GetString(Convert.FromHexString(literal[11..^1]))
            : literal[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return recovered == raw;
    }

    /// <summary>Verifies generated literals cannot expose an undoubled quote in executable SQL.</summary>
    [Property(MaxTest = 1000)]
    public bool Generate_never_exposes_an_undoubled_quote(
        string? raw
    )
    {
        // Arrange
        if (raw is null)
        {
            return true;
        }

        var validInput = IsValidUtf16(raw);

        // Act
        var (literal, rejected) = CaptureGeneratedLiteral(raw, MySqlSqlLiteralGenerator.Generate);

        // Assert
        if (rejected)
        {
            return !validInput;
        }

        if (!validInput || literal is null)
        {
            return false;
        }

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

    /// <summary>Verifies generated DDL comments use quoted text and reject malformed UTF-16.</summary>
    [Property(MaxTest = 1000)]
    public bool GenerateDdlComment_matches_the_quoted_literal_form(
        string? raw
    )
    {
        // Arrange
        if (raw is null)
        {
            return true;
        }

        var expected = IsValidUtf16(raw)
            ? $"'{raw.Replace("'", "''", StringComparison.Ordinal)}'"
            : null;

        // Act
        var (literal, rejected) = CaptureGeneratedLiteral(raw, MySqlSqlLiteralGenerator.GenerateDdlComment);

        // Assert
        return expected is null ? rejected : !rejected && literal == expected;
    }

    /// <summary>Verifies that literal generation rejects an explicitly unpaired UTF-16 surrogate.</summary>
    [Fact]
    public void Generate_rejects_unpaired_utf16_surrogates()
    {
        // Arrange
        var invalid = new string(['\uD800']);
        Action act = () => MySqlSqlLiteralGenerator.Generate(invalid);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<EncoderFallbackException>(recordedException);
    }

    /// <summary>Validates generated text independently of the provider's literal-selection path.</summary>
    private static bool IsValidUtf16(
        string raw
    )
    {
        try
        {
            _ = s_strictUtf8.GetByteCount(raw);

            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Invokes the generator once and captures only its documented encoding rejection.</summary>
    private static (string? Literal, bool Rejected) CaptureGeneratedLiteral(
        string raw,
        Func<string, string> generator
    )
    {
        // WHY: The fallback outcome is part of the generated case. Capturing it
        // once avoids repeating the provider operation while evaluating assertions.
        try
        {
            return (generator(raw), false);
        }
        catch (EncoderFallbackException)
        {
            return (null, true);
        }
    }
}
