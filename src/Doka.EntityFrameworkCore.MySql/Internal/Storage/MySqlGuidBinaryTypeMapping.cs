namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL-specific <see cref="Guid"/> type mapping that stores the value as a fixed 16-byte
/// binary column. The base <see cref="GuidTypeMapping"/> emits literals as
/// <c>'00000000-0000-0000-0000-000000000000'</c> (38 chars including the quotes); inserting
/// that string into a <c>binary(16)</c> column produces the engine error "Data too long for
/// column ..." on every <c>HasData</c>-style seed or other literal-emission path. This
/// mapping overrides only the literal-emission path to produce MySQL's hex-binary form
/// <c>X'00112233445566778899AABBCCDDEEFF'</c>. Parameter binding stays on the base
/// <see cref="DbType.Guid"/> code path which MySqlConnector's native Guid handling already
/// round-trips correctly against <c>binary(16)</c> columns; the parallel <c>char(36)</c> and
/// <c>varchar(36)</c> string-form Guid mappings remain handled by <see cref="StringTypeMapping"/>
/// instances and are not affected by this class.
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

        // MySqlConnector's parameter binding for DbType.Guid against a binary(16) column ships the
        // bytes in big-endian (RFC 4122) order. Guid.ToByteArray() defaults to the Microsoft
        // little-endian layout for the first three fields, which round-trips with itself but not
        // with the connector. Using ToByteArray(bigEndian: true) keeps the seed-literal path
        // byte-identical to the runtime-parameter path so the same Guid value reaches the same
        // row regardless of how the row was inserted.
        return $"X'{Convert.ToHexString(guid.ToByteArray(bigEndian: true))}'";
    }
}
