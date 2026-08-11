namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the database-independent contracts of the runtime sequence reader.
/// Live integration tests own successful command execution; these tests pin the
/// SQL strategy, command preconditions, and connector scalar result contract
/// without requiring a running database.
/// </summary>
public sealed class MySqlSequenceValueGeneratorTests
{
    [Fact]
    public void GenerateNextValueSql_uses_native_sequence_syntax()
    {
        var sql = MySqlSequenceValueGenerator.GenerateNextValueSql("order`sequence", 7, supportsNativeSequences: true);

        Assert.Equal("SELECT NEXT VALUE FOR `order``sequence`;", sql);
    }

    [Fact]
    public void GenerateNextValueSql_uses_atomic_table_emulation()
    {
        var sql = MySqlSequenceValueGenerator.GenerateNextValueSql(
            "order`sequence",
            7,
            supportsNativeSequences: false);

        Assert.Equal(
            """
            UPDATE `__efsequence_order``sequence`
            SET `value` = LAST_INSERT_ID(IF(`is_called`, `value` + 7, `value`)),
                `is_called` = TRUE
            WHERE `id` = 1;
            SELECT LAST_INSERT_ID();
            """,
            sql);
    }

    [Fact]
    public void GetNextValue_rejects_a_missing_connection() => Assert.Throws<ArgumentNullException>(() =>
        MySqlSequenceValueGenerator.GetNextValue(null!, "orders", 1, supportsNativeSequences: false));

    [Fact]
    public async Task GetNextValueAsync_rejects_a_missing_connection() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            MySqlSequenceValueGenerator.GetNextValueAsync(null!, "orders", 1, supportsNativeSequences: false));

    public static TheoryData<object, long> ConnectorScalarRepresentations =>
        new()
        {
            { 42L, 42L },
            { 42, 42L },
            { 42m, 42L },
            { 42UL, 42L },
            { "42", 42L },
        };

    [Theory]
    [MemberData(nameof(ConnectorScalarRepresentations))]
    public void ConvertResult_accepts_connector_scalar_representations(
        object result,
        long expected
    ) => Assert.Equal(expected, MySqlSequenceValueGenerator.ConvertResult(result));

    [Theory]
    [InlineData(null)]
    [MemberData(nameof(DbNullScalarRepresentation))]
    public void ConvertResult_rejects_missing_database_values(
        object? result
    )
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => MySqlSequenceValueGenerator.ConvertResult(result));

        Assert.Equal("The database did not return a sequence value.", exception.Message);
    }

    public static TheoryData<object> DbNullScalarRepresentation =>
        new()
        {
            DBNull.Value,
        };

    [Fact]
    public void ConvertResult_rejects_unsigned_values_above_int64_max_value() =>
        Assert.Throws<OverflowException>(() => MySqlSequenceValueGenerator.ConvertResult(ulong.MaxValue));
}
