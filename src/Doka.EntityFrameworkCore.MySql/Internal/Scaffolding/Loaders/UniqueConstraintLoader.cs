namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads unique constraints from INFORMATION_SCHEMA.STATISTICS where NON_UNIQUE = 0 and
/// the index name is not the reserved PRIMARY. One row per (table, constraint, column)
/// triple in composite order; the loader groups by (table, constraint) and assembles
/// each <see cref="DatabaseUniqueConstraint"/> with its ordered columns.
/// </summary>
internal static class UniqueConstraintLoader
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
                INDEX_NAME,
                COLUMN_NAME,
                SEQ_IN_INDEX
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND NON_UNIQUE = 0
              AND INDEX_NAME <> 'PRIMARY'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        var constraints = new Dictionary<(string TableName, string ConstraintName), DatabaseUniqueConstraint>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var constraintName = reader.GetString(1);
            var key = (tableName, constraintName);

            if (!constraints.TryGetValue(key, out var uniqueConstraint))
            {
                uniqueConstraint = new DatabaseUniqueConstraint
                {
                    Table = table,
                    Name = constraintName,
                };

                table.UniqueConstraints.Add(uniqueConstraint);
                constraints[key] = uniqueConstraint;
            }

            var columnName = reader.GetString(2);

            if (context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                uniqueConstraint.Columns.Add(column);
            }
        }
    }
}
