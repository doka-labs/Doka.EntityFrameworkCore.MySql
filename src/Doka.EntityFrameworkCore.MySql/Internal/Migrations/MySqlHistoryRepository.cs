namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlHistoryRepository : HistoryRepository
{
    private const string ApplyMigrationProcedureName = "__ef_apply_migration";

    public MySqlHistoryRepository(
        HistoryRepositoryDependencies dependencies
    ) : base(dependencies)
    {
        _ = dependencies.Options.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");

        if (!string.IsNullOrWhiteSpace(TableSchema))
        {
            const string message =
                "MySQL schema configuration is not supported. Remove the configured migrations history table schema.";

            var logger = dependencies
                .Options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Configuration);

            if (logger is not null)
            {
                MySqlLoggerMessages.SchemaUnsupported(
                    logger,
                    "MigrationsHistory",
                    TableSchema,
                    "migrations-history table schema declared",
                    "Remove the configured migrations history table schema.");
            }

            throw new InvalidOperationException(message);
        }
    }

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    protected override string ExistsSql
    {
        get
        {
            var tableLiteral = Dependencies
                .TypeMappingSource.GetMapping(typeof(string))
                .GenerateSqlLiteral(TableName);

            return $"""
                    SELECT CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM information_schema.tables
                            WHERE table_schema = DATABASE()
                              AND table_name = {tableLiteral}
                        ) THEN 1
                        ELSE 0
                    END
                    """;
        }
    }

    protected override bool InterpretExistsResult(
        object? value
    ) => MySqlScalarConvert.ToBoolean(value);

    public override void Create()
    {
        Dependencies
            .RawSqlCommandBuilder.Build(GetCreateScript())
            .ExecuteNonQuery(
                new RelationalCommandParameterObject(
                    Dependencies.Connection,
                    null,
                    null,
                    Dependencies.CurrentContext.Context,
                    Dependencies.CommandLogger,
                    CommandSource.Migrations));
    }

    public override async Task CreateAsync(
        CancellationToken cancellationToken = default
    )
    {
        await Dependencies
            .RawSqlCommandBuilder.Build(GetCreateScript())
            .ExecuteNonQueryAsync(
                new RelationalCommandParameterObject(
                    Dependencies.Connection,
                    null,
                    null,
                    Dependencies.CurrentContext.Context,
                    Dependencies.CommandLogger,
                    CommandSource.Migrations),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override string GetCreateScript() => BuildCreateHistoryTableScript("CREATE TABLE");

    public override string GetCreateIfNotExistsScript() => BuildCreateHistoryTableScript("CREATE TABLE IF NOT EXISTS");

    private string BuildCreateHistoryTableScript(
        string createClause
    )
    {
        var tableName = SqlGenerationHelper.DelimitIdentifier(TableName);
        var idColumn = SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName);
        var versionColumn = SqlGenerationHelper.DelimitIdentifier(ProductVersionColumnName);
        var primaryKey = SqlGenerationHelper.DelimitIdentifier($"PK_{TableName}");
        var terminator = SqlGenerationHelper.StatementTerminator;

        return $"""
                {createClause} {tableName} (
                    {idColumn} varchar(150) NOT NULL,
                    {versionColumn} varchar(32) NOT NULL,
                    CONSTRAINT {primaryKey} PRIMARY KEY ({idColumn})
                ) CHARACTER SET utf8mb4{terminator}
                """;
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        var lockInstance = new MySqlMigrationsDatabaseLock(this);
        lockInstance.AcquireLock();

        return lockInstance;
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default
    )
    {
        var lockInstance = new MySqlMigrationsDatabaseLock(this);
        await lockInstance
            .AcquireLockAsync(cancellationToken)
            .ConfigureAwait(false);

        return lockInstance;
    }

    public override string GetBeginIfNotExistsScript(
        string migrationId
    ) => BuildBeginConditionalScript("NOT EXISTS", migrationId);

    public override string GetBeginIfExistsScript(
        string migrationId
    ) => BuildBeginConditionalScript("EXISTS", migrationId);

    private string BuildBeginConditionalScript(
        string condition,
        string migrationId
    )
    {
        var migrationIdLiteral = Dependencies
            .TypeMappingSource.GetMapping(typeof(string))
            .GenerateSqlLiteral(migrationId);

        var procedure = SqlGenerationHelper.DelimitIdentifier(ApplyMigrationProcedureName);
        var tableName = SqlGenerationHelper.DelimitIdentifier(TableName);
        var idColumn = SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName);
        var terminator = SqlGenerationHelper.StatementTerminator;

        return $"""
                DROP PROCEDURE IF EXISTS {procedure}{terminator}
                CREATE PROCEDURE {procedure}()
                BEGIN
                    IF {condition} (SELECT 1 FROM {tableName} WHERE {idColumn} = {migrationIdLiteral}) THEN

                """;
    }

    public override string GetEndIfScript()
    {
        var procedure = SqlGenerationHelper.DelimitIdentifier(ApplyMigrationProcedureName);
        var terminator = SqlGenerationHelper.StatementTerminator;

        return $"""
                    END IF{terminator}
                END{terminator}
                CALL {procedure}(){terminator}
                DROP PROCEDURE IF EXISTS {procedure}{terminator}

                """;
    }

    internal sealed class MySqlMigrationsDatabaseLock : IMigrationsDatabaseLock
    {
        private const int LockTimeoutSeconds = 60;

        private readonly MySqlHistoryRepository _historyRepository;
        private readonly string _lockName;
        private readonly ILogger? _logger;
        private DbConnection? _dedicatedConnection;
        private int _lockAcquired;

        public MySqlMigrationsDatabaseLock(
            IHistoryRepository historyRepository
        )
        {
            HistoryRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
            _historyRepository = (MySqlHistoryRepository)historyRepository;
            _lockName = MySqlAdvisoryLockNaming.BuildLockName(
                _historyRepository.Dependencies.Connection.DbConnection.ConnectionString);
            _logger = _historyRepository
                .Dependencies.Options.FindExtension<CoreOptionsExtension>()
                ?.LoggerFactory?.CreateLogger(MySqlLoggerCategory.Migrations);
        }

        public IHistoryRepository HistoryRepository { get; }

        internal string LockName => _lockName;

        private TimeoutException BuildLockTimeoutException() =>
            new(
                $"Could not acquire the MySQL advisory lock '{_lockName}' within {LockTimeoutSeconds} seconds. "
                + "Another migration process may be running concurrently.");

        /// <summary>
        /// Acquires a MySQL advisory lock using GET_LOCK on a dedicated connection.
        /// Using a dedicated connection ensures the lock is held on a single physical
        /// session regardless of EF connection pooling behavior.
        /// </summary>
        public void AcquireLock()
        {
            using var activity = MySqlActivitySource.StartMigrationLockAcquire();
            activity?.SetTag("db.migration.lock_name", _lockName);
            var stopwatch = Stopwatch.StartNew();
            var outcome = "timeout";

            try
            {
                var connection = CreateDedicatedConnection();
                connection.Open();

                try
                {
                    var result = ExecuteOnConnection(
                        connection,
                        "SELECT GET_LOCK(@name, @timeout)",
                        ("@name", _lockName),
                        ("@timeout", LockTimeoutSeconds));

                    if (!MySqlScalarConvert.ToBoolean(result))
                    {
                        connection.Dispose();

                        throw BuildLockTimeoutException();
                    }
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }

                _dedicatedConnection = connection;
                Interlocked.Exchange(ref _lockAcquired, 1);
                outcome = "acquired";
            }
            finally
            {
                stopwatch.Stop();
                MySqlMeter.MigrationLockAcquireDuration.Record(
                    stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("outcome", outcome));
            }
        }

        /// <summary>
        /// Acquires a MySQL advisory lock asynchronously on a dedicated connection.
        /// </summary>
        public async Task AcquireLockAsync(
            CancellationToken cancellationToken = default
        )
        {
            using var activity = MySqlActivitySource.StartMigrationLockAcquire();
            activity?.SetTag("db.migration.lock_name", _lockName);
            var stopwatch = Stopwatch.StartNew();
            var outcome = "timeout";

            try
            {
                var connection = CreateDedicatedConnection();
                await connection
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    var result = await ExecuteOnConnectionAsync(
                            connection,
                            "SELECT GET_LOCK(@name, @timeout)",
                            cancellationToken,
                            ("@name", _lockName),
                            ("@timeout", LockTimeoutSeconds))
                        .ConfigureAwait(false);

                    if (!MySqlScalarConvert.ToBoolean(result))
                    {
                        await connection
                            .DisposeAsync()
                            .ConfigureAwait(false);

                        throw BuildLockTimeoutException();
                    }
                }
                catch
                {
                    await connection
                        .DisposeAsync()
                        .ConfigureAwait(false);
                    throw;
                }

                _dedicatedConnection = connection;
                Interlocked.Exchange(ref _lockAcquired, 1);
                outcome = "acquired";
            }
            finally
            {
                stopwatch.Stop();
                MySqlMeter.MigrationLockAcquireDuration.Record(
                    stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("outcome", outcome));
            }
        }

        public IMigrationsDatabaseLock ReacquireIfNeeded(
            bool connectionReopened,
            bool? transactionRestarted
        )
        {
            if (connectionReopened)
            {
                ReleaseAndDisposeCurrent();
                AcquireLock();
            }

            return this;
        }

        public async Task<IMigrationsDatabaseLock> ReacquireIfNeededAsync(
            bool connectionReopened,
            bool? transactionRestarted,
            CancellationToken cancellationToken = default
        )
        {
            if (connectionReopened)
            {
                await ReleaseAndDisposeCurrentAsync()
                    .ConfigureAwait(false);
                await AcquireLockAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return this;
        }

        public void Dispose() => ReleaseAndDisposeCurrent();

        public ValueTask DisposeAsync() => ReleaseAndDisposeCurrentAsync();

        /// <summary>
        /// Atomically detaches the dedicated connection (via
        /// <see cref="Interlocked.Exchange{T}(ref T, T)"/>) and disposes it after a
        /// best-effort <c>RELEASE_LOCK</c>. Idempotent: a second call observes the
        /// detached null state and returns. The exchange ordering guarantees that
        /// concurrent <see cref="Dispose"/> + <see cref="ReacquireIfNeeded"/> calls
        /// cannot race on the field, even if the operator invokes the lock from
        /// multiple threads.
        /// </summary>
        private void ReleaseAndDisposeCurrent()
        {
            var connection = Interlocked.Exchange(ref _dedicatedConnection, null);
            if (connection is null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _lockAcquired, 0) == 1)
            {
                try
                {
                    ExecuteOnConnection(connection, "SELECT RELEASE_LOCK(@name)", ("@name", _lockName));
                }
                catch (Exception exception)
                {
                    if (_logger is not null)
                    {
                        MySqlLoggerMessages.LockReleaseFailed(_logger, _lockName, exception);
                    }
                }
            }

            connection.Dispose();
        }

        private async ValueTask ReleaseAndDisposeCurrentAsync()
        {
            var connection = Interlocked.Exchange(ref _dedicatedConnection, null);
            if (connection is null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _lockAcquired, 0) == 1)
            {
                try
                {
                    await ExecuteOnConnectionAsync(
                            connection,
                            "SELECT RELEASE_LOCK(@name)",
                            CancellationToken.None,
                            ("@name", _lockName))
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (_logger is not null)
                    {
                        MySqlLoggerMessages.LockReleaseFailed(_logger, _lockName, exception);
                    }
                }
            }

            await connection
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        private MySqlConnection CreateDedicatedConnection()
        {
            // Create a dedicated non-pooled connection for the advisory lock.
            // Pooling is disabled to ensure the lock is held on a single physical session
            // and released deterministically when the connection is disposed.
            var connectionString = _historyRepository.Dependencies.Connection.DbConnection.ConnectionString;
            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
            };

            return new MySqlConnection(builder.ConnectionString);
        }

        private static object? ExecuteOnConnection(
            DbConnection connection,
            string sql,
            params (string Name, object Value)[] parameters
        )
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, parameters);
            return command.ExecuteScalar();
        }

        private static async Task<object?> ExecuteOnConnectionAsync(
            DbConnection connection,
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] parameters
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, parameters);
            return await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private static void AddParameters(
            DbCommand command,
            ReadOnlySpan<(string Name, object Value)> parameters
        )
        {
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
        }
    }
}
