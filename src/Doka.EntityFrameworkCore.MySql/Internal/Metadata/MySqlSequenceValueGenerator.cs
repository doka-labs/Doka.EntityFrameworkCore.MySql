namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides MySQL-specific sequence value generation at runtime.
///
/// On MySQL (no native sequences): Uses table-based emulation via
/// <c>UPDATE __efsequence_{name} SET value = LAST_INSERT_ID(value + increment); SELECT LAST_INSERT_ID();</c>
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

        using var command = connection.CreateCommand();

        if (supportsNativeSequences)
        {
            // MariaDB 10.3+: native sequence support.
            command.CommandText = $"SELECT NEXT VALUE FOR {DelimitIdentifier(sequenceName)};";
        }
        else
        {
            // MySQL: table-based sequence emulation.
            // The LAST_INSERT_ID(expr) function sets the session's LAST_INSERT_ID to expr
            // and returns it. This makes the UPDATE + SELECT atomic within the session.
            var tableName = DelimitIdentifier("__efsequence_" + sequenceName);
            command.CommandText = $"UPDATE {tableName} SET `value` = LAST_INSERT_ID(`value` + {increment});\n"
                + "SELECT LAST_INSERT_ID();";
        }

        var result = command.ExecuteScalar();

        return result switch
        {
            long longValue => longValue,
            int intValue => intValue,
            decimal decimalValue => (long)decimalValue,
            ulong ulongValue => (long)ulongValue,
            _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
        };
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

        await using var command = connection.CreateCommand();

        if (supportsNativeSequences)
        {
            command.CommandText = $"SELECT NEXT VALUE FOR {DelimitIdentifier(sequenceName)};";
        }
        else
        {
            var tableName = DelimitIdentifier("__efsequence_" + sequenceName);
            command.CommandText = $"UPDATE {tableName} SET `value` = LAST_INSERT_ID(`value` + {increment});\n"
                + "SELECT LAST_INSERT_ID();";
        }

        var result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            long longValue => longValue,
            int intValue => intValue,
            decimal decimalValue => (long)decimalValue,
            ulong ulongValue => (long)ulongValue,
            _ => Convert.ToInt64(result, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Backtick-quotes a sequence or emulation-table identifier so a user-supplied
    /// sequence name containing a backtick cannot terminate the enclosing identifier
    /// and let arbitrary SQL run past the boundary. The escape semantics mirror
    /// <c>MySqlSqlGenerationHelper.DelimitIdentifier</c> -- doubling every backtick
    /// then wrapping in backticks -- so a sequence-name change between the runtime
    /// helper and this generator can never silently produce different SQL.
    /// </summary>
    internal static string DelimitIdentifier(
        string identifier
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return identifier.AsSpan().IndexOf('`') < 0
            ? "`" + identifier + "`"
            : "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
    }
}
