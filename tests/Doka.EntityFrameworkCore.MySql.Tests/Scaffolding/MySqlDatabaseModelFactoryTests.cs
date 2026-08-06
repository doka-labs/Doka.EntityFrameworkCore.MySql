using Microsoft.EntityFrameworkCore.Scaffolding;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies focused reverse-engineering metadata loading behavior.
/// </summary>
public sealed class MySqlDatabaseModelFactoryTests
{
    /// <summary>
    /// Verifies that descending index metadata backfills preceding ascending columns correctly.
    /// </summary>
    [Fact]
    public void Reverse_engineering_backfills_preceding_ascending_columns_for_mixed_direction_indexes()
    {
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["mixed_index_table"], Array.Empty<string>()));
        var table = Assert.Single(databaseModel.Tables);
        var index = Assert.Single(table.Indexes);

        Assert.Collection(
            index.Columns,
            column => Assert.Equal("First", column.Name),
            column => Assert.Equal("Second", column.Name));
        Assert.NotNull(index.IsDescending);
        Assert.Equal(
            [
                false,
                true
            ],
            index.IsDescending);
    }

    /// <summary>
    /// Verifies that reverse engineering loads spatial-index metadata and MySQL SRID metadata.
    /// </summary>
    [Fact]
    public void Reverse_engineering_reads_spatial_index_and_srid_metadata()
    {
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["spatial_feature_table"], Array.Empty<string>()));
        var table = Assert.Single(databaseModel.Tables);
        var spatialColumn = Assert.Single(table.Columns, column => column.Name == "Location");
        var spatialIndex = Assert.Single(table.Indexes);

        Assert.Equal(
            4326,
            spatialColumn.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value);
        Assert.True(
            spatialIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)
                ?.Value as bool?);
    }

    /// <summary>
    /// Verifies that EF schema filters select the equivalent MySQL database,
    /// qualify the returned metadata, and restore the caller's active database.
    /// </summary>
    [Fact]
    public void Reverse_engineering_schema_filter_selects_and_qualifies_database()
    {
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["mixed_index_table"], ["tenant_database"]));
        var table = Assert.Single(databaseModel.Tables);

        Assert.Equal("tenant_database", table.Schema);
        Assert.Equal("phase2", connection.Database);
        Assert.Equal(
            [
                "tenant_database",
                "phase2"
            ],
            connection.DatabaseChanges);
    }

    /// <summary>
    /// Verifies that a foreign key spanning two selected MySQL databases resolves
    /// after both database-qualified table and column sets have been loaded.
    /// </summary>
    [Fact]
    public void Reverse_engineering_resolves_cross_database_foreign_key()
    {
        using var connection = new ScaffoldingDbConnection();
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(
                [],
                [
                    "principal_database",
                    "dependent_database"
                ]));
        var principalTable = Assert.Single(
            databaseModel.Tables,
            table => table.Schema == "principal_database");
        var dependentTable = Assert.Single(
            databaseModel.Tables,
            table => table.Schema == "dependent_database");
        var foreignKey = Assert.Single(dependentTable.ForeignKeys);

        Assert.Same(principalTable, foreignKey.PrincipalTable);
        Assert.Same(
            dependentTable.Columns.Single(column => column.Name == "PrincipalId"),
            Assert.Single(foreignKey.Columns));
        Assert.Same(
            principalTable.Columns.Single(column => column.Name == "Id"),
            Assert.Single(foreignKey.PrincipalColumns));
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDelete);
        Assert.Equal("phase2", connection.Database);
        Assert.Equal(
            [
                "principal_database",
                "dependent_database",
                "principal_database",
                "dependent_database",
                "phase2"
            ],
            connection.DatabaseChanges);
    }

    /// <summary>
    /// Verifies that MariaDB's authoritative SYSTEM VERSIONED table type and
    /// system-time period flags reconstruct the provider temporal contract.
    /// </summary>
    [Fact]
    public void Reverse_engineering_recognizes_native_mariadb_temporal_table()
    {
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.NativeMariaDbTemporal);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions(["audit_entries"], Array.Empty<string>()));
        var table = Assert.Single(databaseModel.Tables);

        Assert.Equal("audit_entries", table.Name);
        Assert.True(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value as bool?);
        Assert.Equal(
            "ValidFrom",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)?.Value);
    }

    /// <summary>
    /// Verifies that the complete provider-owned MySQL trigger and storage
    /// contract reconstructs one temporal table and suppresses its history table.
    /// </summary>
    [Fact]
    public void Reverse_engineering_recognizes_complete_mysql_temporal_emulation()
    {
        using var connection = new ScaffoldingDbConnection(ScaffoldingScenario.MySqlTemporalEmulation);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], Array.Empty<string>()));
        var table = Assert.Single(databaseModel.Tables);

        Assert.Equal("audit_entries", table.Name);
        Assert.True(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value as bool?);
        Assert.Equal(
            "audit_entries_history",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)?.Value);
        Assert.Equal(
            "ValidFrom",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)?.Value);
        Assert.Equal(
            "ValidTo",
            table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)?.Value);
    }

    /// <summary>
    /// Verifies that an incomplete provider trigger set is not guessed to be
    /// temporal and therefore keeps both current and history tables visible.
    /// </summary>
    [Fact]
    public void Reverse_engineering_does_not_recognize_incomplete_mysql_temporal_emulation()
    {
        using var connection = new ScaffoldingDbConnection(
            ScaffoldingScenario.IncompleteMySqlTemporalEmulation);
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingContext());

        var databaseModel = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], Array.Empty<string>()));
        var tables = databaseModel.Tables.OrderBy(table => table.Name).ToArray();

        Assert.Collection(
            tables,
            table =>
            {
                Assert.Equal("audit_entries", table.Name);
                Assert.Null(table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal));
            },
            table => Assert.Equal("audit_entries_history", table.Name));
    }

    private enum ScaffoldingScenario
    {
        Default,
        NativeMariaDbTemporal,
        MySqlTemporalEmulation,
        IncompleteMySqlTemporalEmulation,
    }

    private sealed class StubDriverFacade : IMySqlDriverFacade
    {
        public string DriverName => "Stub";

        public DbConnection CreateConnection(
            string connectionString
        ) => throw new NotSupportedException("This test uses the DbConnection overload directly.");
    }

    private sealed class ScaffoldingDbConnection : DbConnection
    {
        private string _database = "phase2";
        private ConnectionState _state = ConnectionState.Closed;

        public ScaffoldingDbConnection(
            ScaffoldingScenario scenario = ScaffoldingScenario.Default
        )
        {
            Scenario = scenario;
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = "Server=localhost;Database=phase2;";

        public override string Database => _database;

        public List<string> DatabaseChanges { get; } = [];

        public ScaffoldingScenario Scenario { get; }

        public override string DataSource => "localhost";

        public override string ServerVersion => Scenario == ScaffoldingScenario.NativeMariaDbTemporal
            ? "11.4.7-MariaDB"
            : "8.4.6";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        )
        {
            _database = databaseName;
            DatabaseChanges.Add(databaseName);
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new ScaffoldingDbCommand(this);
    }

    private sealed class ScaffoldingDbCommand : DbCommand
    {
        private readonly ScaffoldingDbConnection _connection;
        private readonly ScaffoldingDbParameterCollection _parameters = new();

        public ScaffoldingDbCommand(
            ScaffoldingDbConnection connection
        )
        {
            _connection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object ExecuteScalar()
        {
            return CommandText switch
            {
                var sql when sql.Contains("SELECT VERSION()", StringComparison.Ordinal) =>
                    _connection.ServerVersion,
                var sql when sql.Contains("SELECT DATABASE()", StringComparison.Ordinal)
                    && !sql.Contains("SCHEMATA", StringComparison.Ordinal) => _connection.Database,
                var sql when sql.Contains("FROM information_schema.SCHEMATA", StringComparison.Ordinal) =>
                    "utf8mb4_0900_ai_ci",
                _ => throw new InvalidOperationException($"Unexpected scalar command: {CommandText}"),
            };
        }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new ScaffoldingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
        {
            return CommandText switch
            {
                var sql when sql.Contains("IS_SYSTEM_TIME_PERIOD_START", StringComparison.Ordinal) =>
                    CreateNativeTemporalPeriodColumnsReader(),
                var sql when sql.Contains("FROM information_schema.TRIGGERS", StringComparison.Ordinal) =>
                    CreateTemporalTriggersReader(_connection.Scenario),
                var sql when sql.Contains("TABLE_SCHEMA,", StringComparison.Ordinal)
                    && sql.Contains("FROM information_schema.TABLES", StringComparison.Ordinal) =>
                    CreatePhysicalTemporalTablesReader(_connection.Database),
                var sql when sql.Contains("TABLE_SCHEMA,", StringComparison.Ordinal)
                    && sql.Contains("FROM information_schema.COLUMNS", StringComparison.Ordinal) =>
                    CreatePhysicalTemporalColumnsReader(_connection.Database),
                var sql when sql.Contains("FROM information_schema.CHECK_CONSTRAINTS", StringComparison.Ordinal)
                    && sql.Contains("JSON_VALID", StringComparison.OrdinalIgnoreCase) =>
                    CreateJsonCheckConstraintsReader(),
                var sql when sql.Contains("FROM information_schema.CHECK_CONSTRAINTS", StringComparison.Ordinal) =>
                    CreateCheckConstraintsReader(),
                var sql when sql.Contains("TABLE_TYPE = 'SEQUENCE'", StringComparison.Ordinal) =>
                    CreateNativeSequenceNamesReader(),
                var sql when sql.Contains("FROM information_schema.TABLES", StringComparison.Ordinal) =>
                    CreateTablesReader(_connection.Database, _connection.Scenario),
                var sql when sql.Contains("FROM information_schema.COLUMNS", StringComparison.Ordinal) =>
                    CreateColumnsReader(_connection.Database, _connection.Scenario),
                var sql when sql.Contains("CONSTRAINT_NAME = 'PRIMARY'", StringComparison.Ordinal) =>
                    CreatePrimaryKeysReader(_connection.Database),
                var sql when sql.Contains("CONSTRAINT_TYPE = 'UNIQUE'", StringComparison.Ordinal) =>
                    CreateUniqueConstraintsReader(),
                var sql when sql.Contains("CONSTRAINT_TYPE = 'CHECK'", StringComparison.Ordinal) =>
                    CreateCheckConstraintsReader(),
                var sql when sql.Contains("FROM information_schema.ST_GEOMETRY_COLUMNS", StringComparison.Ordinal) =>
                    CreateSpatialGeometryColumnsReader(),
                var sql when sql.Contains("FROM information_schema.STATISTICS", StringComparison.Ordinal) =>
                    CreateIndexesReader(),
                var sql when sql.Contains("FROM information_schema.PERIODS AS periods", StringComparison.Ordinal) =>
                    CreateApplicationTimePeriodsReader(sql),
                var sql when sql.Contains(
                    "FROM information_schema.KEY_PERIOD_USAGE AS period_keys",
                    StringComparison.Ordinal) => CreateApplicationTimeConstraintsReader(),
                var sql when sql.Contains(
                    "FROM information_schema.KEY_COLUMN_USAGE AS source",
                    StringComparison.Ordinal) => CreateForeignKeysReader(_connection.Database),
                _ => throw new InvalidOperationException($"Unexpected reader command: {CommandText}"),
            };
        }

        private static DataTableReader CreateTablesReader(
            string databaseName,
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("TABLE_COLLATION", typeof(string));
            table.Columns.Add("TABLE_COMMENT", typeof(string));
            table.Columns.Add("ENGINE", typeof(string));
            table.Columns.Add("TABLE_TYPE", typeof(string));

            if (scenario == ScaffoldingScenario.NativeMariaDbTemporal)
            {
                table.Rows.Add(
                    "audit_entries",
                    "utf8mb4_general_ci",
                    DBNull.Value,
                    "InnoDB",
                    "SYSTEM VERSIONED");
            }
            else if (scenario is ScaffoldingScenario.MySqlTemporalEmulation
                     or ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                table.Rows.Add(
                    "audit_entries",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
                table.Rows.Add(
                    "audit_entries_history",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else if (databaseName == "principal_database")
            {
                table.Rows.Add(
                    "principal_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else if (databaseName == "dependent_database")
            {
                table.Rows.Add(
                    "dependent_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }
            else
            {
                table.Rows.Add(
                    "mixed_index_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
                table.Rows.Add(
                    "spatial_feature_table",
                    "utf8mb4_0900_ai_ci",
                    DBNull.Value,
                    "InnoDB",
                    "BASE TABLE");
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateNativeTemporalPeriodColumnsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("IS_SYSTEM_TIME_PERIOD_START", typeof(string));
            table.Columns.Add("IS_SYSTEM_TIME_PERIOD_END", typeof(string));
            table.Rows.Add("audit_entries", "ValidFrom", "YES", "NO");
            table.Rows.Add("audit_entries", "ValidTo", "NO", "YES");

            return table.CreateDataReader();
        }

        private static DataTableReader CreateTemporalTriggersReader(
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TRIGGER_NAME", typeof(string));
            table.Columns.Add("EVENT_MANIPULATION", typeof(string));
            table.Columns.Add("EVENT_OBJECT_TABLE", typeof(string));
            table.Columns.Add("ACTION_TIMING", typeof(string));
            table.Columns.Add("ACTION_STATEMENT", typeof(string));

            if (scenario is not ScaffoldingScenario.MySqlTemporalEmulation
                and not ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                return table.CreateDataReader();
            }

            var marker = MySqlTemporalMetadata.CreateEmulationMarker(
                null,
                "audit_entries_history",
                "ValidFrom",
                "ValidTo");
            var insertBody =
                $"BEGIN /* {marker} */ "
                + "SET NEW.`ValidFrom` = UTC_TIMESTAMP(6); "
                + "SET NEW.`ValidTo` = '9999-12-31 23:59:59.999999'; END";
            var updateBody =
                $"BEGIN /* {marker} */ "
                + "DECLARE __doka_temporal_timestamp datetime(6); "
                + "SET __doka_temporal_timestamp = UTC_TIMESTAMP(6); "
                + "INSERT INTO `audit_entries_history` "
                + "(`Id`, `Name`, `ValidFrom`, `ValidTo`) "
                + "VALUES (OLD.`Id`, OLD.`Name`, OLD.`ValidFrom`, __doka_temporal_timestamp); "
                + "SET NEW.`ValidFrom` = __doka_temporal_timestamp; "
                + "SET NEW.`ValidTo` = '9999-12-31 23:59:59.999999'; END";

            table.Rows.Add(
                MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "insert"),
                "INSERT",
                "audit_entries",
                "BEFORE",
                insertBody);
            table.Rows.Add(
                MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "update"),
                "UPDATE",
                "audit_entries",
                "BEFORE",
                updateBody);

            if (scenario == ScaffoldingScenario.MySqlTemporalEmulation)
            {
                var deleteBody =
                    $"BEGIN /* {marker} */ "
                    + "DECLARE __doka_temporal_timestamp datetime(6); "
                    + "SET __doka_temporal_timestamp = UTC_TIMESTAMP(6); "
                    + "INSERT INTO `audit_entries_history` "
                    + "(`Id`, `Name`, `ValidFrom`, `ValidTo`) "
                    + "VALUES (OLD.`Id`, OLD.`Name`, OLD.`ValidFrom`, __doka_temporal_timestamp); END";

                table.Rows.Add(
                    MySqlTemporalMetadata.CreateTriggerName(null, "audit_entries", "delete"),
                    "DELETE",
                    "audit_entries",
                    "BEFORE",
                    deleteBody);
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreatePhysicalTemporalTablesReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("ENGINE", typeof(string));
            table.Columns.Add("TABLE_TYPE", typeof(string));
            table.Rows.Add(databaseName, "audit_entries", "InnoDB", "BASE TABLE");
            table.Rows.Add(databaseName, "audit_entries_history", "InnoDB", "BASE TABLE");

            return table.CreateDataReader();
        }

        private static DataTableReader CreatePhysicalTemporalColumnsReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("COLUMN_TYPE", typeof(string));
            table.Columns.Add("IS_NULLABLE", typeof(string));
            table.Columns.Add("EXTRA", typeof(string));
            table.Columns.Add("GENERATION_EXPRESSION", typeof(string));

            AddPhysicalTemporalColumns(table, databaseName, "audit_entries");
            AddPhysicalTemporalColumns(table, databaseName, "audit_entries_history");

            return table.CreateDataReader();
        }

        private static DataTableReader CreateJsonCheckConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CHECK_CLAUSE", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateNativeSequenceNamesReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateColumnsReader(
            string databaseName,
            ScaffoldingScenario scenario
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("IS_NULLABLE", typeof(string));
            table.Columns.Add("COLUMN_TYPE", typeof(string));
            table.Columns.Add("DATA_TYPE", typeof(string));
            table.Columns.Add("COLUMN_DEFAULT", typeof(string));
            table.Columns.Add("EXTRA", typeof(string));
            table.Columns.Add("GENERATION_EXPRESSION", typeof(string));
            table.Columns.Add("COLUMN_COMMENT", typeof(string));
            table.Columns.Add("COLLATION_NAME", typeof(string));

            if (scenario is ScaffoldingScenario.NativeMariaDbTemporal)
            {
                AddTemporalColumns(table, "audit_entries", "timestamp(6)", "timestamp");

                return table.CreateDataReader();
            }

            if (scenario is ScaffoldingScenario.MySqlTemporalEmulation
                or ScaffoldingScenario.IncompleteMySqlTemporalEmulation)
            {
                AddTemporalColumns(table, "audit_entries", "datetime(6)", "datetime");
                AddTemporalColumns(table, "audit_entries_history", "datetime(6)", "datetime");

                return table.CreateDataReader();
            }

            if (databaseName == "principal_database")
            {
                AddColumn(table, "principal_table", "Id");

                return table.CreateDataReader();
            }

            if (databaseName == "dependent_database")
            {
                AddColumn(table, "dependent_table", "Id");
                AddColumn(table, "dependent_table", "PrincipalId");

                return table.CreateDataReader();
            }

            table.Rows.Add(
                "mixed_index_table",
                "First",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "mixed_index_table",
                "Second",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "spatial_feature_table",
                "Id",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "spatial_feature_table",
                "Location",
                "NO",
                "point",
                "point",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);

            return table.CreateDataReader();
        }

        private static DataTableReader CreatePrimaryKeysReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));

            if (databaseName == "principal_database")
            {
                table.Rows.Add("principal_table", "Id", "PRIMARY", 1L);
            }
            else if (databaseName == "dependent_database")
            {
                table.Rows.Add("dependent_table", "Id", "PRIMARY", 1L);
            }

            return table.CreateDataReader();
        }

        private static DataTableReader CreateUniqueConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateCheckConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("CHECK_CLAUSE", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateIndexesReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("INDEX_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("NON_UNIQUE", typeof(long));
            table.Columns.Add("COLLATION", typeof(string));
            table.Columns.Add("SEQ_IN_INDEX", typeof(long));
            table.Columns.Add("INDEX_TYPE", typeof(string));
            table.Columns.Add("SUB_PART", typeof(long));
            table.Columns.Add("EXPRESSION", typeof(string));

            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "First",
                1L,
                "A",
                1L,
                "BTREE",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "Second",
                1L,
                "D",
                2L,
                "BTREE",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                "spatial_feature_table",
                "IX_Spatial_Location",
                "Location",
                1L,
                "A",
                1L,
                "SPATIAL",
                DBNull.Value,
                DBNull.Value);

            return table.CreateDataReader();
        }

        private static DataTableReader CreateApplicationTimePeriodsReader(
            string commandText
        )
        {
            // PERIODS also contains MariaDB's native SYSTEM_TIME dimension. The
            // application-time loader must leave that row to TemporalTableLoader.
            Assert.Contains(
                "periods.PERIOD <> 'SYSTEM_TIME'",
                commandText,
                StringComparison.Ordinal);

            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("PERIOD", typeof(string));
            table.Columns.Add("START_COLUMN_NAME", typeof(string));
            table.Columns.Add("END_COLUMN_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateApplicationTimeConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("PERIOD_NAME", typeof(string));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateSpatialGeometryColumnsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("SRS_ID", typeof(int));

            table.Rows.Add("spatial_feature_table", "Location", 4326);

            return table.CreateDataReader();
        }

        private static DataTableReader CreateForeignKeysReader(
            string databaseName
        )
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_SCHEMA", typeof(string));
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));
            table.Columns.Add("REFERENCED_TABLE_SCHEMA", typeof(string));
            table.Columns.Add("REFERENCED_TABLE_NAME", typeof(string));
            table.Columns.Add("REFERENCED_COLUMN_NAME", typeof(string));
            table.Columns.Add("DELETE_RULE", typeof(string));

            if (databaseName == "dependent_database")
            {
                table.Rows.Add(
                    "dependent_database",
                    "dependent_table",
                    "FK_Dependent_Principal",
                    "PrincipalId",
                    1L,
                    "principal_database",
                    "principal_table",
                    "Id",
                    "CASCADE");
            }

            return table.CreateDataReader();
        }

        private static void AddColumn(
            DataTable table,
            string tableName,
            string columnName
        )
        {
            table.Rows.Add(
                tableName,
                columnName,
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
        }

        private static void AddTemporalColumns(
            DataTable table,
            string tableName,
            string periodColumnType,
            string periodDataType
        )
        {
            table.Rows.Add(
                tableName,
                "Id",
                "NO",
                "int",
                "int",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                tableName,
                "Name",
                "NO",
                "varchar(64)",
                "varchar",
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                "utf8mb4_0900_ai_ci");
            table.Rows.Add(
                tableName,
                "ValidFrom",
                "NO",
                periodColumnType,
                periodDataType,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                tableName,
                "ValidTo",
                "NO",
                periodColumnType,
                periodDataType,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value);
        }

        private static void AddPhysicalTemporalColumns(
            DataTable table,
            string databaseName,
            string tableName
        )
        {
            table.Rows.Add(databaseName, tableName, "Id", "int", "NO", DBNull.Value, DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "Name",
                "varchar(64)",
                "NO",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "ValidFrom",
                "datetime(6)",
                "NO",
                DBNull.Value,
                DBNull.Value);
            table.Rows.Add(
                databaseName,
                tableName,
                "ValidTo",
                "datetime(6)",
                "NO",
                DBNull.Value,
                DBNull.Value);
        }
    }

    private sealed class ScaffoldingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class ScaffoldingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Add((DbParameter)value);

            return _parameters.Count - 1;
        }

        public override void AddRange(
            Array values
        )
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            return _parameters.Contains((DbParameter)value);
        }

        public override bool Contains(
            string value
        ) => _parameters.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(
            Array array,
            int index
        ) => ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            return _parameters.IndexOf((DbParameter)value);
        }

        public override int IndexOf(
            string parameterName
        ) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(
            int index,
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Insert(index, (DbParameter)value);
        }

        public override void Remove(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Remove((DbParameter)value);
        }

        public override void RemoveAt(
            int index
        ) => _parameters.RemoveAt(index);

        public override void RemoveAt(
            string parameterName
        )
        {
            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(
            int index
        ) => _parameters[index];

        protected override DbParameter GetParameter(
            string parameterName
        )
        {
            var index = IndexOf(parameterName);

            return index >= 0
                ? _parameters[index]
                : throw new ArgumentException($"Parameter '{parameterName}' was not found.", nameof(parameterName));
        }

        protected override void SetParameter(
            int index,
            DbParameter value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters[index] = value;
        }

        protected override void SetParameter(
            string parameterName,
            DbParameter value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                _parameters[index] = value;
                return;
            }

            _parameters.Add(value);
        }
    }
}
