namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps CLR <see cref="TimeOnly"/> values to MySQL-family <c>TIME</c> columns.
/// </summary>
public sealed class MySqlTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    private readonly int _precision;
    private readonly int _tickResolution;
    private readonly int _fractionDivisor;

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlTimeOnlyTypeMapping Default { get; } = new("time(6)", 6);

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlTimeOnlyTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="precision">The supported fractional-second precision from zero through six.</param>
    public MySqlTimeOnlyTypeMapping(
        string storeType,
        int precision
    ) : base(CreateParameters(storeType, precision))
    {
        _precision = precision;
        _tickResolution = MySqlTemporalLiteralFormatter.Pow10(7 - precision);
        _fractionDivisor = precision == 0 ? 0 : MySqlTemporalLiteralFormatter.Pow10(precision - 1);
    }

    private MySqlTimeOnlyTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters)
    {
        _precision = MySqlTemporalLiteralFormatter.ValidatePrecision(
            parameters.Precision
            ?? throw new InvalidOperationException("A MySQL-family TimeOnly mapping clone requires a precision."));
        _tickResolution = MySqlTemporalLiteralFormatter.Pow10(7 - _precision);
        _fractionDivisor = _precision == 0 ? 0 : MySqlTemporalLiteralFormatter.Pow10(_precision - 1);
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var time = Truncate((TimeOnly)value, _tickResolution);
        var literalLength = 15 + (_precision == 0 ? 0 : _precision + 1);

        return string.Create(
            literalLength,
            (time, _precision, _tickResolution, _fractionDivisor),
            static (
                destination,
                state
            ) =>
            {
                "TIME '"
                    .AsSpan()
                    .CopyTo(destination);
                var position = 6;
                MySqlTemporalLiteralFormatter.WriteTwoDigits(destination, ref position, state.time.Hour);
                destination[position++] = ':';
                MySqlTemporalLiteralFormatter.WriteTwoDigits(destination, ref position, state.time.Minute);
                destination[position++] = ':';
                MySqlTemporalLiteralFormatter.WriteTwoDigits(destination, ref position, state.time.Second);

                if (state._precision > 0)
                {
                    destination[position++] = '.';
                    var fraction = (state.time.Ticks % TimeSpan.TicksPerSecond) / state._tickResolution;
                    MySqlTemporalLiteralFormatter.WriteFraction(
                        destination,
                        ref position,
                        fraction,
                        state._precision,
                        state._fractionDivisor);
                }

                destination[position] = '\'';
            });
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlTimeOnlyTypeMapping(parameters);

    private static RelationalTypeMappingParameters CreateParameters(
        string storeType,
        int precision
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        var validatedPrecision = MySqlTemporalLiteralFormatter.ValidatePrecision(precision);

        return new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(TimeOnly), jsonValueReaderWriter: JsonTimeOnlyReaderWriter.Instance),
            storeType,
            StoreTypePostfix.None,
            System.Data.DbType.Time,
            unicode: false,
            size: null,
            fixedLength: false,
            validatedPrecision,
            scale: null);
    }

    private static TimeOnly Truncate(
        TimeOnly value,
        int resolution
    ) => new(value.Ticks / resolution * resolution);
}
