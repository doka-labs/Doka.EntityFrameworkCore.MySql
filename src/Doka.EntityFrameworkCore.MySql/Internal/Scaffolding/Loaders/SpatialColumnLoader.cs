namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads spatial-column metadata from INFORMATION_SCHEMA.ST_GEOMETRY_COLUMNS. Annotates
/// each matching column with the source SRID via
/// <see cref="MySqlAnnotationNames.SpatialReferenceSystemId"/>. Skipped when the engine
/// does not advertise SupportsSpatialColumnSridAttribute.
/// </summary>
internal static class SpatialColumnLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Profile.Has(Capability.SupportsSpatialColumnSridAttribute))
        {
            return;
        }

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                TABLE_NAME,
                COLUMN_NAME,
                SRS_ID
            FROM information_schema.ST_GEOMETRY_COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, COLUMN_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName)
                || reader.IsDBNull(2))
            {
                continue;
            }

            var columnName = reader.GetString(1);

            if (context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                column.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, reader.GetInt32(2));
            }
        }
    }
}
