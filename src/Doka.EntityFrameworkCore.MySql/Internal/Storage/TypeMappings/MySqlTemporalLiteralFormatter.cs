namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlTemporalLiteralFormatter
{
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
            "MySQL-family time precision must be between zero and six.");

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
}
