namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Generates MySQL-family <see cref="float"/> literals in scientific notation so
/// the server treats them as approximate rather than exact DECIMAL values.
/// </summary>
/// <remarks>
/// Sources retrieved 2026-07-28:
/// <see href="https://dev.mysql.com/doc/refman/8.4/en/number-literals.html">MySQL 8.4 numeric literals</see>
/// and
/// <see href="https://mariadb.com/kb/en/numeric-iterals/">MariaDB numeric literals</see>.
/// </remarks>
public sealed class MySqlFloatTypeMapping : FloatTypeMapping
{
    /// <summary>
    /// Gets the canonical mapping used when a compiled model reconstructs the
    /// provider's default <see cref="float"/> mapping.
    /// </summary>
    public static new MySqlFloatTypeMapping Default { get; } = new("float");

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlFloatTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="dbType">The ADO.NET parameter type.</param>
    public MySqlFloatTypeMapping(
        string storeType,
        DbType? dbType = System.Data.DbType.Single
    ) : base(storeType, dbType) { }

    private MySqlFloatTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlFloatTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        var literal = base.GenerateNonNullSqlLiteral(value);
        var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);

        return float.IsFinite(number) && !literal.Contains('E') && !literal.Contains('e')
            ? literal.Contains('.') ? literal + "E0" : literal + ".0E0"
            : literal;
    }
}
