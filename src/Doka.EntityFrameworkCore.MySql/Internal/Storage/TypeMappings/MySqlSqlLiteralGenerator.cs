namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Generates UTF-8 text literals whose interpretation does not depend on the
/// active MySQL or MariaDB <c>sql_mode</c>.
/// </summary>
internal static class MySqlSqlLiteralGenerator
{
    private const string Utf8HexPrefix = "_utf8mb4 X'";

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Uses a readable SQL-standard quoted literal when its interpretation is
    /// mode-independent. Values containing backslashes or control characters
    /// use a standard hexadecimal literal with an explicit <c>utf8mb4</c>
    /// introducer instead.
    /// </summary>
    /// <param name="value">The valid UTF-16 text to encode.</param>
    /// <returns>A complete MySQL-family SQL text literal.</returns>
    /// <exception cref="EncoderFallbackException">
    /// Thrown when <paramref name="value"/> contains an unpaired UTF-16 surrogate.
    /// </exception>
    public static string Generate(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        var quoteCount = 0;
        var requiresHexLiteral = false;
        var requiresUtf8Validation = false;

        foreach (var character in value)
        {
            quoteCount += character == '\'' ? 1 : 0;
            requiresHexLiteral |= character == '\\' || char.IsControl(character);
            requiresUtf8Validation |= char.IsSurrogate(character);
        }

        if (!requiresHexLiteral)
        {
            if (requiresUtf8Validation)
            {
                _ = s_strictUtf8.GetByteCount(value);
            }

            return GenerateQuotedLiteral(value, quoteCount);
        }

        var byteCount = s_strictUtf8.GetByteCount(value);

        return GenerateHexLiteral(value, byteCount);
    }

    /// <summary>
    /// Generates the quoted-literal form required by MySQL-family DDL comment
    /// grammar. The migrations generator enables <c>NO_BACKSLASH_ESCAPES</c>
    /// around statements that contain a backslash and restores the caller's
    /// session mode immediately afterwards.
    /// </summary>
    /// <param name="value">The valid UTF-16 comment text to encode.</param>
    /// <returns>A quoted and quote-doubled DDL comment literal.</returns>
    /// <exception cref="EncoderFallbackException">
    /// Thrown when <paramref name="value"/> contains an unpaired UTF-16 surrogate.
    /// </exception>
    public static string GenerateDdlComment(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        var quoteCount = 0;
        var requiresUtf8Validation = false;

        foreach (var character in value)
        {
            quoteCount += character == '\'' ? 1 : 0;
            requiresUtf8Validation |= char.IsSurrogate(character);
        }

        if (requiresUtf8Validation)
        {
            _ = s_strictUtf8.GetByteCount(value);
        }

        return GenerateQuotedLiteral(value, quoteCount);
    }

    private static string GenerateQuotedLiteral(
        string value,
        int quoteCount
    )
    {
        return string.Create(
            value.Length + quoteCount + 2,
            value,
            static (
                destination,
                source
            ) =>
            {
                destination[0] = '\'';
                var destinationIndex = 1;

                foreach (var character in source)
                {
                    destination[destinationIndex++] = character;
                    if (character == '\'')
                    {
                        destination[destinationIndex++] = '\'';
                    }
                }

                destination[destinationIndex] = '\'';
            });
    }

    private static string GenerateHexLiteral(
        string value,
        int byteCount
    )
    {
        var rentedBytes = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        var bytesWritten = 0;

        try
        {
            bytesWritten = s_strictUtf8.GetBytes(value, rentedBytes);

            return string.Create(
                Utf8HexPrefix.Length + (bytesWritten * 2) + 1,
                (Bytes: rentedBytes, Length: bytesWritten),
                static (
                    destination,
                    state
                ) =>
                {
                    Utf8HexPrefix
                        .AsSpan()
                        .CopyTo(destination);

                    var hexDestination = destination.Slice(Utf8HexPrefix.Length, state.Length * 2);
                    if (!Convert.TryToHexString(
                            state.Bytes.AsSpan(0, state.Length),
                            hexDestination,
                            out var charsWritten)
                        || charsWritten != hexDestination.Length)
                    {
                        throw new InvalidOperationException(
                            "The UTF-8 SQL literal could not be encoded as hexadecimal.");
                    }

                    destination[^1] = '\'';
                });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rentedBytes.AsSpan(0, bytesWritten));
            ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }
}
