namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Maps byte arrays to fixed-length binary columns without interpreting their contents as GUIDs.
/// </summary>
public sealed class MySqlBinaryByteArrayTypeMapping : ByteArrayTypeMapping
{
    private static readonly MethodInfo s_getValueMethod =
        typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue), [typeof(int)])
        ?? throw new InvalidOperationException("The object reader method could not be resolved.");

    private static readonly MethodInfo s_readBinaryColumnMethod =
        typeof(MySqlBinaryByteArrayTypeMapping).GetMethod(
            nameof(ReadBinaryColumn),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The fixed binary reader method could not be resolved.");

    /// <summary>
    /// Initializes a mapping that preserves the declared fixed-length binary store type.
    /// </summary>
    /// <param name="storeType">The database type name, including any declared length.</param>
    /// <param name="size">The optional EF Core parameter-size facet.</param>
    public MySqlBinaryByteArrayTypeMapping(
        string storeType,
        int? size = null
    ) : base(storeType, System.Data.DbType.Binary, size) { }

    private MySqlBinaryByteArrayTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    /// <summary>
    /// Gets the canonical mapping used as the cloning source for generated compiled models.
    /// </summary>
    public static new MySqlBinaryByteArrayTypeMapping Default { get; } = new("binary(16)", 16);

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlBinaryByteArrayTypeMapping(parameters);

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        // WHY: EF captures this getter before applying reader customization when
        // buffering for retries. Reading object keeps both byte[] and the driver's
        // Binary16 Guid representation valid in direct and buffered queries.
        return s_getValueMethod;
    }

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(
        Expression expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);

        return Expression.Call(s_readBinaryColumnMethod, expression);
    }

    private static byte[] ReadBinaryColumn(
        object value
    )
    {
        // WHY: Binary16 transport interprets BINARY(16) as a big-endian Guid,
        // even for a byte[] property. Restore those exact stored bytes; ordinary
        // binary values already own their array and must not incur a second copy.
        return value switch
        {
            byte[] bytes => bytes,
            Guid guid => guid.ToByteArray(bigEndian: true),
            _ => throw new InvalidOperationException(
                $"The data reader returned unsupported type '{value?.GetType().FullName ?? "<null>"}' for a fixed binary column."),
        };
    }
}
