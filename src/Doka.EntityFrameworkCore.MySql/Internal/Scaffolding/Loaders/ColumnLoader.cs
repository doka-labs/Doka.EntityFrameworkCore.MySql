namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads columns from INFORMATION_SCHEMA.COLUMNS for every table the
/// <see cref="TableLoader"/> registered. Resolves the EF Core store type, default
/// expression, computed-column SQL, value-generated kind, comment, and collation.
/// Lifts MariaDB LONGTEXT columns with utf8mb4_bin collation back to "json" when the
/// <see cref="JsonCheckConstraintLoader"/> recorded a matching json_valid CHECK
/// constraint. Populates <see cref="ScaffoldingPipelineContext.Columns"/> for the
/// downstream key / index / FK loaders.
/// </summary>
internal static class ColumnLoader
{
    private const string DefaultMariaDbJsonCollation = "utf8mb4_bin";

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
                COLUMN_NAME,
                IS_NULLABLE,
                COLUMN_TYPE,
                DATA_TYPE,
                COLUMN_DEFAULT,
                EXTRA,
                GENERATION_EXPRESSION,
                COLUMN_COMMENT,
                COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, ORDINAL_POSITION;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName))
            {
                continue;
            }

            if (!context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var columnName = reader.GetString(1);
            var storeType = reader.GetString(3);
            var dataType = reader.GetString(4);
            var extra = reader.IsDBNull(6) ? null : reader.GetString(6);
            var computedColumnSql = reader.IsDBNull(7) ? null : reader.GetString(7);
            var rawCollation = reader.IsDBNull(9) ? null : reader.GetString(9);
            var collation = rawCollation;
            var tableCollation = table.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value as string;
            var comment = reader.IsDBNull(8) ? null : reader.GetString(8);

            if (string.Equals(collation, tableCollation, StringComparison.OrdinalIgnoreCase))
            {
                collation = null;
            }

            if (string.IsNullOrEmpty(comment))
            {
                comment = null;
            }

            var column = new DatabaseColumn
            {
                Table = table,
                Name = columnName,
                StoreType = NormalizeStoreType(
                    dataType,
                    storeType,
                    tableName,
                    columnName,
                    rawCollation,
                    context.MariaDbJsonColumns),
                IsNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                DefaultValueSql = reader.IsDBNull(5) ? null : reader.GetString(5),
                ComputedColumnSql = string.IsNullOrWhiteSpace(computedColumnSql) ? null : computedColumnSql,
                IsStored = ScaffoldingHelpers.ResolveIsStored(extra),
                Comment = comment,
                Collation = collation,
                ValueGenerated = ScaffoldingHelpers.ResolveValueGenerated(extra),
            };

            table.Columns.Add(column);
            context.Columns[(tableName, columnName)] = column;
            context.DatabaseColumns[(context.DatabaseName, tableName, columnName)] = column;
        }
    }

    private static string NormalizeStoreType(
        string dataType,
        string storeType,
        string tableName,
        string columnName,
        string? collation,
        HashSet<(string TableName, string ColumnName)> mariaDbJsonColumns
    )
    {
        storeType = NormalizeIntegerDisplayWidth(dataType, storeType);
        storeType = NormalizeYearDisplayWidth(dataType, storeType);

        if (!string.Equals(dataType, "longtext", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(collation, DefaultMariaDbJsonCollation, StringComparison.OrdinalIgnoreCase))
        {
            return storeType;
        }

        return mariaDbJsonColumns.Contains((tableName, columnName)) ? "json" : storeType;
    }

    /// <summary>
    /// Removes legacy integer display widths that do not affect storage or range while
    /// preserving boolean inference through <c>tinyint(1)</c> and visible ZEROFILL padding.
    /// </summary>
    internal static string NormalizeIntegerDisplayWidth(
        string dataType,
        string storeType
    )
    {
        var canonicalDataType = CanonicalIntegerDataType(dataType);

        if (canonicalDataType is null
            || !storeType.StartsWith(canonicalDataType, StringComparison.OrdinalIgnoreCase)
            || storeType.Length <= canonicalDataType.Length + 2
            || storeType[canonicalDataType.Length] != '(')
        {
            return storeType;
        }

        var closingParenthesis = storeType.IndexOf(')', canonicalDataType.Length + 1);

        if (closingParenthesis < 0)
        {
            return storeType;
        }

        var width = storeType.AsSpan(canonicalDataType.Length + 1, closingParenthesis - canonicalDataType.Length - 1);
        var suffix = storeType[(closingParenthesis + 1)..];

        if (width.IsEmpty
            || width.IndexOfAnyExceptInRange('0', '9') >= 0
            || (!string.IsNullOrEmpty(suffix)
                && !string.Equals(suffix, " unsigned", StringComparison.OrdinalIgnoreCase))
            || (canonicalDataType == "tinyint" && width is "1"))
        {
            return storeType;
        }

        return canonicalDataType + suffix.ToLowerInvariant();
    }

    private static string? CanonicalIntegerDataType(
        string dataType
    )
    {
        if (string.Equals(dataType, "tinyint", StringComparison.OrdinalIgnoreCase))
        {
            return "tinyint";
        }

        if (string.Equals(dataType, "smallint", StringComparison.OrdinalIgnoreCase))
        {
            return "smallint";
        }

        if (string.Equals(dataType, "mediumint", StringComparison.OrdinalIgnoreCase))
        {
            return "mediumint";
        }

        if (string.Equals(dataType, "int", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataType, "integer", StringComparison.OrdinalIgnoreCase))
        {
            return "int";
        }

        return string.Equals(dataType, "bigint", StringComparison.OrdinalIgnoreCase)
            ? "bigint"
            : null;
    }

    /// <summary>
    /// Removes the optional four-digit YEAR display width while preserving the
    /// semantically different and deprecated two-digit form.
    /// </summary>
    internal static string NormalizeYearDisplayWidth(
        string dataType,
        string storeType
    )
    {
        return string.Equals(dataType, "year", StringComparison.OrdinalIgnoreCase)
            && string.Equals(storeType, "year(4)", StringComparison.OrdinalIgnoreCase)
                ? "year"
                : storeType;
    }
}
