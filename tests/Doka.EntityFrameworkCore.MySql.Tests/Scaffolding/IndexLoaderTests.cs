using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace Doka.EntityFrameworkCore.MySql.Tests.Scaffolding;

/// <summary>
/// Pins the SUB_PART -> IndexPrefixLength annotation translation of the IndexLoader.
/// The previous monolith dropped SUB_PART silently; this test fixes the per-column
/// prefix-length array as the canonical wire shape so a future migration generator
/// can emit <c>KEY ix (col(N))</c> faithfully. Indexes without any non-null SUB_PART
/// must NOT carry the annotation (avoids dead-knob array of zeros).
/// </summary>
public sealed class IndexLoaderTests
{
    [Fact]
    public void Index_with_sub_part_annotates_IndexPrefixLength_array()
    {
        var context = BuildContext(
            ("prefixed_index_table", "IX_Prefixed", "Name", 1L, "A", 1L, "BTREE", 64L),
            ("prefixed_index_table", "IX_Prefixed", "Description", 1L, "A", 2L, "BTREE", 128L));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);
        var annotation = index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength);

        Assert.NotNull(annotation);
        var lengths = Assert.IsType<int[]>(annotation!.Value);
        Assert.Equal([64, 128], lengths);
    }

    [Fact]
    public void Index_without_sub_part_does_not_annotate_IndexPrefixLength()
    {
        var context = BuildContext(
            ("plain_index_table", "IX_Plain", "Name", 1L, "A", 1L, "BTREE", (long?)null));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);

        Assert.Null(index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
    }

    [Fact]
    public void Mixed_sub_part_index_records_zero_for_null_positions()
    {
        var context = BuildContext(
            ("mixed_index_table", "IX_Mixed", "First", 1L, "A", 1L, "BTREE", 32L),
            ("mixed_index_table", "IX_Mixed", "Second", 1L, "A", 2L, "BTREE", (long?)null));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);
        var annotation = index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength);

        Assert.NotNull(annotation);
        Assert.Equal([32, 0], (int[])annotation!.Value!);
    }

    private static ScaffoldingPipelineContext BuildContext(
        params (string TableName, string IndexName, string ColumnName, long NonUnique, string Collation, long SeqInIndex, string IndexType, long? SubPart)[] rows
    )
    {
        var databaseModel = new DatabaseModel { DatabaseName = "test" };
        var tableNames = rows
            .Select(row => row.TableName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var capabilities = MySqlServerVersion.MySql(new Version(8, 4, 0)).Capabilities;
        var context = new ScaffoldingPipelineContext(
            new IndexStubConnection(rows),
            databaseModel,
            TableFilter.MatchAll,
            capabilities,
            []);

        foreach (var tableName in tableNames)
        {
            var table = new DatabaseTable { Database = databaseModel, Name = tableName };
            databaseModel.Tables.Add(table);
            context.TableLookup[tableName] = table;

            foreach (var columnName in rows
                         .Where(row => row.TableName == tableName)
                         .Select(row => row.ColumnName)
                         .Distinct(StringComparer.Ordinal))
            {
                var column = new DatabaseColumn
                {
                    Table = table,
                    Name = columnName,
                    StoreType = "varchar(255)",
                    IsNullable = false,
                };
                table.Columns.Add(column);
                context.Columns[(tableName, columnName)] = column;
            }
        }

        return context;
    }

    private sealed class IndexStubConnection : DbConnection
    {
        private readonly (string TableName, string IndexName, string ColumnName, long NonUnique, string Collation, long SeqInIndex, string IndexType, long? SubPart)[] _rows;
        private ConnectionState _state = ConnectionState.Open;

        public IndexStubConnection(
            (string TableName, string IndexName, string ColumnName, long NonUnique, string Collation, long SeqInIndex, string IndexType, long? SubPart)[] rows
        )
        {
            _rows = rows;
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "test";

        public override string DataSource => "stub";

        public override string ServerVersion => "8.4.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new IndexStubCommand(this, _rows);
    }

    private sealed class IndexStubCommand : DbCommand
    {
        private readonly IndexStubConnection _connection;
        private readonly (string TableName, string IndexName, string ColumnName, long NonUnique, string Collation, long SeqInIndex, string IndexType, long? SubPart)[] _rows;
        private readonly IndexStubParameterCollection _parameters = new();

        public IndexStubCommand(
            IndexStubConnection connection,
            (string TableName, string IndexName, string ColumnName, long NonUnique, string Collation, long SeqInIndex, string IndexType, long? SubPart)[] rows
        )
        {
            _connection = connection;
            _rows = rows;
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

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new IndexStubParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
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

            foreach (var row in _rows)
            {
                table.Rows.Add(
                    row.TableName,
                    row.IndexName,
                    row.ColumnName,
                    row.NonUnique,
                    row.Collation,
                    row.SeqInIndex,
                    row.IndexType,
                    row.SubPart.HasValue ? row.SubPart.Value : DBNull.Value);
            }

            return table.CreateDataReader();
        }
    }

    private sealed class IndexStubParameter : DbParameter
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

    private sealed class IndexStubParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot => ((ICollection)_items).SyncRoot;

        public override int Add(
            object value
        )
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(
            Array values
        )
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _items.Clear();

        public override bool Contains(
            object value
        ) => _items.Contains((DbParameter)value);

        public override bool Contains(
            string value
        ) => _items.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(
            Array array,
            int index
        ) => ((ICollection)_items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(
            object value
        ) => _items.IndexOf((DbParameter)value);

        public override int IndexOf(
            string parameterName
        ) => _items.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(
            int index,
            object value
        ) => _items.Insert(index, (DbParameter)value);

        public override void Remove(
            object value
        ) => _items.Remove((DbParameter)value);

        public override void RemoveAt(
            int index
        ) => _items.RemoveAt(index);

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
        ) => _items[index];

        protected override DbParameter GetParameter(
            string parameterName
        ) => _items[IndexOf(parameterName)];

        protected override void SetParameter(
            int index,
            DbParameter value
        ) => _items[index] = value;

        protected override void SetParameter(
            string parameterName,
            DbParameter value
        )
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items[index] = value;
            }
            else
            {
                _items.Add(value);
            }
        }
    }
}
