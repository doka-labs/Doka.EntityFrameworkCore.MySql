using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Owns and delegates to a live connection while recording every executed command.
/// The wrapper keeps query-count assertions independent from connector diagnostics
/// and logging.
/// </summary>
internal sealed class CountingDbConnection : DbConnection
{
    private readonly DbConnection _innerConnection;
    private readonly ConcurrentQueue<string> _executedCommandTexts = new();
    private int _executedCommandCount;

    public CountingDbConnection(
        DbConnection innerConnection
    )
    {
        _innerConnection = innerConnection
            ?? throw new ArgumentNullException(nameof(innerConnection));
    }

    public int ExecutedCommandCount => Volatile.Read(ref _executedCommandCount);

    public IReadOnlyList<string> ExecutedCommandTexts => _executedCommandTexts.ToArray();

    [AllowNull]
    public override string ConnectionString
    {
        get => _innerConnection.ConnectionString;
        set => _innerConnection.ConnectionString = value;
    }

    public override string Database => _innerConnection.Database;

    public override string DataSource => _innerConnection.DataSource;

    public override string ServerVersion => _innerConnection.ServerVersion;

    public override ConnectionState State => _innerConnection.State;

    public override void ChangeDatabase(
        string databaseName
    ) => _innerConnection.ChangeDatabase(databaseName);

    public override void Close() => _innerConnection.Close();

    public override void Open() => _innerConnection.Open();

    public override Task OpenAsync(
        CancellationToken cancellationToken
    ) => _innerConnection.OpenAsync(cancellationToken);

    protected override DbTransaction BeginDbTransaction(
        IsolationLevel isolationLevel
    ) => _innerConnection.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() =>
        new CountingDbCommand(
            this,
            _innerConnection.CreateCommand(),
            RecordExecution);

    protected override void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _innerConnection.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RecordExecution(
        string commandText
    )
    {
        _executedCommandTexts.Enqueue(commandText);
        Interlocked.Increment(ref _executedCommandCount);
    }

    private sealed class CountingDbCommand : DbCommand
    {
        private readonly CountingDbConnection _connection;
        private readonly DbCommand _innerCommand;
        private readonly Action<string> _recordExecution;

        public CountingDbCommand(
            CountingDbConnection connection,
            DbCommand innerCommand,
            Action<string> recordExecution
        )
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _innerCommand = innerCommand ?? throw new ArgumentNullException(nameof(innerCommand));
            _recordExecution = recordExecution
                ?? throw new ArgumentNullException(nameof(recordExecution));
        }

        [AllowNull]
        public override string CommandText
        {
            get => _innerCommand.CommandText;
            set => _innerCommand.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => _innerCommand.CommandTimeout;
            set => _innerCommand.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _innerCommand.CommandType;
            set => _innerCommand.CommandType = value;
        }

        public override bool DesignTimeVisible
        {
            get => _innerCommand.DesignTimeVisible;
            set => _innerCommand.DesignTimeVisible = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _innerCommand.UpdatedRowSource;
            set => _innerCommand.UpdatedRowSource = value;
        }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set
            {
                if (value is not null
                    && !ReferenceEquals(value, _connection))
                {
                    throw new InvalidOperationException(
                        "The counting command cannot move to a different connection.");
                }
            }
        }

        protected override DbParameterCollection DbParameterCollection =>
            _innerCommand.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _innerCommand.Transaction;
            set => _innerCommand.Transaction = value;
        }

        public override void Cancel() => _innerCommand.Cancel();

        public override int ExecuteNonQuery()
        {
            RecordExecution();
            return _innerCommand.ExecuteNonQuery();
        }

        public override Task<int> ExecuteNonQueryAsync(
            CancellationToken cancellationToken
        )
        {
            RecordExecution();
            return _innerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        public override object? ExecuteScalar()
        {
            RecordExecution();
            return _innerCommand.ExecuteScalar();
        }

        public override Task<object?> ExecuteScalarAsync(
            CancellationToken cancellationToken
        )
        {
            RecordExecution();
            return _innerCommand.ExecuteScalarAsync(cancellationToken);
        }

        public override void Prepare() => _innerCommand.Prepare();

        public override Task PrepareAsync(
            CancellationToken cancellationToken = default
        ) => _innerCommand.PrepareAsync(cancellationToken);

        protected override DbParameter CreateDbParameter() =>
            _innerCommand.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
        {
            RecordExecution();
            return _innerCommand.ExecuteReader(behavior);
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken
        )
        {
            RecordExecution();
            return _innerCommand.ExecuteReaderAsync(behavior, cancellationToken);
        }

        protected override void Dispose(
            bool disposing
        )
        {
            if (disposing)
            {
                _innerCommand.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RecordExecution() => _recordExecution(CommandText);
    }
}
