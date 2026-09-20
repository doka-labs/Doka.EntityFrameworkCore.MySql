namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Transports relational string values through JSON as Base64-encoded UTF-8.
/// </summary>
internal sealed class MySqlBase64StringJsonValueReaderWriter : JsonValueReaderWriter<string>
{
    private const int MaxStackAllocationByteCount = 256;

    private static readonly PropertyInfo s_instanceProperty =
        typeof(MySqlBase64StringJsonValueReaderWriter).GetProperty(nameof(Instance))!;

    private static readonly Encoding s_utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Gets the reusable reader/writer for strings without a value converter.
    /// </summary>
    public static MySqlBase64StringJsonValueReaderWriter Instance { get; } = new();

    private MySqlBase64StringJsonValueReaderWriter() { }

    /// <inheritdoc />
    public override string FromJsonTyped(
        ref Utf8JsonReaderManager manager,
        object? existingObject = null
    )
    {
        var bytes = manager.CurrentReader.GetBytesFromBase64();

        return s_utf8.GetString(bytes);
    }

    /// <inheritdoc />
    public override void ToJsonTyped(
        Utf8JsonWriter writer,
        string value
    )
    {
        var byteCount = s_utf8.GetByteCount(value);
        byte[]? rentedBytes = null;
        var utf8Bytes = byteCount <= MaxStackAllocationByteCount
            ? stackalloc byte[byteCount]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            var bytesWritten = s_utf8.GetBytes(value, utf8Bytes);
            writer.WriteBase64StringValue(utf8Bytes[..bytesWritten]);
        }
        finally
        {
            if (rentedBytes is not null)
            {
                // Query values may contain sensitive application data. Clear
                // the populated slice before returning shared memory.
                utf8Bytes[..byteCount]
                    .Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, s_instanceProperty);
}
