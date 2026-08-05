namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads foreign keys from INFORMATION_SCHEMA.KEY_COLUMN_USAGE joined with
/// INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS for the OnDelete action. Groups by
/// (table, constraint) so composite-column FKs assemble their ordered column / referenced
/// column pairs. Resolves principals through the database-qualified lookup populated
/// across all selected MySQL databases. References outside that selection are skipped
/// with a structured warning so the operator can re-run scaffolding with a wider filter.
/// </summary>
internal static class ForeignKeyLoader
{
    public static void Load(
        ScaffoldingPipelineContext context,
        ILogger? logger
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                source.TABLE_SCHEMA,
                source.TABLE_NAME,
                source.CONSTRAINT_NAME,
                source.COLUMN_NAME,
                source.ORDINAL_POSITION,
                source.REFERENCED_TABLE_SCHEMA,
                source.REFERENCED_TABLE_NAME,
                source.REFERENCED_COLUMN_NAME,
                constraints.DELETE_RULE
            FROM information_schema.KEY_COLUMN_USAGE AS source
            INNER JOIN information_schema.REFERENTIAL_CONSTRAINTS AS constraints
                ON constraints.CONSTRAINT_SCHEMA = source.CONSTRAINT_SCHEMA
               AND constraints.CONSTRAINT_NAME = source.CONSTRAINT_NAME
            WHERE source.TABLE_SCHEMA = DATABASE()
              AND source.REFERENCED_TABLE_NAME IS NOT NULL
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "source.TABLE_NAME");
        sql.Append(" ORDER BY source.TABLE_NAME, source.CONSTRAINT_NAME, source.ORDINAL_POSITION;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        var foreignKeys = new Dictionary<(string TableName, string ForeignKeyName), DatabaseForeignKey>();

        while (reader.Read())
        {
            var sourceDatabaseName = reader.GetString(0);
            var tableName = reader.GetString(1);

            if (context.TemporalHistoryTables.Contains((sourceDatabaseName, tableName))
                || !context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var foreignKeyName = reader.GetString(2);
            var key = (tableName, foreignKeyName);

            if (!foreignKeys.TryGetValue(key, out var foreignKey))
            {
                var principalDatabaseName = reader.GetString(5);
                var principalTableName = reader.GetString(6);

                if (context.TemporalHistoryTables.Contains((principalDatabaseName, principalTableName))
                    || !context.DatabaseTables.TryGetValue(
                        (principalDatabaseName, principalTableName),
                        out var principalTable))
                {
                    if (logger is not null)
                    {
                        MySqlLoggerMessages.ForeignKeyPrincipalTableNotScaffolded(
                            logger,
                            foreignKeyName,
                            tableName,
                            principalTableName);
                    }

                    continue;
                }

                foreignKey = new DatabaseForeignKey
                {
                    Table = table,
                    Name = foreignKeyName,
                    PrincipalTable = principalTable,
                    OnDelete = ScaffoldingHelpers.ResolveReferentialAction(reader.GetString(8)),
                };

                table.ForeignKeys.Add(foreignKey);
                foreignKeys[key] = foreignKey;
            }

            var columnName = reader.GetString(3);
            var principalDatabaseNameForColumn = reader.GetString(5);
            var principalTableNameForColumn = reader.GetString(6);
            var principalColumnName = reader.GetString(7);

            if (context.DatabaseColumns.TryGetValue(
                    (sourceDatabaseName, tableName, columnName),
                    out var column))
            {
                foreignKey.Columns.Add(column);
            }

            if (context.DatabaseColumns.TryGetValue(
                    (principalDatabaseNameForColumn, principalTableNameForColumn, principalColumnName),
                    out var principalColumn))
            {
                foreignKey.PrincipalColumns.Add(principalColumn);
            }
        }
    }
}
