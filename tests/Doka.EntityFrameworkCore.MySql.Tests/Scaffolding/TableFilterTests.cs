using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests.Scaffolding;

/// <summary>
/// Pins the per-call <c>TableFilter</c> contract: empty input collapses to
/// <c>MatchAll</c> (no SQL filter, no client-side rejection); a non-empty
/// list applies both the server-side <c>WHERE TABLE_NAME IN (...)</c>
/// parameter binding and the client-side <c>Matches</c> belt-and-suspenders check.
/// </summary>
public sealed class TableFilterTests
{
    [Fact]
    public void Empty_input_collapses_to_match_all()
    {
        var filter = TableFilter.For(Array.Empty<string>());

        Assert.Null(filter.Tables);
        Assert.True(filter.Matches("any_table"));
    }

    [Fact]
    public void Non_empty_input_carries_a_case_sensitive_set()
    {
        var filter = TableFilter.For(["Orders", "Products"]);

        Assert.NotNull(filter.Tables);
        Assert.Equal(2, filter.Tables!.Count);
        Assert.True(filter.Matches("Orders"));
        Assert.True(filter.Matches("Products"));
        Assert.False(filter.Matches("Customers"));
        Assert.False(filter.Matches("orders"));
    }

    [Fact]
    public void Match_all_singleton_short_circuits_the_filter()
    {
        var matchAll = TableFilter.MatchAll;

        Assert.Null(matchAll.Tables);
        Assert.True(matchAll.Matches("anything"));
    }

    [Fact]
    public void AppendTableNameFilter_writes_parametrized_in_clause()
    {
        var filter = TableFilter.For(["Orders", "Products"]);
        var sql = new StringBuilder("WHERE TABLE_SCHEMA = DATABASE()");
        using var command = new ScaffoldingTestCommand();

        var bound = ScaffoldingHelpers.AppendTableNameFilter(sql, command, filter);

        Assert.Equal(2, bound);
        Assert.Contains("TABLE_NAME IN (@t0, @t1)", sql.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal("@t0", command.Parameters[0]!.ParameterName);
        Assert.Equal("@t1", command.Parameters[1]!.ParameterName);
    }

    [Fact]
    public void AppendTableNameFilter_is_a_noop_on_match_all()
    {
        var sql = new StringBuilder("WHERE TABLE_SCHEMA = DATABASE()");
        using var command = new ScaffoldingTestCommand();

        var bound = ScaffoldingHelpers.AppendTableNameFilter(sql, command, TableFilter.MatchAll);

        Assert.Equal(0, bound);
        Assert.Equal("WHERE TABLE_SCHEMA = DATABASE()", sql.ToString());
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void AppendTableNameFilter_honors_custom_column_reference()
    {
        var filter = TableFilter.For(["Orders"]);
        var sql = new StringBuilder("WHERE ");
        using var command = new ScaffoldingTestCommand();

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, filter, "source.TABLE_NAME");

        Assert.Contains("source.TABLE_NAME IN (@t0)", sql.ToString(), StringComparison.Ordinal);
    }

    private sealed class ScaffoldingTestCommand : DbCommand
    {
        private readonly ScaffoldingTestParameterCollection _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new ScaffoldingTestParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        ) => throw new NotSupportedException();
    }

    private sealed class ScaffoldingTestParameter : DbParameter
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

    private sealed class ScaffoldingTestParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot => ((ICollection)_items).SyncRoot;

        public override int Add(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Add((DbParameter)value);
            return _items.Count - 1;
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
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Insert(index, (DbParameter)value);
        }

        public override void Remove(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Remove((DbParameter)value);
        }

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
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
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
                _items[index] = value;
            }
            else
            {
                _items.Add(value);
            }
        }
    }
}
