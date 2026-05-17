namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL-specific <see cref="Guid"/> type mapping that stores the value as a fixed 16-byte
/// binary column. The base <see cref="GuidTypeMapping"/> emits literals as
/// <c>'00000000-0000-0000-0000-000000000000'</c> (38 chars including the quotes); inserting
/// that string into a <c>binary(16)</c> column produces the engine error "Data too long for
/// column ..." on every <c>HasData</c>-style seed or other literal-emission path. This
/// mapping overrides only the literal-emission path to produce MySQL's hex-binary form
/// <c>X'00112233445566778899AABBCCDDEEFF'</c>. Parameter binding stays on the base
/// <see cref="System.Data.DbType.Guid"/> code path; for parameter-bound writes against
/// <c>binary(16)</c> columns the connection-string-level <c>GuidFormat=Binary16</c> setting
/// makes MySqlConnector ship the Guid as 16 bytes instead of the 36-char string default.
/// Without that connection-string setting, parameter-bound writes against binary(16) Guid
/// columns trip "Data too long for column"; with it set, the wire format matches the
/// literal-emission path documented here.
/// </summary>
internal sealed class MySqlGuidBinaryTypeMapping : GuidTypeMapping
{
    public MySqlGuidBinaryTypeMapping(
        string storeType = "binary(16)"
    ) : base(storeType, System.Data.DbType.Guid) { }

    private MySqlGuidBinaryTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlGuidBinaryTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(
        object value
    )
    {
        if (value is not Guid guid)
        {
            throw new InvalidOperationException(
                $"MySqlGuidBinaryTypeMapping received an unexpected value of type '{value.GetType().FullName}'.");
        }

        // Match MySqlConnector's RFC 4122 / big-endian wire format for binary(16) Guid
        // columns when DbType.Guid is bound, so HasData / migration seed inserts land
        // byte-identical to the runtime parameter-bound path. The 32-hex form keeps the
        // seed literal valid for a binary(16) column (the base GuidTypeMapping emits a
        // 36-char string which trips "Data too long for column").
        return $"X'{Convert.ToHexString(guid.ToByteArray(bigEndian: true))}'";
    }
}
