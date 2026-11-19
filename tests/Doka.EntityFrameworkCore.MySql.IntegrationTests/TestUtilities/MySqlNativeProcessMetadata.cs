namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Selects native process metadata that can safely expose concurrent command text.</summary>
internal static class MySqlNativeProcessMetadata
{
    /// <summary>Returns the native process source for the connected server family.</summary>
    /// <param name="connection">The open connection whose server owns the process metadata.</param>
    /// <returns>A process source exposing the native ID, STATE and INFO columns.</returns>
    internal static string GetProcessListSource(
        MySqlConnection connection
    )
    {
        // WHY: MySQL materializes INFORMATION_SCHEMA.INFO before filtering, so unrelated
        // binary SQL can break even STATE-only reads. Performance Schema avoids that path;
        // MariaDB can run with Performance Schema disabled and retains its native source.
        return connection.ServerVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase)
            ? "information_schema.PROCESSLIST"
            : "performance_schema.processlist";
    }

    /// <summary>Builds active-statement diagnostics without invoking MySQL's legacy metadata conversion.</summary>
    /// <param name="connection">The open connection whose server owns the process metadata.</param>
    /// <returns>SQL using the @tableName parameter to locate ASCII statement text outside the observer session.</returns>
    internal static string GetActiveStatementsCommandText(
        MySqlConnection connection
    )
    {
        var processSource = GetProcessListSource(connection);

        // WHY: MySQL's INNODB_TRX can materialize unrelated binary command text too.
        // Keep its diagnostics in Performance Schema; MariaDB retains its transaction details.
        return processSource == "performance_schema.processlist"
            ? $"""
            SELECT GROUP_CONCAT(CONCAT_WS(' | ', p.ID, p.STATE, CONVERT(CAST(p.INFO AS BINARY) USING latin1))
                SEPARATOR '\n')
            FROM {processSource} AS p
            WHERE p.ID <> CONNECTION_ID()
                AND LOCATE(@tableName, COALESCE(CONVERT(CAST(p.INFO AS BINARY) USING latin1), '')) > 0;
            """
            : $"""
            SELECT GROUP_CONCAT(CONCAT_WS(' | ', p.ID, p.STATE, t.TRX_STATE, t.TRX_OPERATION_STATE,
                CONVERT(CAST(p.INFO AS BINARY) USING latin1))
                SEPARATOR '\n')
            FROM {processSource} AS p
            LEFT JOIN information_schema.INNODB_TRX AS t ON t.TRX_MYSQL_THREAD_ID = p.ID
            WHERE p.ID <> CONNECTION_ID()
                AND LOCATE(@tableName, COALESCE(CONVERT(CAST(p.INFO AS BINARY) USING latin1), '')) > 0;
            """;
    }
}
