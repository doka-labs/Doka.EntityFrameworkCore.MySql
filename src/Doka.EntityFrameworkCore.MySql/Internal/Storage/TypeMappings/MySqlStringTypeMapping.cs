namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Represents a MySQL-family string type mapping with optional
/// case-insensitive key comparison.
/// </summary>
public sealed class MySqlStringTypeMapping : StringTypeMapping
{
    private static readonly CaseInsensitiveValueComparer s_caseInsensitiveComparer = new();

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlStringTypeMapping Default { get; } = new(
        "longtext",
        System.Data.DbType.String);

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlStringTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The database type name.</param>
    /// <param name="dbType">The ADO.NET parameter type.</param>
    /// <param name="unicode">Whether the mapping stores Unicode text.</param>
    /// <param name="size">The optional maximum character count.</param>
    /// <param name="useKeyComparison">
    /// Whether key comparisons should match the default case-insensitive
    /// MySQL-family collation semantics.
    /// </param>
    public MySqlStringTypeMapping(
        string storeType,
        DbType? dbType,
        bool unicode = true,
        int? size = null,
        bool useKeyComparison = false
    ) : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(string),
                comparer: useKeyComparison ? s_caseInsensitiveComparer : null,
                keyComparer: useKeyComparison ? s_caseInsensitiveComparer : null,
                jsonValueReaderWriter: JsonStringReaderWriter.Instance),
            storeType,
            StoreTypePostfix.None,
            dbType,
            unicode,
            size))
    {
    }

    private MySqlStringTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlStringTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(
        object value
    ) => MySqlSqlLiteralGenerator.Generate((string)value);
}
