namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps CLR <see cref="TimeOnly"/> values to MySQL-family <c>TIME</c> columns.
/// </summary>
public sealed class MySqlTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    private readonly int _precision;

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
    }

    private MySqlTimeOnlyTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters)
    {
        _precision = ValidatePrecision(
            parameters.Precision
            ?? throw new InvalidOperationException("A MySQL-family TimeOnly mapping clone requires a precision."));
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var time = Truncate((TimeOnly)value, _precision);
        var format = _precision == 0 ? @"HH\:mm\:ss" : @"HH\:mm\:ss\." + new string('f', _precision);

        return "TIME '" + time.ToString(format, CultureInfo.InvariantCulture) + "'";
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

        var validatedPrecision = ValidatePrecision(precision);

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
        int precision
    )
    {
        var resolution = Pow10(7 - precision);

        return new TimeOnly(value.Ticks / resolution * resolution);
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
