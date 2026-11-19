using IsolationLevel = System.Data.IsolationLevel;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    private sealed class CommandScope : IDisposable
    {
        private readonly ServiceProvider _services;

        internal CommandScope(
            DbCommandInterceptor? interceptor = null
        )
        {
            _services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            var options = new DbContextOptionsBuilder<ReaderContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(Database, MySqlServerVersion.MySql(new Version(8, 4, 0)));

            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }

            Context = new ReaderContext(options.Options);
            Connection = Context.GetService<IRelationalConnection>();
        }

        internal RecordingConnection Database { get; } = new();
        internal ReaderContext Context { get; }
        internal IRelationalConnection Connection { get; }

        public void Dispose()
        {
            Database.CloseFailure = null;
            Database.CommandDisposeFailure = null;
            Context.Dispose();
            Database.Dispose();
            _services.Dispose();
        }

        internal RelationalCommandParameterObject Parameters(
            IReadOnlyDictionary<string, object?>? values,
            IReadOnlyList<ReaderColumn?>? columns,
            CommandSource source = CommandSource.LinqQuery
        ) => new(
            Connection,
            values,
            columns,
            Context,
            Context.GetService<IRelationalCommandDiagnosticsLogger>(),
            detailedErrorsEnabled: true,
            source);

        internal MySqlRelationalCommand Command(
            MySqlRecursiveQueryPolicy? policy
        ) => new(Context.GetService<RelationalCommandBuilderDependencies>(),
            "SELECT guarded-payload",
            "SELECT <redacted>",
            [])
        {
            RecursiveQueryPolicy = policy,
        };
    }

    private sealed class ReaderContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ReaderContext(
            DbContextOptions<ReaderContext> options
        ) : base(options) { }
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
        internal Func<ReaderProbe> ReaderFactory { get; set; } = static () => Rows(typeof(int), 7, 8);
        internal ReaderProbe? LastReader { get; set; }
        internal int OpenCalls { get; private set; }
        internal int CloseCalls { get; private set; }
        internal int CommandsCreated { get; private set; }
        internal int CommandsDisposed { get; set; }
        internal int ReaderExecutions { get; set; }
        internal Exception? CloseFailure { get; set; }
        internal Exception? CommandDisposeFailure { get; set; }

        public override void Open()
        {
            OpenCalls++;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: The fake records in-memory state without asynchronous I/O.
            // ReSharper disable once MethodHasAsyncOverload
            Open();

            return Task.CompletedTask;
        }

        public override void Close()
        {
            CloseCalls++;

            if (CloseFailure is { } failure)
            {
                throw failure;
            }

            _state = ConnectionState.Closed;
        }

        public override void ChangeDatabase(
            string databaseName
        ) => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        ) => new RecordingTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand()
        {
            CommandsCreated++;

            return new RecordingCommand(this);
        }
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        private readonly RecordingConnection _connection;
        private readonly IsolationLevel _isolationLevel;

        /// <summary>Initializes the transaction for the supplied connection and isolation level.</summary>
        public RecordingTransaction(
            RecordingConnection connection,
            IsolationLevel isolationLevel
        )
        {
            _connection = connection;
            _isolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel => _isolationLevel;
        protected override DbConnection DbConnection => _connection;
        public override void Commit() => throw new InvalidOperationException("Unexpected hidden commit.");
        public override void Rollback() => throw new InvalidOperationException("Unexpected hidden rollback.");
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
        public override int ExecuteNonQuery() => 1;
        public override object ExecuteScalar() => 1;
        protected override DbParameter CreateDbParameter() => new MySqlParameter();

        protected override DbDataReader ExecuteDbDataReader(
            CommandBehavior behavior
        )
        {
            _connection.ReaderExecutions++;

            return _connection.LastReader = _connection.ReaderFactory();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: The fake's asynchronous path performs the same deterministic in-memory operation.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(ExecuteDbDataReader(behavior));
        }

        protected override void Dispose(
            bool disposing
        )
        {
            if (disposing)
            {
                _connection.CommandsDisposed++;
                _parameterOwner.Dispose();

                if (_connection.CommandDisposeFailure is { } failure)
                {
                    throw failure;
                }
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ReaderObserver : DbCommandInterceptor
    {
        internal DbDataReader? ExecutedReader { get; private set; }
        internal DbDataReader? Replacement { get; set; }
        internal CommandSource? Source { get; private set; }
        internal CancellationToken CancellationToken { get; private set; }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result
        )
        {
            ExecutedReader = result;
            Source = eventData.CommandSource;

            return Replacement ?? result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            ExecutedReader = result;
            Source = eventData.CommandSource;
            CancellationToken = cancellationToken;

            return ValueTask.FromResult(Replacement ?? result);
        }
    }
}
