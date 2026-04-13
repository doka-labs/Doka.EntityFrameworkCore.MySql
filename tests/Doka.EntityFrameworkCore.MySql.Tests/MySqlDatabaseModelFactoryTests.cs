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
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingState());

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
        var factory = new MySqlDatabaseModelFactory(new StubDriverFacade(), new MySqlScaffoldingState());

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

    private sealed class StubDriverFacade : IMySqlDriverFacade
    {
        public string DriverName => "Stub";

        public DbConnection CreateConnection(
            string connectionString
        ) => throw new NotSupportedException("This test uses the DbConnection overload directly.");
    }

    private sealed class ScaffoldingDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        [AllowNull]
        public override string ConnectionString { get; set; } = "Server=localhost;Database=phase2;";

        public override string Database => "phase2";

        public override string DataSource => "localhost";

        public override string ServerVersion => "8.4.6";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

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
                var sql when sql.Contains("SELECT VERSION()", StringComparison.Ordinal) => "8.4.6",
                var sql when sql.Contains("SELECT DATABASE()", StringComparison.Ordinal)
                    && !sql.Contains("SCHEMATA", StringComparison.Ordinal) => "phase2",
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
                var sql when sql.Contains("FROM information_schema.TABLES", StringComparison.Ordinal) =>
                    CreateTablesReader(),
                var sql when sql.Contains("FROM information_schema.COLUMNS", StringComparison.Ordinal) =>
                    CreateColumnsReader(),
                var sql when sql.Contains("CONSTRAINT_NAME = 'PRIMARY'", StringComparison.Ordinal) =>
                    CreatePrimaryKeysReader(),
                var sql when sql.Contains("NON_UNIQUE = 0", StringComparison.Ordinal) =>
                    CreateUniqueConstraintsReader(),
                var sql when sql.Contains("FROM information_schema.ST_GEOMETRY_COLUMNS", StringComparison.Ordinal) =>
                    CreateSpatialGeometryColumnsReader(),
                var sql when sql.Contains("FROM information_schema.STATISTICS", StringComparison.Ordinal) =>
                    CreateIndexesReader(),
                var sql when sql.Contains(
                    "FROM information_schema.KEY_COLUMN_USAGE AS source",
                    StringComparison.Ordinal) => CreateForeignKeysReader(),
                _ => throw new InvalidOperationException($"Unexpected reader command: {CommandText}"),
            };
        }

        private static DataTableReader CreateTablesReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("TABLE_COLLATION", typeof(string));
            table.Columns.Add("TABLE_COMMENT", typeof(string));
            table.Columns.Add("ENGINE", typeof(string));
            table.Columns.Add("TABLE_TYPE", typeof(string));
            table.Rows.Add("mixed_index_table", "utf8mb4_0900_ai_ci", DBNull.Value, "InnoDB", "BASE TABLE");
            table.Rows.Add("spatial_feature_table", "utf8mb4_0900_ai_ci", DBNull.Value, "InnoDB", "BASE TABLE");

            return table.CreateDataReader();
        }

        private static DataTableReader CreateColumnsReader()
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

        private static DataTableReader CreatePrimaryKeysReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));

            return table.CreateDataReader();
        }

        private static DataTableReader CreateUniqueConstraintsReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("INDEX_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("SEQ_IN_INDEX", typeof(long));

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

            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "First",
                1L,
                "A",
                1L,
                "BTREE");
            table.Rows.Add(
                "mixed_index_table",
                "IX_Mixed",
                "Second",
                1L,
                "D",
                2L,
                "BTREE");
            table.Rows.Add(
                "spatial_feature_table",
                "IX_Spatial_Location",
                "Location",
                1L,
                "A",
                1L,
                "SPATIAL");

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

        private static DataTableReader CreateForeignKeysReader()
        {
            var table = new DataTable();
            table.Columns.Add("TABLE_NAME", typeof(string));
            table.Columns.Add("CONSTRAINT_NAME", typeof(string));
            table.Columns.Add("COLUMN_NAME", typeof(string));
            table.Columns.Add("ORDINAL_POSITION", typeof(long));
            table.Columns.Add("REFERENCED_TABLE_NAME", typeof(string));
            table.Columns.Add("REFERENCED_COLUMN_NAME", typeof(string));
            table.Columns.Add("DELETE_RULE", typeof(string));

            return table.CreateDataReader();
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
