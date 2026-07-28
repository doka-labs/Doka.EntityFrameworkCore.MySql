namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Emits MySQL-compatible <c>datetime</c> and <c>timestamp</c> literals.
/// </summary>
/// <remarks>
/// The relational base mapping emits the SQL-standard <c>TIMESTAMP '...'</c>
/// form with seven fractional digits. MySQL-family temporal columns accept at
/// most six fractional digits, so provider-owned DDL and seed literals require
/// a dedicated format.
/// </remarks>
public sealed class MySqlDateTimeTypeMapping : DateTimeTypeMapping
{
    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlDateTimeTypeMapping Default { get; } = new("datetime(6)");

    /// <summary>
    /// Creates a MySQL temporal mapping for the requested store type.
    /// </summary>
    /// <param name="storeType">The MySQL temporal store type.</param>
    /// <param name="dbType">The ADO.NET parameter type.</param>
    public MySqlDateTimeTypeMapping(
        string storeType,
        DbType? dbType = System.Data.DbType.DateTime
    ) : base(storeType, dbType) { }

    private MySqlDateTimeTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override string SqlLiteralFormatString => @"'{0:yyyy-MM-dd HH\:mm\:ss.ffffff}'";

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlDateTimeTypeMapping(parameters);
}
