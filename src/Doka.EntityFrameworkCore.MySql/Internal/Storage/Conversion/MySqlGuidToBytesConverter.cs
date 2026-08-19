namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlGuidToBytesConverter : ValueConverter<Guid, byte[]>
{
    public static MySqlGuidToBytesConverter Default { get; } = new();

    private MySqlGuidToBytesConverter() : base(
        guid => guid.ToByteArray(true),
        bytes => FromProvider(bytes),
        new ConverterMappingHints(size: 16)) { }

    private static Guid FromProvider(
        byte[] bytes
    )
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return bytes.Length == 16
            ? new Guid(bytes, bigEndian: true)
            : throw new InvalidOperationException("A Binary16 Guid provider value must contain exactly 16 bytes.");
    }
}
