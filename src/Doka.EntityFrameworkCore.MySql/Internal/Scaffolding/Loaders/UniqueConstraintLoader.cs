namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads declared unique constraints from INFORMATION_SCHEMA.TABLE_CONSTRAINTS and
/// KEY_COLUMN_USAGE. The index loader reads the physical index representation separately;
/// the model factory deduplicates matching names when it builds EF metadata.
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
                constraints.TABLE_NAME,
                constraints.CONSTRAINT_NAME,
                columns.COLUMN_NAME,
                columns.ORDINAL_POSITION
            FROM information_schema.TABLE_CONSTRAINTS AS constraints
            INNER JOIN information_schema.KEY_COLUMN_USAGE AS columns
                ON columns.CONSTRAINT_SCHEMA = constraints.CONSTRAINT_SCHEMA
                AND columns.TABLE_NAME = constraints.TABLE_NAME
                AND columns.CONSTRAINT_NAME = constraints.CONSTRAINT_NAME
            WHERE constraints.TABLE_SCHEMA = DATABASE()
              AND constraints.CONSTRAINT_TYPE = 'UNIQUE'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "constraints.TABLE_NAME");
        sql.Append(" ORDER BY constraints.TABLE_NAME, constraints.CONSTRAINT_NAME, columns.ORDINAL_POSITION;");
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
