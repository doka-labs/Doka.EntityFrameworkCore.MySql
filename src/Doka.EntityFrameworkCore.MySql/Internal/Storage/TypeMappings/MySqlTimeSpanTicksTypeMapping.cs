namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Materializes query-computed 100-nanosecond ticks as <see cref="TimeSpan"/>
/// values without using the database engine's range-limited <c>TIME</c> type.
/// </summary>
internal sealed class MySqlTimeSpanTicksTypeMapping : LongTypeMapping
{
    /// <summary>
    /// Gets the canonical query-only mapping for tick-valued <see cref="TimeSpan"/> results.
    /// </summary>
    public static new MySqlTimeSpanTicksTypeMapping Default { get; } = new();

    private MySqlTimeSpanTicksTypeMapping() : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(TimeSpan), new TimeSpanToTicksConverter()),
            "bigint",
            StoreTypePostfix.None,
            System.Data.DbType.Int64))
    { }

    private MySqlTimeSpanTicksTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlTimeSpanTicksTypeMapping(parameters);
}
