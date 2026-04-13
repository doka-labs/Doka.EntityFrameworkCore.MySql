namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlDatabaseModelFactory : IDatabaseModelFactory
{
    private const string DefaultMariaDbJsonCollation = "utf8mb4_bin";

    private readonly IMySqlDriverFacade _driverFacade;
    private readonly MySqlScaffoldingState _scaffoldingState;
    private readonly ILogger? _logger;

    public MySqlDatabaseModelFactory(
        IMySqlDriverFacade driverFacade,
        MySqlScaffoldingState scaffoldingState,
        ILoggerFactory? loggerFactory = null
    )
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
        _scaffoldingState = scaffoldingState ?? throw new ArgumentNullException(nameof(scaffoldingState));
        _logger = loggerFactory?.CreateLogger(MySqlLoggerCategory.Scaffolding);
    }

    public DatabaseModel Create(
        string connectionString,
        DatabaseModelFactoryOptions options
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        using var connection = _driverFacade.CreateConnection(connectionString);

        return Create(connection, options);
    }

    public DatabaseModel Create(
        DbConnection connection,
        DatabaseModelFactoryOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Schemas.Any())
        {
            throw new InvalidOperationException(
                "MySQL-family reverse engineering does not support schema filtering because schemas are unsupported.");
        }

        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            connection.Open();
        }

        try
        {
            _scaffoldingState.Reset();

            var rawServerVersion = ExecuteScalarString(connection, "SELECT VERSION();");
            var serverVersion = MySqlServerVersion.AutoDetect(rawServerVersion);

            _scaffoldingState.SetDetectedServerVersionText(rawServerVersion);

            var databaseName = ExecuteScalarString(connection, "SELECT DATABASE();");
            var databaseCollation = ExecuteScalarString(
                connection,
                """
                SELECT DEFAULT_COLLATION_NAME
                FROM information_schema.SCHEMATA
                WHERE SCHEMA_NAME = DATABASE();
                """);

            var databaseModel = new DatabaseModel
            {
                DatabaseName = databaseName,
                Collation = databaseCollation,
            };

            var databaseCharSet = DeriveCharSetFromCollation(databaseCollation);

            if (!string.IsNullOrWhiteSpace(databaseCharSet))
            {
                databaseModel.SetAnnotation(MySqlAnnotationNames.CharSet, databaseCharSet);
            }

            var tableFilter = CreateTableFilter(options.Tables);
            var mariaDbJsonColumns = serverVersion.IsMariaDb
                ? LoadMariaDbJsonCheckConstraints(connection)
                : new HashSet<(string, string)>();

            var tables = LoadTables(connection, databaseModel, tableFilter);
            var tableLookup = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
            var columns = LoadColumns(connection, databaseName, tableFilter, tableLookup, mariaDbJsonColumns);

            LoadPrimaryKeys(connection, tableFilter, tableLookup, columns);
            LoadUniqueConstraints(connection, tableFilter, tableLookup, columns);
            LoadIndexes(connection, tableFilter, tableLookup, columns);
            LoadSpatialColumnMetadata(connection, tableFilter, columns, serverVersion.Capabilities);
            LoadForeignKeys(connection, tableFilter, tableLookup, columns, _logger);

            return databaseModel;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                connection.Close();
            }
        }
    }

    private static List<DatabaseTable> LoadTables(
        DbConnection connection,
        DatabaseModel databaseModel,
        TableFilter tableFilter
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  TABLE_COLLATION,
                                  TABLE_COMMENT,
                                  ENGINE,
                                  TABLE_TYPE
                              FROM information_schema.TABLES
                              WHERE TABLE_SCHEMA = DATABASE()
                                AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                              ORDER BY TABLE_NAME;
                              """;

        using var reader = command.ExecuteReader();

        var tables = new List<DatabaseTable>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName))
            {
                continue;
            }

            var tableType = reader.IsDBNull(4) ? "BASE TABLE" : reader.GetString(4);
            var isView = string.Equals(tableType, "VIEW", StringComparison.OrdinalIgnoreCase);

            var table = isView
                ? new DatabaseView
                {
                    Database = databaseModel,
                    Name = tableName,
                    Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                }
                : new DatabaseTable
                {
                    Database = databaseModel,
                    Name = tableName,
                    Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
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

                var charSet = DeriveCharSetFromCollation(tableCollation);

                if (!string.IsNullOrWhiteSpace(charSet))
                {
                    table.SetAnnotation(MySqlAnnotationNames.CharSet, charSet);
                }
            }

            databaseModel.Tables.Add(table);
            tables.Add(table);
        }

        return tables;
    }

    private static Dictionary<(string TableName, string ColumnName), DatabaseColumn> LoadColumns(
        DbConnection connection,
        string databaseName,
        TableFilter tableFilter,
        Dictionary<string, DatabaseTable> tableLookup,
        HashSet<(string TableName, string ColumnName)> mariaDbJsonColumns
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
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
                              ORDER BY TABLE_NAME, ORDINAL_POSITION;
                              """;

        using var reader = command.ExecuteReader();

        var columns = new Dictionary<(string TableName, string ColumnName), DatabaseColumn>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName))
            {
                continue;
            }

            var columnName = reader.GetString(1);

            if (!tableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var storeType = reader.GetString(3);
            var dataType = reader.GetString(4);
            var extra = reader.IsDBNull(6) ? null : reader.GetString(6);
            var computedColumnSql = reader.IsDBNull(7) ? null : reader.GetString(7);
            var collation = reader.IsDBNull(9) ? null : reader.GetString(9);

            var column = new DatabaseColumn
            {
                Table = table,
                Name = columnName,
                StoreType = NormalizeStoreType(
                    dataType,
                    storeType,
                    table.Name,
                    columnName,
                    collation,
                    mariaDbJsonColumns),
                IsNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                DefaultValueSql = reader.IsDBNull(5) ? null : reader.GetString(5),
                ComputedColumnSql = string.IsNullOrWhiteSpace(computedColumnSql) ? null : computedColumnSql,
                IsStored = ResolveIsStored(extra),
                Comment = reader.IsDBNull(8) ? null : reader.GetString(8),
                Collation = collation,
                ValueGenerated = ResolveValueGenerated(extra),
            };

            table.Columns.Add(column);
            columns[(tableName, columnName)] = column;
        }

        return columns;
    }

    private static void LoadPrimaryKeys(
        DbConnection connection,
        TableFilter tableFilter,
        Dictionary<string, DatabaseTable> tableLookup,
        Dictionary<(string TableName, string ColumnName), DatabaseColumn> columns
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  COLUMN_NAME,
                                  CONSTRAINT_NAME,
                                  ORDINAL_POSITION
                              FROM information_schema.KEY_COLUMN_USAGE
                              WHERE TABLE_SCHEMA = DATABASE()
                                AND CONSTRAINT_NAME = 'PRIMARY'
                              ORDER BY TABLE_NAME, ORDINAL_POSITION;
                              """;

        using var reader = command.ExecuteReader();

        var primaryKeys = new Dictionary<string, DatabasePrimaryKey>(StringComparer.Ordinal);

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName)
                || !tableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            if (!primaryKeys.TryGetValue(tableName, out var primaryKey))
            {
                primaryKey = new DatabasePrimaryKey
                {
                    Table = table,
                    Name = reader.GetString(2),
                };

                table.PrimaryKey = primaryKey;
                primaryKeys[tableName] = primaryKey;
            }

            var columnName = reader.GetString(1);

            if (columns.TryGetValue((tableName, columnName), out var column))
            {
                primaryKey.Columns.Add(column);
            }
        }
    }

    private static void LoadUniqueConstraints(
        DbConnection connection,
        TableFilter tableFilter,
        Dictionary<string, DatabaseTable> tableLookup,
        Dictionary<(string TableName, string ColumnName), DatabaseColumn> columns
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  INDEX_NAME,
                                  COLUMN_NAME,
                                  SEQ_IN_INDEX
                              FROM information_schema.STATISTICS
                              WHERE TABLE_SCHEMA = DATABASE()
                                AND NON_UNIQUE = 0
                                AND INDEX_NAME <> 'PRIMARY'
                              ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;
                              """;

        using var reader = command.ExecuteReader();

        var constraints = new Dictionary<(string TableName, string ConstraintName), DatabaseUniqueConstraint>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName)
                || !tableLookup.TryGetValue(tableName, out var table))
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

            if (columns.TryGetValue((tableName, columnName), out var column))
            {
                uniqueConstraint.Columns.Add(column);
            }
        }
    }

    private static void LoadIndexes(
        DbConnection connection,
        TableFilter tableFilter,
        Dictionary<string, DatabaseTable> tableLookup,
        Dictionary<(string TableName, string ColumnName), DatabaseColumn> columns
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  INDEX_NAME,
                                  COLUMN_NAME,
                                  NON_UNIQUE,
                                  COLLATION,
                                  SEQ_IN_INDEX,
                                  INDEX_TYPE
                              FROM information_schema.STATISTICS
                              WHERE TABLE_SCHEMA = DATABASE()
                                AND INDEX_NAME <> 'PRIMARY'
                              ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;
                              """;

        using var reader = command.ExecuteReader();

        var indexes = new Dictionary<(string TableName, string IndexName), DatabaseIndex>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName)
                || !tableLookup.TryGetValue(tableName, out var table))
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

            if (columns.TryGetValue((tableName, columnName), out var column))
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
        }
    }

    private static void LoadSpatialColumnMetadata(
        DbConnection connection,
        TableFilter tableFilter,
        Dictionary<(string TableName, string ColumnName), DatabaseColumn> columns,
        ServerCapabilities capabilities
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!capabilities.SupportsSpatialColumnSridAttribute)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  COLUMN_NAME,
                                  SRS_ID
                              FROM information_schema.ST_GEOMETRY_COLUMNS
                              WHERE TABLE_SCHEMA = DATABASE()
                              ORDER BY TABLE_NAME, COLUMN_NAME;
                              """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName)
                || reader.IsDBNull(2))
            {
                continue;
            }

            var columnName = reader.GetString(1);

            if (columns.TryGetValue((tableName, columnName), out var column))
            {
                column.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, reader.GetInt32(2));
            }
        }
    }

    private static void LoadForeignKeys(
        DbConnection connection,
        TableFilter tableFilter,
        Dictionary<string, DatabaseTable> tableLookup,
        Dictionary<(string TableName, string ColumnName), DatabaseColumn> columns,
        ILogger? logger
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
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
                              ORDER BY source.TABLE_NAME, source.CONSTRAINT_NAME, source.ORDINAL_POSITION;
                              """;

        using var reader = command.ExecuteReader();

        var foreignKeys = new Dictionary<(string TableName, string ForeignKeyName), DatabaseForeignKey>();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);

            if (!tableFilter.Matches(tableName)
                || !tableLookup.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var foreignKeyName = reader.GetString(1);
            var key = (tableName, foreignKeyName);

            if (!foreignKeys.TryGetValue(key, out var foreignKey))
            {
                var principalTableName = reader.GetString(4);

                if (!tableLookup.TryGetValue(principalTableName, out var principalTable))
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
                    OnDelete = ResolveReferentialAction(reader.GetString(6)),
                };

                table.ForeignKeys.Add(foreignKey);
                foreignKeys[key] = foreignKey;
            }

            var columnName = reader.GetString(2);
            var principalColumnName = reader.GetString(5);

            if (columns.TryGetValue((tableName, columnName), out var column))
            {
                foreignKey.Columns.Add(column);
            }

            if (columns.TryGetValue((foreignKey.PrincipalTable.Name, principalColumnName), out var principalColumn))
            {
                foreignKey.PrincipalColumns.Add(principalColumn);
            }
        }
    }

    private static string ExecuteScalarString(
        DbConnection connection,
        string commandText
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;

        var result = command.ExecuteScalar();

        return result switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            _ => Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static ValueGenerated? ResolveValueGenerated(
        string? extra
    )
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return null;
        }

        if (extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase))
        {
            return ValueGenerated.OnAdd;
        }

        return null;
    }

    private static bool? ResolveIsStored(
        string? extra
    )
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return null;
        }

        if (extra.Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extra.Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static ReferentialAction? ResolveReferentialAction(
        string? deleteRule
    ) => deleteRule?.ToUpperInvariant() switch
    {
        "CASCADE" => ReferentialAction.Cascade,
        "SET NULL" => ReferentialAction.SetNull,
        "SET DEFAULT" => ReferentialAction.SetDefault,
        "RESTRICT" => ReferentialAction.Restrict,
        "NO ACTION" => ReferentialAction.NoAction,
        _ => null,
    };

    private static string NormalizeStoreType(
        string dataType,
        string storeType,
        string tableName,
        string columnName,
        string? collation,
        HashSet<(string TableName, string ColumnName)> mariaDbJsonColumns
    )
    {
        if (!string.Equals(dataType, "longtext", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(collation, DefaultMariaDbJsonCollation, StringComparison.OrdinalIgnoreCase))
        {
            return storeType;
        }

        // MariaDB exposes JSON columns as LONGTEXT with a binary JSON_VALID check constraint.
        return mariaDbJsonColumns.Contains((tableName, columnName)) ? "json" : storeType;
    }

    private static HashSet<(string TableName, string ColumnName)> LoadMariaDbJsonCheckConstraints(
        DbConnection connection
    )
    {
        // Use case-insensitive column name comparison because MariaDB may store
        // CHECK_CLAUSE column references in a different case than the declared column name.
        var result = new HashSet<(string TableName, string ColumnName)>(CaseInsensitiveTupleComparer.Instance);

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  TABLE_NAME,
                                  CHECK_CLAUSE
                              FROM information_schema.CHECK_CONSTRAINTS
                              WHERE CONSTRAINT_SCHEMA = DATABASE()
                                AND LOWER(CHECK_CLAUSE) LIKE '%json_valid%';
                              """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var checkClause = reader.GetString(1);

            var columnName = ExtractJsonValidColumnName(checkClause);

            if (columnName is not null)
            {
                result.Add((tableName, columnName));
            }
        }

        return result;
    }

    private static string? ExtractJsonValidColumnName(
        string checkClause
    )
    {
        // Matches patterns like: json_valid(`column_name`) or json_valid(column_name)
        const string prefix = "json_valid(";
        var lowerClause = checkClause.ToLowerInvariant();
        var startIndex = lowerClause.IndexOf(prefix, StringComparison.Ordinal);

        if (startIndex < 0)
        {
            return null;
        }

        startIndex += prefix.Length;
        var endIndex = lowerClause.IndexOf(')', startIndex);

        if (endIndex <= startIndex)
        {
            return null;
        }

        var columnRef = checkClause[startIndex..endIndex]
            .Trim();

        // Strip backtick delimiters if present.
        if (columnRef.Length >= 2
            && columnRef[0] == '`'
            && columnRef[^1] == '`')
        {
            columnRef = columnRef[1..^1];
        }

        return string.IsNullOrWhiteSpace(columnRef) ? null : columnRef;
    }

    private static string? DeriveCharSetFromCollation(
        string? collation
    )
    {
        if (string.IsNullOrWhiteSpace(collation))
        {
            return null;
        }

        var separatorIndex = collation.IndexOf('_');

        return separatorIndex > 0 ? collation[..separatorIndex] : null;
    }

    private static TableFilter CreateTableFilter(
        IEnumerable<string> tables
    )
    {
        var set = new HashSet<string>(tables, StringComparer.Ordinal);

        return set.Count == 0 ? TableFilter.MatchAll : new TableFilter(set);
    }

    private readonly record struct TableFilter(HashSet<string>? Tables)
    {
        public static TableFilter MatchAll => new(null);

        public bool Matches(
            string tableName
        ) => Tables is null || Tables.Contains(tableName);
    }

    /// <summary>
    /// Equality comparer for (TableName, ColumnName) tuples with case-insensitive column name comparison.
    /// Table names in MySQL are case-sensitive on Linux but column names in CHECK_CLAUSE
    /// may not match the declared casing.
    /// </summary>
    private sealed class CaseInsensitiveTupleComparer : IEqualityComparer<(string TableName, string ColumnName)>
    {
        public static readonly CaseInsensitiveTupleComparer Instance = new();

        public bool Equals(
            (string TableName, string ColumnName) x,
            (string TableName, string ColumnName) y
        )
        {
            return string.Equals(x.TableName, y.TableName, StringComparison.Ordinal)
                && string.Equals(x.ColumnName, y.ColumnName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(
            (string TableName, string ColumnName) obj
        )
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.TableName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ColumnName));
        }
    }
}
