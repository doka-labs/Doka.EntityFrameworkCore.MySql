using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Pins the escape contract of <c>MySqlJsonTypeMapping.GenerateNonNullSqlLiteral</c>: a
/// raw JSON payload containing a backslash or single quote must round-trip into a
/// single-quoted MySQL literal that the server parses as one token, not as two with a
/// trailing tail of unintended SQL.
/// </summary>
public sealed class MySqlJsonLiteralEscapeTests
{
    private static readonly RelationalTypeMapping s_jsonElementMapping =
        MySqlJsonTypeMapping.CreateJsonElementMapping();

    [Theory]
    [InlineData("""{"k":"v"}""", """'{"k":"v"}'""")]
    [InlineData("""{"k":"a'b"}""", """'{"k":"a''b"}'""")]
    [InlineData("""{"k":"a\\b"}""", """'{"k":"a\\\\b"}'""")]
    [InlineData("""{"k":"a\\'b"}""", """'{"k":"a\\\\''b"}'""")]
    [InlineData("""{"k":"\\\\"}""", """'{"k":"\\\\\\\\"}'""")]
    public void GenerateSqlLiteral_escapes_backslash_before_quote(
        string rawJson,
        string expectedSqlLiteral
    )
    {
        using var document = JsonDocument.Parse(rawJson);

        var literal = s_jsonElementMapping.GenerateSqlLiteral(document.RootElement);

        Assert.Equal(expectedSqlLiteral, literal);
    }

    /// <summary>
    /// Length-invariant: the escaped literal grows by exactly the count of backslashes
    /// and single quotes in the serialized JSON plus the two delimiting single quotes.
    /// Handrolled to keep this test free of new dependencies; a property-testing
    /// framework can replace this loop in a later test-infrastructure pass.
    /// </summary>
    [Fact]
    public void GenerateSqlLiteral_length_grows_by_special_char_count_plus_delimiters()
    {
        var seed = new Random(Seed: 1337);

        for (var iteration = 0; iteration < 256; iteration++)
        {
            var rawPayload = BuildRandomJsonStringPayload(seed);
            var serialized = JsonSerializer.Serialize(new { k = rawPayload });

            using var document = JsonDocument.Parse(serialized);

            var literal = s_jsonElementMapping.GenerateSqlLiteral(document.RootElement);

            var specialCount = serialized
                .Count(c => c is '\\' or '\'');
            var expectedLength = serialized.Length + specialCount + 2; // +2 for the wrapping single quotes.

            Assert.Equal(expectedLength, literal.Length);
            Assert.StartsWith("'", literal, StringComparison.Ordinal);
            Assert.EndsWith("'", literal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The escaped literal must contain no naked single quote and no odd-length
    /// backslash run, so the MySQL tokenizer cannot terminate the literal mid-payload.
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

        // Inside the literal (strip the wrapping single quotes), every single quote
        // must be doubled and every backslash run must be even-length.
        Assert.StartsWith("'", literal, StringComparison.Ordinal);
        Assert.EndsWith("'", literal, StringComparison.Ordinal);

        var body = literal[1..^1];
        AssertSingleQuotesAreDoubled(body);
        AssertBackslashRunsAreEvenLength(body);
    }

    private static void AssertSingleQuotesAreDoubled(
        string body
    )
    {
        for (var index = 0; index < body.Length;)
        {
            if (body[index] != '\'')
            {
                index++;
                continue;
            }

            Assert.True(
                index + 1 < body.Length && body[index + 1] == '\'',
                $"single quote at position {index} is not doubled: {body}");

            index += 2;
        }
    }

    private static void AssertBackslashRunsAreEvenLength(
        string body
    )
    {
        var index = 0;
        while (index < body.Length)
        {
            if (body[index] != '\\')
            {
                index++;
                continue;
            }

            var runStart = index;
            while (index < body.Length && body[index] == '\\')
            {
                index++;
            }

            var runLength = index - runStart;
            Assert.True(
                runLength % 2 == 0,
                $"backslash run starting at {runStart} has odd length {runLength}: {body}");
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
