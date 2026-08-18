namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Restores spatial SRID metadata from the engine-specific enforcement mechanism.
/// MySQL exposes native SRID attributes through ST_GEOMETRY_COLUMNS. MariaDB uses
/// the provider-owned column CHECK shape because it has no enforcing SRID attribute.
/// </summary>
internal static class SpatialColumnLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (context.Profile.GetSupport(ProviderCapability.SpatialColumnSridEnforcement))
        {
            case ProviderSupportStatus.Native:
                LoadNativeAttributes(context);
                return;
            case ProviderSupportStatus.Emulated:
                // CheckConstraintLoader owns the MariaDB CHECK catalog query and
                // applies provider-owned SRID checks while preserving user checks.
                return;
            case ProviderSupportStatus.UnsupportedByEngine:
                return;
            default:
                throw new InvalidOperationException("Unknown spatial SRID enforcement status.");
        }
    }

    private static void LoadNativeAttributes(
        ScaffoldingPipelineContext context
    )
    {
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

    public static bool TryApplyMariaDbCheck(
        ScaffoldingPipelineContext context,
        string tableName,
        string constraintName,
        string checkClause
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        ArgumentNullException.ThrowIfNull(checkClause);

        if (context.Profile.GetSupport(ProviderCapability.SpatialColumnSridEnforcement)
            != ProviderSupportStatus.Emulated
            || !MariaDbSpatialSridCheckConstraintParser.TryParse(
                checkClause,
                out var columnName,
                out var spatialReferenceSystemId)
            || !string.Equals(constraintName, columnName, StringComparison.Ordinal)
            || !context.Columns.TryGetValue((tableName, columnName), out var column))
        {
            return false;
        }

        column.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, spatialReferenceSystemId);
        return true;
    }
}
