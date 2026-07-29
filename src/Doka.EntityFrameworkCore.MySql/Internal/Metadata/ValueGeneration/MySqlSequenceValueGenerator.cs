namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides MySQL-specific sequence value generation at runtime.
///
/// On MySQL (no native sequences): Uses table-based emulation via
/// a singleton table whose first call returns the configured start value and whose
/// subsequent calls atomically advance by the configured increment.
///
/// On MariaDB 10.3+ (native sequences): Uses
/// <c>SELECT NEXT VALUE FOR {name};</c>
/// </summary>
internal static class MySqlSequenceValueGenerator
{
    /// <summary>
    /// Fetches the next value from a sequence, using the appropriate strategy
    /// for the current server capabilities.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sequenceName">The logical sequence name.</param>
    /// <param name="increment">The increment value.</param>
    /// <param name="supportsNativeSequences">Whether the server supports native sequences (MariaDB 10.3+).</param>
    /// <returns>The next sequence value.</returns>
    public static long GetNextValue(
        DbConnection connection,
        string sequenceName,
        int increment,
        bool supportsNativeSequences
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(increment);

        using var command = connection.CreateCommand();
        command.CommandText = GenerateNextValueSql(sequenceName, increment, supportsNativeSequences);

        var result = command.ExecuteScalar();

        return ConvertResult(result);
    }

    /// <summary>
    /// Asynchronous version of <see cref="GetNextValue"/>.
    /// </summary>
    public static async Task<long> GetNextValueAsync(
        DbConnection connection,
        string sequenceName,
        int increment,
        bool supportsNativeSequences,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(increment);

        await using var command = connection.CreateCommand();
        command.CommandText = GenerateNextValueSql(sequenceName, increment, supportsNativeSequences);

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return ConvertResult(result);
    }

    /// <summary>
    /// Generates the scalar SQL used by both direct test coverage and EF's relational
    /// command path. Keeping it here prevents migration DDL and runtime fetch logic
    /// from evolving independently.
    /// </summary>
    internal static string GenerateNextValueSql(
        string sequenceName,
        int increment,
        bool supportsNativeSequences
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(increment);

        if (supportsNativeSequences)
        {
            return $"SELECT NEXT VALUE FOR {MySqlIdentifierEscaping.DelimitIdentifier(sequenceName)};";
        }

        var tableName = MySqlIdentifierEscaping.DelimitIdentifier(MySqlSequenceNaming.EmulationTableName(sequenceName));

        // LAST_INSERT_ID(expr) is session-scoped and returns expr. The singleton row
        // plus is_called flag makes the first fetch return StartValue and every later
        // fetch advance atomically without a preceding SELECT.
        return $"UPDATE {tableName}\n"
            + $"SET `value` = LAST_INSERT_ID(IF(`is_called`, `value` + {increment}, `value`)),\n"
            + "    `is_called` = TRUE\n"
            + "WHERE `id` = 1;\n"
            + "SELECT LAST_INSERT_ID();";
    }

    /// <summary>
    /// Converts connector scalar representations to the signed 64-bit sequence
    /// contract used by EF Core's Hi/Lo generator.
    /// </summary>
    internal static long ConvertResult(
        object? result
    ) => result switch
    {
        long longValue => longValue,
        int intValue => intValue,
        decimal decimalValue => (long)decimalValue,
        ulong ulongValue => checked((long)ulongValue),
        _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
    };
}
