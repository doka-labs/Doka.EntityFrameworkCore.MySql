namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers the transaction-operability baseline for savepoints and commit-unknown diagnostics.
/// </summary>
public sealed class MySqlTransactionOperabilityTests
{
    /// <summary>
    /// Verifies that provider transactions surface savepoint support and issue the expected savepoint commands.
    /// </summary>
    [Fact]
    public void Provider_transactions_expose_and_execute_savepoint_commands()
    {
        using var connection = new RecordingDbConnection();
        using var context = new TransactionOperabilityContext(CreateOptions(connection));
        using var transaction = context.Database.BeginTransaction();

        Assert.True(transaction.SupportsSavepoints);

        transaction.CreateSavepoint("before-update");
        transaction.RollbackToSavepoint("before-update");
        transaction.ReleaseSavepoint("before-update");

        var commands = connection
            .Commands.Select(command => command.CommandText)
            .ToArray();

        Assert.Contains(
            commands,
            c => c.StartsWith("SAVEPOINT", StringComparison.OrdinalIgnoreCase)
                && c.Contains("before-update", StringComparison.Ordinal));
        Assert.Contains(
            commands,
            c => c.StartsWith("ROLLBACK TO", StringComparison.OrdinalIgnoreCase)
                && c.Contains("before-update", StringComparison.Ordinal));
        Assert.Contains(
            commands,
            c => c.StartsWith("RELEASE SAVEPOINT", StringComparison.OrdinalIgnoreCase)
                && c.Contains("before-update", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that commit failures with an unproven outcome use the active
    /// context logger after a shared provider was initialized without one.
    /// </summary>
    [Fact]
    public async Task Commit_unknown_failures_emit_the_resilience_diagnostic()
    {
        MySqlSingletonOptions primedSingletonOptions;

        await using (var primingConnection = new RecordingDbConnection())
        await using (var primingContext =
            new TransactionOperabilityContext(
                CreateOptions(primingConnection, defaultGuidFormat: MySqlGuidFormat.Char36)))
        await using (var primingTransaction = await primingContext.Database.BeginTransactionAsync())
        {
            primedSingletonOptions = primingContext
                .GetService<IEnumerable<ISingletonOptions>>()
                .OfType<MySqlSingletonOptions>()
                .Single();
            await primingTransaction.RollbackAsync();
        }

        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestLoggerProvider(sink)));
        await using var connection =
            new RecordingDbConnection(commitFailure: new SocketException((int)SocketError.ConnectionReset));

        await using var context =
            new TransactionOperabilityContext(
                CreateOptions(connection, loggerFactory, MySqlGuidFormat.Char36));

        var activeSingletonOptions = context
            .GetService<IEnumerable<ISingletonOptions>>()
            .OfType<MySqlSingletonOptions>()
            .Single();

        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Same(primedSingletonOptions, activeSingletonOptions);
        await Assert.ThrowsAsync<SocketException>(() => transaction.CommitAsync());

        var entry = Assert.Single(sink.Entries, candidate => candidate.EventId.Id == MySqlEventId.CommitUnknown.Id);

        Assert.Equal(MySqlLoggerCategory.Resilience, entry.Category);
        Assert.Contains("ExecuteInTransaction", entry.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<TransactionOperabilityContext> CreateOptions(
        DbConnection connection,
        ILoggerFactory? loggerFactory = null,
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
    {
        var builder = new DbContextOptionsBuilder<TransactionOperabilityContext>();

        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        builder.UseMySql(
            connection,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options
                .EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1))
                .DefaultGuidFormat(defaultGuidFormat));

        return builder.Options;
    }

    private sealed class TransactionOperabilityContext : DbContext
    {
        public TransactionOperabilityContext(
            DbContextOptions<TransactionOperabilityContext> options
        ) : base(options) { }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly Exception? _commitFailure;
        private ConnectionState _state = ConnectionState.Closed;

        public RecordingDbConnection(
            Exception? commitFailure = null
        )
        {
            _commitFailure = commitFailure;
        }

        public List<RecordingDbCommand> Commands { get; } = new();

        [AllowNull]
        public override string ConnectionString { get; set; } = "Server=localhost;Database=doka;";

        public override string Database => "doka";

        public override string DataSource => "recording";

        public override string ServerVersion => "8.4.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        public override Task OpenAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Open();

            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        )
        {
            if (_state != ConnectionState.Open)
            {
                Open();
            }

            return new RecordingDbTransaction(this, isolationLevel, _commitFailure);
        }

        protected override DbCommand CreateDbCommand()
        {
            var command = new RecordingDbCommand(this);

            Commands.Add(command);

            return command;
        }
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        private readonly Exception? _commitFailure;
        private readonly RecordingDbConnection _connection;

        public RecordingDbTransaction(
            RecordingDbConnection connection,
            IsolationLevel isolationLevel,
            Exception? commitFailure
        )
        {
            _connection = connection;
            _commitFailure = commitFailure;
            IsolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel { get; }

        public override bool SupportsSavepoints => true;

        protected override DbConnection DbConnection => _connection;

        public override void Commit()
        {
            if (_commitFailure is not null)
            {
                throw _commitFailure;
            }
        }

        public override Task CommitAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commit();

            return Task.CompletedTask;
        }

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

        public override void Cancel() { }

        public override int ExecuteNonQuery() => 1;

        public override Task<int> ExecuteNonQueryAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(ExecuteNonQuery());
        }

        public override object ExecuteScalar() => 1;

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        ) => new DataTable().CreateDataReader();
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; }

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = new();

        public override int Count => _parameters.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(
            object value
        )
        {
            _parameters.Add((DbParameter)value);

            return _parameters.Count - 1;
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

        public override void Clear() => _parameters.Clear();

        public override bool Contains(
            object value
        ) => _parameters.Contains((DbParameter)value);

        public override bool Contains(
            string value
        ) => _parameters.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(
            Array array,
            int index
        )
        {
            _parameters
                .ToArray()
                .CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(
            object value
        ) => _parameters.IndexOf((DbParameter)value);

        public override int IndexOf(
            string parameterName
        ) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(
            int index,
            object value
        ) => _parameters.Insert(index, (DbParameter)value);

        public override void Remove(
            object value
        ) => _parameters.Remove((DbParameter)value);

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
        ) => _parameters[IndexOf(parameterName)];

        protected override void SetParameter(
            int index,
            DbParameter value
        ) => _parameters[index] = value;

        protected override void SetParameter(
            string parameterName,
            DbParameter value
        )
        {
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
