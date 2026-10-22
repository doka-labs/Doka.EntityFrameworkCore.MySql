namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRuntimeBaselineTests
{
    private static DbContextOptions<RuntimeBaselineContext> CreateOptions(
        DbConnection connection
    )
    {
        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();

        builder.UseMySql(connection, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private static DbContextOptions<RuntimeBaselineContext> CreateOptions(
        string connectionString,
        Action<MySqlDbContextOptionsBuilder>? configure = null
    )
    {
        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();

        builder.UseMySql(
            connectionString,
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            configure);

        return builder.Options;
    }

    private static DbContextOptions<RuntimeBaselineContext> CreateOptions(
        string connectionString,
        IServiceProvider internalServiceProvider
    )
    {
        var builder = new DbContextOptionsBuilder<RuntimeBaselineContext>();

        builder.UseInternalServiceProvider(internalServiceProvider);
        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class RuntimeBaselineContext : DbContext
    {
        public RuntimeBaselineContext(
            DbContextOptions<RuntimeBaselineContext> options
        ) : base(options) { }
    }

#pragma warning disable EF1001 // Test double for EF's named-connection-string resolution boundary.
    private sealed class RecordingNamedConnectionStringResolver
        : Microsoft.EntityFrameworkCore.Storage.Internal.INamedConnectionStringResolver
    {
        private readonly string _expectedInput;
        private readonly string _resolvedConnectionString;

        public RecordingNamedConnectionStringResolver(
            string expectedInput,
            string resolvedConnectionString
        )
        {
            _expectedInput = expectedInput;
            _resolvedConnectionString = resolvedConnectionString;
        }

        public string? LastInput { get; private set; }

        public string ResolveConnectionString(
            string connectionString
        )
        {
            Assert.Equal(_expectedInput, connectionString);
            LastInput = connectionString;

            return _resolvedConnectionString;
        }
    }
#pragma warning restore EF1001

    private sealed class RecordingDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public List<RecordingDbCommand> Commands { get; } = new();

        public RecordingDbTransaction? LastTransaction { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } =
            "Server=localhost;Database=doka;GuidFormat=Binary16;";

        public override string Database => "doka";

        public override string DataSource => "recording";

        public bool IsDisposed { get; private set; }

        public override string ServerVersion => "8.4.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(
            string databaseName
        ) => ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(
            IsolationLevel isolationLevel
        )
        {
            if (_state != ConnectionState.Open)
            {
                Open();
            }

            LastTransaction = new RecordingDbTransaction(this, isolationLevel);

            return LastTransaction;
        }

        protected override DbCommand CreateDbCommand()
        {
            var command = new RecordingDbCommand(this);

            Commands.Add(command);

            return command;
        }

        protected override void Dispose(
            bool disposing
        )
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class AsyncAwareDriverFacade : IMySqlDriverFacade
    {
        public List<AsyncAwareDbConnection> DatabaseConnections { get; } = new();

        public string DriverName => "AsyncAware";

        public List<AsyncAwareDbConnection> ServerConnections { get; } = new();

        public DbConnection CreateConnection(
            string connectionString
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            var isServerConnection = !connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase)
                && !connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);

            var connection = new AsyncAwareDbConnection(connectionString);

            if (isServerConnection)
            {
                ServerConnections.Add(connection);
            }
            else
            {
                DatabaseConnections.Add(connection);
            }

            return connection;
        }
    }
}
