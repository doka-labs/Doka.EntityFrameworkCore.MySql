namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads primary keys from INFORMATION_SCHEMA.KEY_COLUMN_USAGE for every table the
/// <see cref="TableLoader"/> registered. One row per (table, column) pair in
/// composite-key order; the loader groups by table and assembles the
/// <see cref="DatabasePrimaryKey"/> with its ordered column list.
/// </summary>
internal static class PrimaryKeyLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder("""
                                    SELECT
                                        TABLE_NAME,
                                        COLUMN_NAME,
                                        CONSTRAINT_NAME,
                                        ORDINAL_POSITION
                                    FROM information_schema.KEY_COLUMN_USAGE
                                    WHERE TABLE_SCHEMA = DATABASE()
                                      AND CONSTRAINT_NAME = 'PRIMARY'
                                    """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, ORDINAL_POSITION;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        var primaryKeys = new Dictionary<string, DatabasePrimaryKey>(StringComparer.Ordinal);

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            if (!primaryKeys.TryGetValue(tableName, out var primaryKey))
            {
                primaryKey = new DatabasePrimaryKey
                {
                    Table = table,
                    Name = reader.GetString(2),
                };

                table.PrimaryKey = primaryKey;
                primaryKeys[tableName] = primaryKey;
            }

            var columnName = reader.GetString(1);

            if (context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                primaryKey.Columns.Add(column);
            }
        }
    }
}
