namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLockingReadIntegrationTests
{
    private const string RowsTable = "LockingReadRows";
    private const string ParentsTable = "LockingReadParents";

    /// <summary>Bounds hangs and teardown preparation; elapsed time never proves a database lock.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(2));

    /// <summary>Applies only the requested public operator, keeping the query deferred.</summary>
    private static IQueryable<T> Mark<T>(
        IQueryable<T> query,
        string mode
    )
        where T : class => mode switch
    {
        "update" => query.ForUpdate(),
        "share" => query.ForShare(),
        "nowait" => query.ForUpdateNoWait(),
        "skip" => query.ForUpdateSkipLocked(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Observes an actual InnoDB lock wait belonging to a fresh, nonpooled physical session.</summary>
    private static Task WaitForNativeLockWaitAsync(
        LockStore store,
        MySqlConnection worker,
        Task pending,
        CancellationToken cancellationToken
    ) => MySqlNativeLockWaitObserver.ObserveAsync(store.Version.IsMariaDb,
        store.Observer,
        worker,
        pending,
        cancellationToken);

    /// <summary>Executes a fixture-owned SQL statement with explicit cancellation and optional native transaction.</summary>
    private static async Task<int> ExecuteNativeAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null
    )
    {
        await using var command = new MySqlCommand(sql, connection, transaction);

        return await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads fixture data independently from EF tracking and its query cache.</summary>
    private static async Task<int> ReadValueAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand($"SELECT `Value` FROM `{RowsTable}` WHERE `Id` = 1;", connection);

        return Convert.ToInt32(await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Releases only an unfinished test-owned EF transaction, independently of forward-work cancellation.</summary>
    private static async Task RollbackOwnedAsync(
        IDbContextTransaction transaction
    )
    {
        // WHY: Rollback and drainage must complete after cancellation; callers own the
        // transaction, and a completed transaction must not be rolled back a second time.
        if (transaction.GetDbTransaction()
            .Connection is not null)
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Collects a compiled asynchronous result with the caller's required token.</summary>
    private static async Task<LockRow[]> MaterializeAsync(
        IAsyncEnumerable<LockRow> query,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<LockRow>();

        await foreach (var row in query
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows.ToArray();
    }

    private sealed class LockContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LockContext(
            DbContextOptions<LockContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<LockParent>(entity =>
            {
                entity
                    .ToTable(ParentsTable)
                    .UseStorageEngine("InnoDB");

                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(64);
            });

            modelBuilder.Entity<LockRow>(entity =>
            {
                entity
                    .ToTable(RowsTable)
                    .UseStorageEngine("InnoDB");

                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Payload)
                    .HasMaxLength(64);

                entity
                    .HasIndex(row => row.Value)
                    .HasDatabaseName("IX_LockingRead_Value");

                entity
                    .HasOne(row => row.Parent)
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(row => row.ParentId);
            });
        }
    }

    private sealed class LockRow
    {
        public int Id { get; set; }

        public int Value { get; set; }

        public string Payload { get; set; } = string.Empty;

        public int ParentId { get; set; }

        public LockParent Parent { get; set; } = null!;
    }

    private sealed class LockParent
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<LockRow> Children { get; set; } = [];
    }

    /// <summary>Owns its schema and service graph while every actor receives a distinct nonpooled connection.</summary>
    private sealed class LockStore : IAsyncDisposable
    {
        private readonly string _database;
        private readonly ServiceProvider _services;

        private LockStore(
            IntegrationDatabaseTarget target,
            string database,
            string connectionString,
            MySqlConnection observer,
            ServiceProvider services
        )
        {
            Target = target;
            _database = database;
            ConnectionString = connectionString;
            Observer = observer;
            _services = services;
        }

        public IntegrationDatabaseTarget Target { get; }

        public string ConnectionString { get; }

        public MySqlConnection Observer { get; }

        public MySqlServerVersion Version => IntegrationTestEnvironment.GetServerVersion(Target);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // WHY: Every actor/reader/transaction is disposed before its store; protected
            // teardown must drop the owned database even after the forward token is canceled.
            try
            {
                await ExecuteNativeAsync(Observer, $"DROP DATABASE IF EXISTS `{_database}`;", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await Observer
                    .DisposeAsync()
                    .ConfigureAwait(false);

                await _services
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Creates actual InnoDB tables and deterministic PK fixtures in an isolated database.</summary>
        public static async Task<LockStore> CreateAsync(
            string target,
            CancellationToken cancellationToken,
            string? baseConnectionString = null
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var database = "DokaLocking_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var builder =
                new MySqlConnectionStringBuilder(baseConnectionString
                    ?? IntegrationTestEnvironment.GetBaseConnectionString(selected))
                {
                    Database = string.Empty,
                    Pooling = false,
                    AutoEnlist = false,
                    GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
                };

            var observer = new MySqlConnection(builder.ConnectionString);
            var services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            LockStore? store = null;

            try
            {
                await observer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteNativeAsync(observer,
                        $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4;",
                        cancellationToken)
                    .ConfigureAwait(false);

                builder.Database = database;
                builder.AutoEnlist = true;
                store = new LockStore(selected, database, builder.ConnectionString, observer, services);
                await observer
                    .ChangeDatabaseAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await using var context = store.CreateContext();
                await context
                    .Database
                    .ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken)
                    .ConfigureAwait(false);

                context.AddRange(new LockParent
                    {
                        Id = 1,
                        Name = "first",
                    },
                    new LockParent
                    {
                        Id = 2,
                        Name = "second",
                    });

                context.AddRange(new LockRow
                    {
                        Id = 1,
                        ParentId = 1,
                        Value = 10,
                        Payload = "one",
                    },
                    new LockRow
                    {
                        Id = 2,
                        ParentId = 1,
                        Value = 20,
                        Payload = "two",
                    },
                    new LockRow
                    {
                        Id = 3,
                        ParentId = 2,
                        Value = 30,
                        Payload = "three",
                    });

                await context
                    .SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                await using var engine = new MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() "
                    + "AND TABLE_NAME IN ('LockingReadRows', 'LockingReadParents') AND ENGINE = 'InnoDB';",
                    observer);

                if (Convert.ToInt32(await engine
                            .ExecuteScalarAsync(cancellationToken)
                            .ConfigureAwait(false),
                        CultureInfo.InvariantCulture)
                    != 2)
                {
                    throw new InvalidOperationException("The locking fixture requires two actual InnoDB tables.");
                }

                return store;
            }
            catch
            {
                // WHY: Setup failure still owns schema/connection cleanup; cancellation cannot abandon them.
                if (store is not null)
                {
                    await store
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    await observer
                        .DisposeAsync()
                        .ConfigureAwait(false);

                    await services
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }

                throw;
            }
        }

        /// <summary>Builds a context on this fixture's explicit service graph, preserving real model reuse.</summary>
        public LockContext CreateContext(
            LockCommands? commands = null,
            MySqlConnection? connection = null
        ) => new(CreateOptions(commands, connection: connection));

        /// <summary>Configures provider identity and caller-owned optional connection without global cache accumulation.</summary>
        public DbContextOptions<LockContext> CreateOptions(
            LockCommands? commands = null,
            bool retries = false,
            MySqlConnection? connection = null
        )
        {
            var builder = IntegrationTestDbContextOptions
                .Create<LockContext>()
                .UseInternalServiceProvider(_services);

            Action<MySqlDbContextOptionsBuilder> configure = provider =>
            {
                if (retries)
                {
                    provider.EnableRetryOnFailure();
                }
            };

            if (connection is null)
            {
                builder.UseMySql(ConnectionString, Version, configure);
            }
            else
            {
                builder.UseMySql(connection, Version, configure);
            }

            if (commands is not null)
            {
                builder.AddInterceptors(commands);
            }

            return builder.Options;
        }

        /// <summary>Owns a one-entry context pool while retaining this store's immutable provider and model identity.</summary>
        public ServiceProvider CreatePooledServices(
            LockCommands commands
        ) => new ServiceCollection()
            .AddPooledDbContextFactory<LockContext>(builder => builder
                    .UseInternalServiceProvider(_services)
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .UseMySql(ConnectionString, Version)
                    .AddInterceptors(commands),
                poolSize: 1)
            .BuildServiceProvider();

        /// <summary>Creates an independent actor that cannot accidentally enlist into the test's ambient scope.</summary>
        public async Task<MySqlConnection> OpenNativeAsync(
            CancellationToken cancellationToken
        )
        {
            var builder = new MySqlConnectionStringBuilder(ConnectionString) { AutoEnlist = false };
            var connection = new MySqlConnection(builder.ConnectionString);

            try
            {
                await connection
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                return connection;
            }
            catch
            {
                await connection
                    .DisposeAsync()
                    .ConfigureAwait(false);

                throw;
            }
        }
    }

    /// <summary>Records actual SELECT execution and connection identity, independently of SQL inspection.</summary>
    private sealed class LockCommands : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public List<DbConnection?> Connections { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken
        )
        {
            Statements.Add(command.CommandText);
            Connections.Add(command.Connection);

            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
