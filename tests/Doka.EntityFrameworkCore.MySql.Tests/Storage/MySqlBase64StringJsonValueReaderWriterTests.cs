using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the SQL-mode-independent JSON transport for parameterized strings.
/// </summary>
public sealed class MySqlBase64StringJsonValueReaderWriterTests
{
    /// <summary>
    /// Verifies exact UTF-8 round trips for empty, escaped, non-ASCII, and
    /// pooled-buffer values.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("\"\\q\"")]
    [InlineData("gr\u00FCn")]
    [InlineData(
        "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz"
        + "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz"
        + "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz"
        + "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void Values_round_trip_through_base64_utf8(
        string value
    )
    {
        // Arrange
        var readerWriter = MySqlBase64StringJsonValueReaderWriter.Instance;

        // Act
        var json = readerWriter.ToJsonString(value);
        var actual = readerWriter.FromJsonString(json);

        // Assert
        Assert.Equal(value, actual);
        Assert.Equal($"\"{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}\"", json);
    }

    /// <summary>
    /// Verifies invalid UTF-8 bytes are rejected instead of being replaced
    /// while the Base64 envelope is decoded.
    /// </summary>
    [Fact]
    public void Invalid_utf8_payload_is_rejected()
    {
        // Arrange
        var readerWriter = MySqlBase64StringJsonValueReaderWriter.Instance;
        Action act = () => readerWriter.FromJsonString("\"/w==\"");

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<DecoderFallbackException>(recordedException);
        Assert.Equal([byte.MaxValue], Assert.IsType<byte[]>(exception.BytesUnknown));
    }
}
