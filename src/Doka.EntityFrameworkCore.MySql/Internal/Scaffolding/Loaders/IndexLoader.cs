namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads non-primary indexes from INFORMATION_SCHEMA.STATISTICS. Captures the index
/// columns in composite order, the per-column descending flag (COLLATION = 'D'), the
/// FULLTEXT and SPATIAL index-type annotations, and the per-column SUB_PART prefix length emitted
/// as an int[] annotation <see cref="MySqlAnnotationNames.IndexPrefixLength"/> when
/// any column carries a non-null SUB_PART. MySQL functional key parts are retained as
/// <see cref="MySqlScaffoldedIndexPart"/> records because EF Core indexes require a
/// property for every key part and must not be populated with invented shadow properties.
/// </summary>
internal static class IndexLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        using var command = context.Connection.CreateCommand();
        var sql = CreateQuery(context.Profile.Family);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        var indexes = new Dictionary<(string TableName, string IndexName), DatabaseIndex>();
        var indexParts =
            new Dictionary<(string TableName, string IndexName), List<MySqlScaffoldedIndexPart>>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var indexName = reader.GetString(1);
            var key = (tableName, indexName);

            if (!indexes.TryGetValue(key, out var index))
            {
                index = new DatabaseIndex
                {
                    Table = table,
                    Name = indexName,
                    IsUnique = reader.GetInt64(3) == 0,
                };

                table.Indexes.Add(index);
                indexes[key] = index;
            }

            var columnName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var expression = reader.IsDBNull(8) ? null : reader.GetString(8);
            var collation = reader.IsDBNull(4) ? null : reader.GetString(4);
            var isDescending = string.Equals(collation, "D", StringComparison.OrdinalIgnoreCase);
            var indexType = reader.IsDBNull(6) ? null : reader.GetString(6);
            var subPart = reader.IsDBNull(7)
                ? (int?)null
                : Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture);

            var parts = indexParts.TryGetValue(key, out var existingParts)
                ? existingParts
                : indexParts[key] = [];

            parts.Add(new MySqlScaffoldedIndexPart(columnName, expression, isDescending, subPart));

            if (columnName is not null
                && context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                index.Columns.Add(column);
                index.IsDescending.Add(isDescending);
            }

            if (indexType is not null
                && (string.Equals(indexType, "SPATIAL", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(indexType, "RTREE", StringComparison.OrdinalIgnoreCase)))
            {
                index.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
            }

            if (string.Equals(indexType, "FULLTEXT", StringComparison.OrdinalIgnoreCase))
            {
                index.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
            }
        }

        foreach (var ((tableName, indexName), parts) in indexParts)
        {
            var index = indexes[(tableName, indexName)];

            if (parts.Any(part => part.Expression is not null))
            {
                index.SetAnnotation(MySqlAnnotationNames.ScaffoldingIndexParts, parts.ToArray());
                continue;
            }

            if ((index.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?) == true
                || (index.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?) == true)
            {
                continue;
            }

            var prefixLengths = parts
                .Select(part => part.PrefixLength ?? 0)
                .ToArray();

            if (prefixLengths.Any(length => length > 0))
            {
                index.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, prefixLengths);
            }
        }
    }

    private static StringBuilder CreateQuery(
        EngineFamily family
    ) => new(
        family == EngineFamily.MySql
            ?
            """
            SELECT
                TABLE_NAME,
                INDEX_NAME,
                COLUMN_NAME,
                NON_UNIQUE,
                COLLATION,
                SEQ_IN_INDEX,
                INDEX_TYPE,
                SUB_PART,
                EXPRESSION
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND INDEX_NAME <> 'PRIMARY'
            """
            :
            """
            SELECT
                TABLE_NAME,
                INDEX_NAME,
                COLUMN_NAME,
                NON_UNIQUE,
                COLLATION,
                SEQ_IN_INDEX,
                INDEX_TYPE,
                SUB_PART,
                NULL AS EXPRESSION
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND INDEX_NAME <> 'PRIMARY'
            """);
}

internal sealed record MySqlScaffoldedIndexPart(
    string? ColumnName,
    string? Expression,
    bool IsDescending,
    int? PrefixLength
);
