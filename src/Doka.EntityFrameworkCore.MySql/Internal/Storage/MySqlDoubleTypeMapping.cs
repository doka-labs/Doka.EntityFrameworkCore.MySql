namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Generates MySQL-family <see cref="double"/> literals in scientific notation so
/// the server treats them as approximate rather than exact DECIMAL values.
/// </summary>
/// <remarks>
/// Sources retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/number-literals.html">MySQL 8.4 numeric literals</see>
/// and
/// <see href="https://mariadb.com/kb/en/numeric-iterals/">MariaDB numeric literals</see>.
/// </remarks>
public sealed class MySqlDoubleTypeMapping : DoubleTypeMapping
{
    /// <summary>
    /// Gets the canonical mapping used when a compiled model reconstructs the
    /// provider's default <see cref="double"/> mapping.
    /// </summary>
    public static new MySqlDoubleTypeMapping Default { get; } = new("double");

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDoubleTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="dbType">The ADO.NET parameter type.</param>
    public MySqlDoubleTypeMapping(
        string storeType,
        DbType? dbType = System.Data.DbType.Double
    ) : base(storeType, dbType) { }

    private MySqlDoubleTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlDoubleTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var literal = base.GenerateNonNullSqlLiteral(value);
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

        return double.IsFinite(number) && !literal.Contains('E') && !literal.Contains('e') ? literal + "E0" : literal;
    }
}
