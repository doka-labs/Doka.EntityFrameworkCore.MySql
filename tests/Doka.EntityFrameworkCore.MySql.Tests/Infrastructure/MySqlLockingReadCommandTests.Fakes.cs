using IsolationLevel = System.Data.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    private sealed class CommandScope : IDisposable
    {
        private readonly ServiceProvider _services;

        internal CommandScope(
            IInterceptor? interceptor = null
        )
        {
            _services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            var builder = new DbContextOptionsBuilder<CommandContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(Database, MySqlServerVersion.MySql(new Version(8, 4, 0)));

            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            Context = new CommandContext(builder.Options);

            Connection = Assert.IsType<MySqlRelationalConnection>(Context.GetService<IRelationalConnection>());
            Dependencies = Context.GetService<RelationalCommandBuilderDependencies>();
        }

        internal RecordingConnection Database { get; } = new();
        internal CommandContext Context { get; }
        internal MySqlRelationalConnection Connection { get; }
        internal RelationalCommandBuilderDependencies Dependencies { get; }

        internal RelationalCommandParameterObject Parameters => new(Connection, null, null, Context, null);

        public void Dispose()
        {
            Context.Dispose();
            Database.Dispose();
            _services.Dispose();
        }

        internal MySqlRelationalCommand Command(
            MySqlLockingReadMode mode
        ) => new(Dependencies, "SELECT 1", "SELECT <redacted>", []) { LockingReadMode = mode };
    }

    private sealed class CommandContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public CommandContext(
            DbContextOptions<CommandContext> options
        ) : base(options) { }
    }

    private sealed class ThrowingOpenedInterceptor : DbConnectionInterceptor
    {
        internal bool Enabled { get; set; }
        internal Exception Failure { get; } = new InvalidOperationException("opened-interceptor-canary");

        public override void ConnectionOpened(
            DbConnection connection,
            ConnectionEndEventData eventData
        )
        {
            if (Enabled)
            {
                throw Failure;
            }
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            // WHY: This fake only records a synchronous callback; it performs no I/O.
            // ReSharper disable once MethodHasAsyncOverload
            ConnectionOpened(connection, eventData);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingConnection : DbConnection
    {
        private ConnectionState _state;

        [AllowNull]
        public override string ConnectionString { get; set; } = "Server=localhost;Database=doka;GuidFormat=Binary16;";

        public override string Database => "doka";
        public override string DataSource => "recording";
        public override string ServerVersion => "8.4.0";
        public override ConnectionState State => _state;
        internal int OpenCount { get; private set; }
        internal int CloseCount { get; private set; }
        internal int ReaderCount { get; set; }
        internal int CommitCount { get; set; }
        internal int RollbackCount { get; set; }
        internal int NonQueryCount { get; set; }
        internal int CommandDisposeCount { get; set; }
        internal CancellationToken LastNonQueryCancellationToken { get; set; }
        internal Exception? ReaderFailure { get; set; }
        internal Exception? NonQueryFailure { get; set; }
        internal Exception? CommandDisposeFailure { get; set; }
        internal Exception? CloseFailure { get; set; }
        internal Exception? EnlistmentFailure { get; set; }
        internal Transaction? Enlisted { get; private set; }
        internal List<string> LocalControlSql { get; } = [];
        internal List<(string Sql, int Timeout, bool HasTransaction)> ControlCommands { get; } = [];
        internal Func<string, Exception?>? LocalControlFailure { get; set; }
        internal Action<string>? BeforeLocalControl { get; set; }
        internal Action<string>? AfterCommandDispose { get; set; }

        public override void Open()
        {
            OpenCount++;
            SetState(ConnectionState.Open);
        }

        public override Task OpenAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: The fake mutates in-memory state; there is no asynchronous I/O to schedule.
            // ReSharper disable once MethodHasAsyncOverload
            Open();

            return Task.CompletedTask;
        }

        public override void Close()
        {
            CloseCount++;

            if (CloseFailure is { } failure)
            {
                throw failure;
            }

            SetState(ConnectionState.Closed);
        }

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        public override void EnlistTransaction(
            Transaction? transaction
        )
        {
            if (EnlistmentFailure is not null)
            {
                throw EnlistmentFailure;
            }

            Enlisted = transaction;
        }

        internal void SetState(
            ConnectionState state
        )
        {
            var previous = _state;
            _state = state;
            OnStateChange(new StateChangeEventArgs(previous, state));
        }

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => new RecordingTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        private readonly IsolationLevel _isolationLevel;

        private RecordingConnection? _connection;

        /// <summary>Initializes the transaction for the supplied connection and isolation level.</summary>
        public RecordingTransaction(
            RecordingConnection connection,
            IsolationLevel isolationLevel
        )
        {
            _isolationLevel = isolationLevel;
            _connection = connection;
        }

        public override IsolationLevel IsolationLevel => _isolationLevel;
        protected override DbConnection? DbConnection => _connection;

        public override void Commit()
        {
            if (_connection is { } current)
            {
                current.CommitCount++;
                _connection = null;
            }
        }

        public override void Rollback()
        {
            if (_connection is { } current)
            {
                current.RollbackCount++;
                _connection = null;
            }
        }
    }

    private sealed class RecordingCommand : DbCommand
    {
        private readonly RecordingConnection _connection;

        private readonly MySqlCommand _parameterOwner;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public RecordingCommand(
            RecordingConnection connection
        )
        {
            _connection = connection;
            _parameterOwner = new MySqlCommand();
            CommandText = string.Empty;
            DbConnection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; }

        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameterOwner.Parameters;
        public override void Cancel() { }
        public override void Prepare() { }

        public override int ExecuteNonQuery()
        {
            _connection.NonQueryCount++;

            if (CommandText.StartsWith("SAVEPOINT", StringComparison.Ordinal)
                || CommandText.StartsWith("ROLLBACK TO", StringComparison.Ordinal)
                || CommandText.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal))
            {
                _connection.LocalControlSql.Add(CommandText);
                _connection.ControlCommands.Add((CommandText, CommandTimeout, DbTransaction is not null));
                _connection.BeforeLocalControl?.Invoke(CommandText);

                if (_connection.LocalControlFailure?.Invoke(CommandText) is { } controlFailure)
                {
                    throw controlFailure;
                }
            }

            if (_connection.NonQueryFailure is { } failure)
            {
                throw failure;
            }

            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(
            CancellationToken cancellationToken
        )
        {
            _connection.LastNonQueryCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: This test command has no asynchronous I/O; record the same native attempt.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(ExecuteNonQuery());
        }

        public override object ExecuteScalar() => 1;
        protected override DbParameter CreateDbParameter() => new MySqlParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
        {
            _connection.ReaderCount++;

            if (_connection.ReaderFailure is { } failure)
            {
                throw failure;
            }

            return new DataTable().CreateDataReader();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: Both fake entry points share the same deterministic in-memory reader behavior.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(ExecuteDbDataReader(behavior));
        }

        protected override void Dispose(
            bool disposing
        )
        {
            if (disposing)
            {
                _connection.CommandDisposeCount++;
                _parameterOwner.Dispose();
                _connection.AfterCommandDispose?.Invoke(CommandText);

                if (_connection.CommandDisposeFailure is { } failure)
                {
                    throw failure;
                }
            }

            base.Dispose(disposing);
        }
    }
}
