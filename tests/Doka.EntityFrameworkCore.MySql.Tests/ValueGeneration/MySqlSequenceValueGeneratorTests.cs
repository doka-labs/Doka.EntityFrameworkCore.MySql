namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the database-independent contracts of the runtime sequence reader.
/// Live integration tests own successful command execution; these tests pin the
/// SQL strategy, command preconditions, and connector scalar result contract
/// without requiring a running database.
/// </summary>
public sealed class MySqlSequenceValueGeneratorTests
{
    /// <summary>
    /// Verifies native sequence SQL escapes the configured sequence name.
    /// </summary>
    [Fact]
    public void GenerateNextValueSql_uses_native_sequence_syntax()
    {
        // Arrange
        const string sequenceName = "order`sequence";
        const int increment = 7;

        // Act
        var sql = MySqlSequenceValueGenerator.GenerateNextValueSql(
            sequenceName, increment, supportsNativeSequences: true);

        // Assert
        Assert.Equal("SELECT NEXT VALUE FOR `order``sequence`;", sql);
    }

    /// <summary>
    /// Verifies emulated sequence SQL advances the escaped sequence table atomically.
    /// </summary>
    [Fact]
    public void GenerateNextValueSql_uses_atomic_table_emulation()
    {
        // Arrange
        const string sequenceName = "order`sequence";
        const int increment = 7;

        // Act
        var sql = MySqlSequenceValueGenerator.GenerateNextValueSql(
            sequenceName,
            increment,
            supportsNativeSequences: false);

        // Assert
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

    /// <summary>
    /// Verifies the synchronous sequence reader rejects a missing connection before creating a command.
    /// </summary>
    [Fact]
    public void GetNextValue_rejects_a_missing_connection()
    {
        // Arrange
        DbConnection connection = null!;

        // Act
        // WHY: The synchronous reader has its own public argument-validation boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var recordedException = Record.Exception(() =>
            MySqlSequenceValueGenerator.GetNextValue(connection, "orders", 1, supportsNativeSequences: false));

        // Assert
        Assert.Equal("connection", Assert.IsType<ArgumentNullException>(recordedException).ParamName);
    }

    /// <summary>
    /// Verifies the asynchronous sequence reader rejects a missing connection before creating a command.
    /// </summary>
    [Fact]
    public async Task GetNextValueAsync_rejects_a_missing_connection()
    {
        // Arrange
        DbConnection connection = null!;

        // Act
        var recordedException = await Record.ExceptionAsync(() =>
            MySqlSequenceValueGenerator.GetNextValueAsync(
                connection, "orders", 1, supportsNativeSequences: false, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("connection", Assert.IsType<ArgumentNullException>(recordedException).ParamName);
    }

    /// <summary>
    /// Supplies the scalar representations returned by connector sequence commands.
    /// </summary>
    public static TheoryData<object, long> ConnectorScalarRepresentations =>
    [
        new(42L, 42L),
        new(42, 42L),
        new(42m, 42L),
        new(42UL, 42L),
        new("42", 42L),
    ];

    /// <summary>
    /// Verifies a connector scalar converts to the signed 64-bit sequence contract.
    /// </summary>
    /// <param name="result">The value returned by the connector.</param>
    /// <param name="expected">The expected signed sequence value.</param>
    [Theory]
    [MemberData(nameof(ConnectorScalarRepresentations))]
    public void ConvertResult_accepts_connector_scalar_representations(
        object result,
        long expected
    )
    {
        // Arrange
        var connectorResult = result;

        // Act
        var actual = MySqlSequenceValueGenerator.ConvertResult(connectorResult);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies missing connector scalar values cannot become a sequence value.
    /// </summary>
    /// <param name="result">The missing connector scalar representation.</param>
    [Theory]
    [InlineData(null)]
    [MemberData(nameof(DbNullScalarRepresentation))]
    public void ConvertResult_rejects_missing_database_values(
        object? result
    )
    {
        // Arrange
        var connectorResult = result;

        // Act
        var recordedException = Record.Exception(() => MySqlSequenceValueGenerator.ConvertResult(connectorResult));

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal("The database did not return a sequence value.", exception.Message);
    }

    /// <summary>
    /// Supplies the database null scalar separately from a CLR null reference.
    /// </summary>
    public static TheoryData<object> DbNullScalarRepresentation =>
    [
        DBNull.Value,
    ];

    /// <summary>
    /// Verifies unsigned connector values cannot overflow the signed 64-bit sequence contract silently.
    /// </summary>
    [Fact]
    public void ConvertResult_rejects_unsigned_values_above_int64_max_value()
    {
        // Arrange
        const ulong connectorResult = ulong.MaxValue;

        // Act
        var recordedException = Record.Exception(() => MySqlSequenceValueGenerator.ConvertResult(connectorResult));

        // Assert
        Assert.IsType<OverflowException>(recordedException);
    }
}
