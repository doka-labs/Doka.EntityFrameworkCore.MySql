namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reconstructs MariaDB application-time periods and their <c>WITHOUT OVERLAPS</c>
/// constraints from the 11.4+ INFORMATION_SCHEMA period catalogs.
/// </summary>
internal static class ApplicationTimeTableLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        // MariaDB exposes no stable catalog contract for application-time periods
        // before 11.4. Guessing from column names or SHOW CREATE TABLE would make
        // reverse engineering depend on formatting and user naming conventions.
        if (!context.Profile.Engine.Has(EngineCapability.TemporalPeriodCatalog))
        {
            return;
        }

        LoadPeriods(context);
        LoadWithoutOverlapsConstraints(context);
    }

    private static void LoadPeriods(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();

        // PERIODS contains both temporal dimensions for bitemporal tables.
        // TemporalTableLoader owns SYSTEM_TIME; this loader owns only the
        // user-named application-time period.
        var sql = new StringBuilder(
            """
            SELECT
                periods.TABLE_NAME,
                periods.PERIOD,
                periods.START_COLUMN_NAME,
                periods.END_COLUMN_NAME
            FROM information_schema.PERIODS AS periods
            WHERE periods.TABLE_SCHEMA = DATABASE()
              AND periods.PERIOD <> 'SYSTEM_TIME'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "periods.TABLE_NAME");
        sql.Append(" ORDER BY periods.TABLE_NAME, periods.PERIOD;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var periodName = reader.GetString(1);
            var startColumnName = reader.GetString(2);
            var endColumnName = reader.GetString(3);

            if (!context.Columns.ContainsKey((tableName, startColumnName))
                || !context.Columns.ContainsKey((tableName, endColumnName)))
            {
                throw new InvalidOperationException(
                    $"Application-time table '{tableName}' exposes period '{periodName}' with missing boundary "
                    + $"columns '{startColumnName}' and '{endColumnName}'.");
            }

            if (table.FindAnnotation(MySqlAnnotationNames.IsApplicationTime) is not null)
            {
                throw new InvalidOperationException(
                    $"Application-time table '{tableName}' exposes more than one application-time period.");
            }

            table.SetAnnotation(MySqlAnnotationNames.IsApplicationTime, true);
            table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodName, periodName);
            table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodStartColumn, startColumnName);
            table.SetAnnotation(MySqlAnnotationNames.ApplicationTimePeriodEndColumn, endColumnName);
        }
    }

    private static void LoadWithoutOverlapsConstraints(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                period_keys.TABLE_NAME,
                period_keys.CONSTRAINT_NAME,
                period_keys.PERIOD_NAME
            FROM information_schema.KEY_PERIOD_USAGE AS period_keys
            WHERE period_keys.TABLE_SCHEMA = DATABASE()
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "period_keys.TABLE_NAME");
        sql.Append(" ORDER BY period_keys.TABLE_NAME, period_keys.CONSTRAINT_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableLookup.TryGetValue(tableName, out var table)
                || table.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)
                    ?.Value is not true)
            {
                continue;
            }

            var constraintName = reader.GetString(1);
            var periodName = reader.GetString(2);

            if (!string.Equals(
                    table.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodName)
                        ?.Value as string,
                    periodName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Application-time constraint '{constraintName}' on table '{tableName}' references unknown "
                    + $"period '{periodName}'.");
            }

            if (string.Equals(constraintName, "PRIMARY", StringComparison.OrdinalIgnoreCase))
            {
                if (table.PrimaryKey is null)
                {
                    throw new InvalidOperationException(
                        $"Application-time table '{tableName}' exposes a PRIMARY WITHOUT OVERLAPS constraint "
                        + "without a primary key.");
                }

                table.PrimaryKey.SetAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, true);
                table.SetAnnotation(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps, true);
                continue;
            }

            var uniqueConstraint = table.UniqueConstraints.SingleOrDefault(constraint =>
                string.Equals(constraint.Name, constraintName, StringComparison.Ordinal));
            var index = table.Indexes.SingleOrDefault(candidate => string.Equals(
                candidate.Name,
                constraintName,
                StringComparison.Ordinal));

            if (uniqueConstraint is null
                && index is null)
            {
                throw new InvalidOperationException(
                    $"Application-time table '{tableName}' exposes WITHOUT OVERLAPS constraint "
                    + $"'{constraintName}' without matching unique constraint or index metadata.");
            }

            uniqueConstraint?.SetAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, true);
            index?.SetAnnotation(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps, true);
        }
    }
}
