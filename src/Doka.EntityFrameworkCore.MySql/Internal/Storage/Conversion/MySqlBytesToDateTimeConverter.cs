namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Converts EF's byte-array row-version shape to the <see cref="DateTime"/>
/// value stored by MySQL <c>timestamp</c> columns.
/// </summary>
public sealed class MySqlBytesToDateTimeConverter : ValueConverter<byte[], DateTime>
{
    /// <summary>
    /// Creates a converter between EF's byte-array row-version token and MySQL's
    /// temporal store representation.
    /// </summary>
    public MySqlBytesToDateTimeConverter() : base(bytes => FromBytes(bytes), value => ToBytes(value)) { }

    /// <summary>
    /// Converts a MySQL temporal row-version value to EF's byte-array token.
    /// </summary>
    /// <param name="value">The temporal row-version value.</param>
    /// <returns>The stable eight-byte token.</returns>
    public static byte[] ToBytes(
        DateTime value
    ) => BitConverter.GetBytes(value.ToBinary());

    /// <summary>
    /// Converts EF's byte-array row-version token to its temporal store value.
    /// </summary>
    /// <param name="bytes">The eight-byte row-version token.</param>
    /// <returns>The temporal row-version value.</returns>
    public static DateTime FromBytes(
        byte[] bytes
    )
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return bytes.Length != sizeof(long)
            ? throw new InvalidOperationException("A MySQL row-version token must contain exactly eight bytes.")
            : DateTime.FromBinary(BitConverter.ToInt64(bytes));
    }
}
