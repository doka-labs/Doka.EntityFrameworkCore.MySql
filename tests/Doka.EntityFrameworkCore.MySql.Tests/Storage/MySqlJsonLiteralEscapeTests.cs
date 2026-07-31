using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the mode-independent literal contract of
/// <c>MySqlJsonTypeMapping.GenerateNonNullSqlLiteral</c>.
/// </summary>
public sealed class MySqlJsonLiteralEscapeTests
{
    private static readonly RelationalTypeMapping s_jsonElementMapping =
        MySqlJsonTypeMapping.CreateJsonElementMapping();

    [Theory]
    [InlineData("""{"k":"v"}""")]
    [InlineData("""{"k":"a'b"}""")]
    [InlineData("""{"k":"a\\b"}""")]
    [InlineData("""{"k":"a\\'b"}""")]
    [InlineData("""{"k":"\\\\"}""")]
    public void GenerateSqlLiteral_uses_the_mode_independent_literal_form(
        string rawJson
    )
    {
        using var document = JsonDocument.Parse(rawJson);

        var literal = s_jsonElementMapping.GenerateSqlLiteral(document.RootElement);

        var expected = rawJson.Contains('\\', StringComparison.Ordinal)
            ? $"_utf8mb4 X'{Convert.ToHexString(Encoding.UTF8.GetBytes(rawJson))}'"
            : $"'{rawJson.Replace("'", "''", StringComparison.Ordinal)}'";

        Assert.Equal(expected, literal);
    }

    /// <summary>
    /// Verifies the UTF-8 byte-to-hex length invariant over deterministic payloads.
    /// </summary>
    [Fact]
    public void GenerateSqlLiteral_length_tracks_utf8_bytes_not_utf16_characters()
    {
        var seed = new Random(Seed: 1337);

        for (var iteration = 0; iteration < 256; iteration++)
        {
            var rawPayload = BuildRandomJsonStringPayload(seed);
            var serialized = JsonSerializer.Serialize(new { k = rawPayload });

            using var document = JsonDocument.Parse(serialized);

            var literal = s_jsonElementMapping.GenerateSqlLiteral(document.RootElement);

            var expectedLength = serialized.Contains('\\', StringComparison.Ordinal)
                ? 12 + (Encoding.UTF8.GetByteCount(serialized) * 2)
                : serialized.Length + serialized.Count(character => character == '\'') + 2;

            Assert.Equal(expectedLength, literal.Length);
            Assert.EndsWith("'", literal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The payload must remain inside one quoted or hexadecimal literal so no
    /// content can become a SQL token under any supported session mode.
    /// </summary>
    [Theory]
    [InlineData("""{"sql":"' OR 1=1 --"}""")]
    [InlineData("""{"path":"C:\\Users\\admin"}""")]
    [InlineData("""{"q":"\\' OR 1=1 --"}""")]
    public void GenerateSqlLiteral_does_not_leak_outside_delimiters(
        string maliciousJson
    )
    {
        using var document = JsonDocument.Parse(maliciousJson);

        var literal = s_jsonElementMapping.GenerateSqlLiteral(document.RootElement);

        if (literal.StartsWith("_utf8mb4 X'", StringComparison.Ordinal))
        {
            Assert.All(literal[11..^1], character => Assert.True(Uri.IsHexDigit(character)));
        }
        else
        {
            var expected = $"'{maliciousJson.Replace("'", "''", StringComparison.Ordinal)}'";

            Assert.Equal(expected, literal);
        }
    }

    private static string BuildRandomJsonStringPayload(
        Random seed
    )
    {
        var length = seed.Next(0, 32);
        var builder = new StringBuilder(length);

        for (var index = 0; index < length; index++)
        {
            builder.Append((char)seed.Next('!', '~'));
        }

        return builder.ToString();
    }
}
