namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlTemporalLiteralFormatter
{
    /// <summary>
    /// Parses an optional MySQL-family fractional-seconds facet without
    /// accepting trailing or partially formed store-type metadata.
    /// </summary>
    public static int ParsePrecision(
        string storeType
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        var value = storeType
            .AsSpan()
            .Trim();
        var openParenthesis = value.IndexOf('(');
        var closeParenthesis = value.IndexOf(')');

        if (openParenthesis < 0)
        {
            return closeParenthesis >= 0 ? ThrowInvalidPrecision(storeType) : 0;
        }

        var precisionText = closeParenthesis > openParenthesis
            ? value[(openParenthesis + 1)..closeParenthesis].Trim()
            : default;

        if (openParenthesis == 0
            || closeParenthesis != value.Length - 1
            || precisionText.IsEmpty
            || precisionText.IndexOfAny('(', ')') >= 0
            || !int.TryParse(precisionText, NumberStyles.None, CultureInfo.InvariantCulture, out var precision))
        {
            return ThrowInvalidPrecision(storeType);
        }

        return precision;
    }

    public static int Pow10(
        int exponent
    )
    {
        var value = 1;

        for (var index = 0; index < exponent; index++)
        {
            value *= 10;
        }

        return value;
    }

    public static int ValidatePrecision(
        int precision
    ) => precision is >= 0 and <= 6
        ? precision
        : throw new ArgumentOutOfRangeException(
            nameof(precision),
            precision,
            "MySQL-family temporal precision must be between zero and six.");

    public static void WriteTwoDigits(
        Span<char> destination,
        ref int position,
        long value
    )
    {
        destination[position++] = (char)('0' + (value / 10));
        destination[position++] = (char)('0' + (value % 10));
    }

    public static void WriteFraction(
        Span<char> destination,
        ref int position,
        long value,
        int precision,
        int divisor
    )
    {
        for (var index = 0; index < precision; index++)
        {
            destination[position++] = (char)('0' + (value / divisor));
            value %= divisor;
            divisor /= 10;
        }
    }

    private static int ThrowInvalidPrecision(
        string storeType
    ) => throw new InvalidOperationException(
        $"The MySQL-family temporal store type '{storeType}' has an invalid fractional-seconds precision.");
}
