namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Converts EF's byte-array row-version shape to the <see cref="DateTime"/>
/// value stored by MySQL <c>timestamp</c> columns.
/// </summary>
internal sealed class MySqlBytesToDateTimeConverter : ValueConverter<byte[], DateTime>
{
    public MySqlBytesToDateTimeConverter() : base(bytes => FromBytes(bytes), value => ToBytes(value)) { }

    private static byte[] ToBytes(
        DateTime value
    ) => BitConverter.GetBytes(value.ToBinary());

    private static DateTime FromBytes(
        byte[] bytes
    )
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return bytes.Length != sizeof(long)
            ? throw new InvalidOperationException("A MySQL row-version token must contain exactly eight bytes.")
            : DateTime.FromBinary(BitConverter.ToInt64(bytes));
    }
}
