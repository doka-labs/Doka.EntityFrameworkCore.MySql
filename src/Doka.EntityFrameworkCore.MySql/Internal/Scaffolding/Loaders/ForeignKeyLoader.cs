namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads foreign keys from INFORMATION_SCHEMA.KEY_COLUMN_USAGE joined with
/// INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS for the OnDelete action. Groups by
/// (table, constraint) so composite-column FKs assemble their ordered column / referenced
/// column pairs. Skips FKs that reference a table the
/// <see cref="ScaffoldingPipelineContext.TableLookup"/> does not contain (the principal
/// fell outside the filter) and emits a structured warning so the operator can re-run
/// scaffolding with a wider filter when this matters.
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
                source.TABLE_NAME,
                source.CONSTRAINT_NAME,
                source.COLUMN_NAME,
                source.ORDINAL_POSITION,
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
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var foreignKeyName = reader.GetString(1);
            var key = (tableName, foreignKeyName);

            if (!foreignKeys.TryGetValue(key, out var foreignKey))
            {
                var principalTableName = reader.GetString(4);

                if (!context.TableLookup.TryGetValue(principalTableName, out var principalTable))
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
                    OnDelete = ScaffoldingHelpers.ResolveReferentialAction(reader.GetString(6)),
                };

                table.ForeignKeys.Add(foreignKey);
                foreignKeys[key] = foreignKey;
            }

            var columnName = reader.GetString(2);
            var principalColumnName = reader.GetString(5);

            if (context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                foreignKey.Columns.Add(column);
            }

            if (context.Columns.TryGetValue((foreignKey.PrincipalTable.Name, principalColumnName), out var principalColumn))
            {
                foreignKey.PrincipalColumns.Add(principalColumn);
            }
        }
    }
}
