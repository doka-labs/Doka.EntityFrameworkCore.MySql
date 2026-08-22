namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps CLR <see cref="TimeSpan"/> values to MySQL-family <c>TIME</c> columns.
/// </summary>
public sealed class MySqlTimeSpanTypeMapping : TimeSpanTypeMapping
{
    private const long MaximumTimeTicks = (838L * TimeSpan.TicksPerHour)
        + (59L * TimeSpan.TicksPerMinute)
        + (59L * TimeSpan.TicksPerSecond);

    private readonly int _precision;
    private readonly int _tickResolution;
    private readonly int _fractionDivisor;

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlTimeSpanTypeMapping Default { get; } = new("time(6)", 6);

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlTimeSpanTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="precision">The supported fractional-second precision from zero through six.</param>
    public MySqlTimeSpanTypeMapping(
        string storeType,
        int precision
    ) : base(CreateParameters(storeType, precision))
    {
        _precision = precision;
        _tickResolution = MySqlTemporalLiteralFormatter.Pow10(7 - precision);
        _fractionDivisor = precision == 0 ? 0 : MySqlTemporalLiteralFormatter.Pow10(precision - 1);
    }

    private MySqlTimeSpanTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters)
    {
        _precision = MySqlTemporalLiteralFormatter.ValidatePrecision(
            parameters.Precision
            ?? throw new InvalidOperationException("A MySQL-family TimeSpan mapping clone requires a precision."));

        _tickResolution = MySqlTemporalLiteralFormatter.Pow10(7 - _precision);
        _fractionDivisor = _precision == 0 ? 0 : MySqlTemporalLiteralFormatter.Pow10(_precision - 1);
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var time = (TimeSpan)value;
        var truncatedTicks = time.Ticks / _tickResolution * _tickResolution;

        // Validate the emitted precision so discarded ticks do not reject an exact boundary.
        if (truncatedTicks is > MaximumTimeTicks or < -MaximumTimeTicks)
        {
            throw new InvalidOperationException($"The TimeSpan value '{time:c}' exceeds the MySQL TIME range.");
        }

        var isNegative = truncatedTicks < 0;
        var absoluteTicks = Math.Abs(truncatedTicks);
        var hours = absoluteTicks / TimeSpan.TicksPerHour;
        var remainder = absoluteTicks % TimeSpan.TicksPerHour;
        var minutes = remainder / TimeSpan.TicksPerMinute;
        remainder %= TimeSpan.TicksPerMinute;

        var seconds = remainder / TimeSpan.TicksPerSecond;
        var fraction = remainder % TimeSpan.TicksPerSecond;
        var hourDigits = hours >= 100 ? 3 : 2;
        var literalLength = 8 + hourDigits + (isNegative ? 1 : 0) + (_precision == 0 ? 0 : _precision + 1);
        var fractionalValue = fraction / _tickResolution;

        return string.Create(
            literalLength,
            (isNegative, hours, minutes, seconds, fractionalValue, _precision, _fractionDivisor),
            static (
                destination,
                state
            ) =>
            {
                var position = 0;
                destination[position++] = '\'';
                if (state.isNegative)
                {
                    destination[position++] = '-';
                }

                WriteHours(destination, ref position, state.hours);
                destination[position++] = ':';
                MySqlTemporalLiteralFormatter.WriteTwoDigits(destination, ref position, state.minutes);
                destination[position++] = ':';
                MySqlTemporalLiteralFormatter.WriteTwoDigits(destination, ref position, state.seconds);

                if (state._precision > 0)
                {
                    destination[position++] = '.';
                    MySqlTemporalLiteralFormatter.WriteFraction(
                        destination,
                        ref position,
                        state.fractionalValue,
                        state._precision,
                        state._fractionDivisor);
                }

                destination[position] = '\'';
            });
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlTimeSpanTypeMapping(parameters);

    private static RelationalTypeMappingParameters CreateParameters(
        string storeType,
        int precision
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        var validatedPrecision = MySqlTemporalLiteralFormatter.ValidatePrecision(precision);

        return new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(TimeSpan), jsonValueReaderWriter: JsonTimeSpanReaderWriter.Instance),
            storeType,
            StoreTypePostfix.None,
            System.Data.DbType.Time,
            unicode: false,
            size: null,
            fixedLength: false,
            validatedPrecision,
            scale: null);
    }

    private static void WriteHours(
        Span<char> destination,
        ref int position,
        long hours
    )
    {
        if (hours >= 100)
        {
            destination[position++] = (char)('0' + (hours / 100));
        }

        destination[position++] = (char)('0' + ((hours / 10) % 10));
        destination[position++] = (char)('0' + (hours % 10));
    }

}
