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
    }

    private MySqlTimeSpanTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters)
    {
        _precision = ValidatePrecision(
            parameters.Precision
            ?? throw new InvalidOperationException("A MySQL-family TimeSpan mapping clone requires a precision."));
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var time = (TimeSpan)value;
        var resolution = Pow10(7 - _precision);
        var truncatedTicks = time.Ticks / resolution * resolution;

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

        var literal = string.Create(
            CultureInfo.InvariantCulture,
            $"'{(isNegative ? "-" : string.Empty)}{hours:00}:{minutes:00}:{seconds:00}");

        if (_precision > 0)
        {
            var fractionalValue = fraction / Pow10(7 - _precision);
            literal += "." + fractionalValue.ToString(new string('0', _precision), CultureInfo.InvariantCulture);
        }

        return literal + "'";
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

        var validatedPrecision = ValidatePrecision(precision);

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

    private static int Pow10(
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

    private static int ValidatePrecision(
        int precision
    ) => precision is >= 0 and <= 6
        ? precision
        : throw new ArgumentOutOfRangeException(
            nameof(precision),
            precision,
            "MySQL-family time precision must be between zero and six.");
}
