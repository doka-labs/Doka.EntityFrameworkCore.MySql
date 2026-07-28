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
internal sealed class MySqlDateTimeTypeMapping : DateTimeTypeMapping
{
    public MySqlDateTimeTypeMapping(
        string storeType,
        DbType? dbType = System.Data.DbType.DateTime
    ) : base(storeType, dbType) { }

    private MySqlDateTimeTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override string SqlLiteralFormatString => @"'{0:yyyy-MM-dd HH\:mm\:ss.ffffff}'";

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlDateTimeTypeMapping(parameters);
}
