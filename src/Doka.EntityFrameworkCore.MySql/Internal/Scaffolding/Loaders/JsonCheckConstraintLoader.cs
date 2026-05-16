namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads the set of MariaDB columns guarded by a JSON_VALID CHECK constraint. MariaDB
/// stores JSON columns as LONGTEXT with utf8mb4_bin collation plus a CHECK constraint
/// of the form <c>json_valid(`column`)</c>; the
/// <see cref="ColumnLoader"/> uses the returned set to lift those columns back to the
/// canonical "json" store type during reverse engineering. Runs once per scaffolding
/// pass on MariaDB engines only; returns an empty set on MySQL.
/// </summary>
internal static class JsonCheckConstraintLoader
{
    public static HashSet<(string TableName, string ColumnName)> Load(
        DbConnection connection,
        TableFilter tableFilter
    )
    {
        ArgumentNullException.ThrowIfNull(connection);

        var result = new HashSet<(string TableName, string ColumnName)>(CaseInsensitiveColumnTupleComparer.Instance);

        using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TABLE_NAME,
                CHECK_CLAUSE
            FROM information_schema.CHECK_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND LOWER(CHECK_CLAUSE) LIKE '%json_valid%'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, tableFilter);
        sql.Append(';');
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName))
            {
                continue;
            }

            var checkClause = reader.GetString(1);
            var columnName = ScaffoldingHelpers.ExtractJsonValidColumnName(checkClause);

            if (columnName is not null)
            {
                result.Add((tableName, columnName));
            }
        }

        return result;
    }
}
