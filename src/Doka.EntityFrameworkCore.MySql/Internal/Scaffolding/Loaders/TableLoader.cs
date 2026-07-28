namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads tables and views from INFORMATION_SCHEMA.TABLES into the
/// <see cref="ScaffoldingPipelineContext.DatabaseModel"/> and populates
/// <see cref="ScaffoldingPipelineContext.TableLookup"/> for the downstream loaders.
/// Attaches the table-level CharSet, Collation, and StorageEngine annotations.
/// </summary>
internal static class TableLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TABLE_NAME,
                TABLE_COLLATION,
                TABLE_COMMENT,
                ENGINE,
                TABLE_TYPE
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName))
            {
                continue;
            }

            var tableType = reader.IsDBNull(4) ? "BASE TABLE" : reader.GetString(4);
            var isView = string.Equals(tableType, "VIEW", StringComparison.OrdinalIgnoreCase);
            var comment = reader.IsDBNull(2)
                ? null
                : reader.GetString(2);

            if (string.IsNullOrEmpty(comment))
            {
                comment = null;
            }

            var table = isView
                ? new DatabaseView
                {
                    Database = context.DatabaseModel,
                    Name = tableName,
                    Comment = comment,
                }
                : new DatabaseTable
                {
                    Database = context.DatabaseModel,
                    Name = tableName,
                    Comment = comment,
                };

            var tableCollation = reader.IsDBNull(1) ? null : reader.GetString(1);
            var storageEngine = reader.IsDBNull(3) ? null : reader.GetString(3);

            if (!string.IsNullOrWhiteSpace(storageEngine))
            {
                table.SetAnnotation(MySqlAnnotationNames.StorageEngine, storageEngine);
            }

            if (!string.IsNullOrWhiteSpace(tableCollation))
            {
                table.SetAnnotation(RelationalAnnotationNames.Collation, tableCollation);

                var charSet = ScaffoldingHelpers.DeriveCharSetFromCollation(tableCollation);

                if (!string.IsNullOrWhiteSpace(charSet))
                {
                    table.SetAnnotation(MySqlAnnotationNames.CharSet, charSet);
                }
            }

            context.DatabaseModel.Tables.Add(table);
            context.TableLookup[tableName] = table;
        }
    }
}
