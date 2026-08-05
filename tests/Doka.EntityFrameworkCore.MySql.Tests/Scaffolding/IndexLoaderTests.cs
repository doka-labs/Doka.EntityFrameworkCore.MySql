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
            new IndexRow(
                "prefixed_index_table",
                "IX_Prefixed",
                "Name",
                1,
                "A",
                1,
                "BTREE",
                64,
                null),
            new IndexRow(
                "prefixed_index_table",
                "IX_Prefixed",
                "Description",
                1,
                "A",
                2,
                "BTREE",
                128,
                null));

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
            new IndexRow(
                "plain_index_table",
                "IX_Plain",
                "Name",
                1,
                "A",
                1,
                "BTREE",
                null,
                null));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);

        Assert.Null(index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
    }

    [Fact]
    public void Mixed_sub_part_index_records_zero_for_null_positions()
    {
        var context = BuildContext(
            new IndexRow(
                "mixed_index_table",
                "IX_Mixed",
                "First",
                1,
                "A",
                1,
                "BTREE",
                32,
                null),
            new IndexRow(
                "mixed_index_table",
                "IX_Mixed",
                "Second",
                1,
                "A",
                2,
                "BTREE",
                null,
                null));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);
        var annotation = index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength);

        Assert.NotNull(annotation);
        Assert.Equal([32, 0], (int[])annotation!.Value!);
    }

    [Fact]
    public void Index_type_and_direction_metadata_are_preserved()
    {
        var context = BuildContext(
            new IndexRow(
                "index_type_table",
                "IX_FullText",
                "Body",
                1,
                null,
                1,
                "FULLTEXT",
                16,
                null),
            new IndexRow(
                "index_type_table",
                "IX_Spatial",
                "Location",
                1,
                "A",
                1,
                "RTREE",
                32,
                null),
            new IndexRow(
                "index_type_table",
                "IX_Unique",
                "Code",
                0,
                "D",
                1,
                "BTREE",
                null,
                null));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var fullTextIndex = table.Indexes.Single(index => index.Name == "IX_FullText");
        var spatialIndex = table.Indexes.Single(index => index.Name == "IX_Spatial");
        var uniqueIndex = table.Indexes.Single(index => index.Name == "IX_Unique");

        Assert.True(fullTextIndex.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?);
        Assert.True(spatialIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?);
        Assert.Null(fullTextIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.Null(spatialIndex.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength));
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal([true], uniqueIndex.IsDescending);
    }

    [Fact]
    public void Functional_key_part_remains_visible_without_an_invented_column()
    {
        var context = BuildContext(
            new IndexRow(
                "functional_index_table",
                "IX_NormalizedName",
                null,
                1,
                "A",
                1,
                "BTREE",
                null,
                "lower(`Name`)"));

        IndexLoader.Load(context);

        var table = Assert.Single(context.DatabaseModel.Tables);
        var index = Assert.Single(table.Indexes);
        var parts = Assert.IsType<MySqlScaffoldedIndexPart[]>(
            index.FindAnnotation(MySqlAnnotationNames.ScaffoldingIndexParts)?.Value);
        var part = Assert.Single(parts);

        Assert.Empty(index.Columns);
        Assert.Null(part.ColumnName);
        Assert.Equal("lower(`Name`)", part.Expression);
        Assert.False(part.IsDescending);
        Assert.Null(part.PrefixLength);
    }

    private static ScaffoldingPipelineContext BuildContext(
        params IndexRow[] rows
    )
    {
        var databaseModel = new DatabaseModel { DatabaseName = "test" };
        var tableNames = rows
            .Select(row => row.TableName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var capabilities = MySqlServerVersion.MySql(new Version(8, 4, 0)).Profile;
        var context = new ScaffoldingPipelineContext(
            new IndexStubConnection(rows),
            databaseModel,
            TableFilter.MatchAll,
            capabilities,
            [],
            "test",
            false,
            [],
            [],
            []);

        foreach (var tableName in tableNames)
        {
            var table = new DatabaseTable { Database = databaseModel, Name = tableName };
            databaseModel.Tables.Add(table);
            context.TableLookup[tableName] = table;

            foreach (var columnName in rows
                         .Where(row => row.TableName == tableName)
                         .Select(row => row.ColumnName)
                         .Where(columnName => columnName is not null)
                         .Select(columnName => columnName!)
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
        private readonly IndexRow[] _rows;
        private ConnectionState _state = ConnectionState.Open;

        public IndexStubConnection(
            IndexRow[] rows
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
        private readonly IndexRow[] _rows;
        private readonly IndexStubParameterCollection _parameters = new();

        public IndexStubCommand(
            IndexStubConnection connection,
            IndexRow[] rows
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
            table.Columns.Add("EXPRESSION", typeof(string));

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
                    row.SubPart.HasValue ? row.SubPart.Value : DBNull.Value,
                    row.Expression is null ? DBNull.Value : row.Expression);
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

    private sealed record IndexRow(
        string TableName,
        string IndexName,
        string? ColumnName,
        long NonUnique,
        string? Collation,
        long SeqInIndex,
        string IndexType,
        long? SubPart,
        string? Expression
    );
}
