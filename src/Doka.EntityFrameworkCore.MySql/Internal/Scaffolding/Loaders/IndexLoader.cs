namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads non-primary indexes from INFORMATION_SCHEMA.STATISTICS. Captures the index
/// columns in composite order, the per-column descending flag (COLLATION = 'D'), the
/// SPATIAL index-type annotation, and the per-column SUB_PART prefix length emitted
/// as an int[] annotation <see cref="MySqlAnnotationNames.IndexPrefixLength"/> when
/// any column carries a non-null SUB_PART. The previous monolith silently dropped the
/// prefix-length data; the array shape preserves the per-column position so a future
/// migration generator can emit <c>KEY ix (col(N))</c> faithfully.
/// </summary>
internal static class IndexLoader
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
                NON_UNIQUE,
                COLLATION,
                SEQ_IN_INDEX,
                INDEX_TYPE,
                SUB_PART
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND INDEX_NAME <> 'PRIMARY'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();

        var indexes = new Dictionary<(string TableName, string IndexName), DatabaseIndex>();
        var prefixLengths = new Dictionary<(string TableName, string IndexName), List<int>>();

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

            var columnName = reader.GetString(2);

            if (context.Columns.TryGetValue((tableName, columnName), out var column))
            {
                index.Columns.Add(column);
            }

            var collation = reader.IsDBNull(4) ? null : reader.GetString(4);
            var indexType = reader.IsDBNull(6) ? null : reader.GetString(6);

            index.IsDescending.Add(string.Equals(collation, "D", StringComparison.OrdinalIgnoreCase));

            if (string.Equals(indexType, "SPATIAL", StringComparison.OrdinalIgnoreCase))
            {
                index.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
            }

            var subPart = reader.IsDBNull(7)
                ? (int?)null
                : Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture);
            var lengths = prefixLengths.TryGetValue(key, out var list) ? list : prefixLengths[key] = [];

            lengths.Add(subPart ?? 0);
        }

        foreach (var ((tableName, indexName), lengths) in prefixLengths)
        {
            if (lengths.Any(length => length > 0)
                && indexes.TryGetValue((tableName, indexName), out var index))
            {
                index.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, lengths.ToArray());
            }
        }
    }
}
