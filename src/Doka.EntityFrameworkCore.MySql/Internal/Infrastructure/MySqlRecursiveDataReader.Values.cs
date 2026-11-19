namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlRecursiveDataReader
{
    /// <inheritdoc />
    public override object this[
        int ordinal
    ] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[
        string name
    ] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    [SuppressMessage("Usage",
        "CA2201:Do not raise reserved exception types",
        Justification =
            "DbDataReader.GetOrdinal requires IndexOutOfRangeException for names outside the visible result.")]
    public override int GetOrdinal(
        string name
    )
    {
        var ordinal = _reader.GetOrdinal(name);

        if ((uint)ordinal >= (uint)_payloadCount)
        {
            // WHY: A hidden alias must behave like a missing native column name.
            // Keep the native name diagnostic while ordinal getters use their own range diagnostic.
            throw new IndexOutOfRangeException($"The column name '{name}' does not exist in the result set.");
        }

        return ordinal;
    }

    /// <inheritdoc />
    public override string GetName(
        int ordinal
    ) => _reader.GetName(VisibleOrdinal(ordinal));

    /// <inheritdoc />
    public override string GetDataTypeName(
        int ordinal
    ) => _reader.GetDataTypeName(VisibleOrdinal(ordinal));

    /// <inheritdoc />
    [return:
        DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetFieldType(
        int ordinal
    ) => _reader.GetFieldType(VisibleOrdinal(ordinal));

    /// <inheritdoc />
    [return:
        DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetProviderSpecificFieldType(
        int ordinal
    ) => _reader.GetProviderSpecificFieldType(VisibleOrdinal(ordinal));

    /// <inheritdoc />
    public override object GetValue(
        int ordinal
    ) => _reader.GetValue(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override object GetProviderSpecificValue(
        int ordinal
    ) => _reader.GetProviderSpecificValue(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override int GetValues(
        object[] values
    ) => CopyValues(values, providerSpecific: false);

    /// <inheritdoc />
    public override int GetProviderSpecificValues(
        object[] values
    ) => CopyValues(values, providerSpecific: true);

    private int CopyValues(
        object[] values,
        bool providerSpecific
    )
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureVisibleRow();
        var count = Math.Min(values.Length, _payloadCount);

        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            values[ordinal] = providerSpecific
                ? _reader.GetProviderSpecificValue(ordinal)
                : _reader.GetValue(ordinal);
        }

        return count;
    }

    /// <inheritdoc />
    public override bool GetBoolean(
        int ordinal
    ) => _reader.GetBoolean(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override byte GetByte(
        int ordinal
    ) => _reader.GetByte(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override char GetChar(
        int ordinal
    ) => _reader.GetChar(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override DateTime GetDateTime(
        int ordinal
    ) => _reader.GetDateTime(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override decimal GetDecimal(
        int ordinal
    ) => _reader.GetDecimal(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override double GetDouble(
        int ordinal
    ) => _reader.GetDouble(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override float GetFloat(
        int ordinal
    ) => _reader.GetFloat(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override Guid GetGuid(
        int ordinal
    ) => _reader.GetGuid(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override short GetInt16(
        int ordinal
    ) => _reader.GetInt16(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override int GetInt32(
        int ordinal
    ) => _reader.GetInt32(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override long GetInt64(
        int ordinal
    ) => _reader.GetInt64(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override string GetString(
        int ordinal
    ) => _reader.GetString(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override bool IsDBNull(
        int ordinal
    ) => _reader.IsDBNull(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override T GetFieldValue<T>(
        int ordinal
    ) => _reader.GetFieldValue<T>(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override Stream GetStream(
        int ordinal
    ) => _reader.GetStream(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override TextReader GetTextReader(
        int ordinal
    ) => _reader.GetTextReader(ValueOrdinal(ordinal));

    /// <inheritdoc />
    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length
    ) => _reader.GetBytes(ValueOrdinal(ordinal), dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length
    ) => _reader.GetChars(ValueOrdinal(ordinal), dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc />
    public override Task<T> GetFieldValueAsync<T>(
        int ordinal,
        CancellationToken cancellationToken
    ) => _reader.GetFieldValueAsync<T>(ValueOrdinal(ordinal), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> IsDBNullAsync(
        int ordinal,
        CancellationToken cancellationToken
    ) => _reader.IsDBNullAsync(ValueOrdinal(ordinal), cancellationToken);
}
