namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRuntimeBaselineTests
{
    private sealed class AsyncAwareDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public AsyncAwareDbConnection(
            string connectionString
        )
        {
            ConnectionString = connectionString;
        }

        public List<AsyncAwareDbCommand> Commands { get; } = new();

        [AllowNull]
        public override string ConnectionString { get; set; }

        public override string Database => "doka";

        public override string DataSource => "async-aware";

        public bool OpenAsyncCalled { get; private set; }

        public bool OpenCalled { get; private set; }

        public override string ServerVersion => "8.4.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open()
        {
            OpenCalled = true;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenAsyncCalled = true;
            _state = ConnectionState.Open;

            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            var command = new AsyncAwareDbCommand(this);

            Commands.Add(command);

            return command;
        }
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        private readonly RecordingDbConnection _connection;

        public RecordingDbTransaction(
            RecordingDbConnection connection,
            IsolationLevel isolationLevel
        )
        {
            _connection = connection;
            IsolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel { get; }

        protected override DbConnection DbConnection => _connection;

        public override void Commit() { }

        public override void Rollback() { }
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        private readonly RecordingDbConnection _connection;
        private readonly RecordingDbParameterCollection _parameters = new();

        public RecordingDbCommand(
            RecordingDbConnection connection
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

        public DbTransaction? RecordedTransaction => DbTransaction;

        public override void Cancel() { }

        public override int ExecuteNonQuery() => 1;

        public override object ExecuteScalar() => 1;

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        ) => new DataTable().CreateDataReader();
    }

    private sealed class AsyncAwareDbCommand : DbCommand
    {
        private readonly AsyncAwareDbConnection _connection;
        private readonly RecordingDbParameterCollection _parameters = new();

        public AsyncAwareDbCommand(
            AsyncAwareDbConnection connection
        )
        {
            _connection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public bool ExecuteNonQueryAsyncCalled { get; private set; }

        public bool ExecuteNonQueryCalled { get; private set; }

        public bool ExecuteScalarAsyncCalled { get; private set; }

        public bool ExecuteScalarCalled { get; private set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery()
        {
            ExecuteNonQueryCalled = true;

            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteNonQueryAsyncCalled = true;

            return Task.FromResult(1);
        }

        public override object ExecuteScalar()
        {
            ExecuteScalarCalled = true;

            return 1;
        }

        public override Task<object?> ExecuteScalarAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteScalarAsyncCalled = true;

            return Task.FromResult<object?>(1);
        }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        ) => new DataTable().CreateDataReader();
    }

    private sealed class RecordingDbParameter : DbParameter
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

    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = new();

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

            return index < 0
                ? throw new InvalidOperationException($"Parameter '{parameterName}' was not found.")
                : _parameters[index];
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

            if (index < 0)
            {
                _parameters.Add(value);

                return;
            }

            _parameters[index] = value;
        }
    }
}
