namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Owns a unique schema, its exact client pool, native observer and isolated EF provider graph.</summary>
    private sealed partial class GraphStore : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly ServiceProvider _services;

        private string? _pooledConnectionString;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public GraphStore(
            string connectionString,
            MySqlServerVersion version,
            ServiceProvider services,
            MySqlConnection observer
        )
        {
            _connectionString = connectionString;
            _services = services;
            Version = version;
            Observer = observer;
        }

        /// <summary>Gets the owned connection identity; its database also isolates the client pool.</summary>
        internal string ConnectionString => _pooledConnectionString ?? _connectionString;

        /// <summary>Gets the actual configured pinned engine.</summary>
        internal MySqlServerVersion Version { get; }

        /// <summary>Gets the independently opened nonpooled native observer.</summary>
        internal MySqlConnection Observer { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // WHY: A canceled test must not abandon its own pool, schema or service graph.
            // Each independent cleanup stage runs even if an earlier stage fails.
            try
            {
                if (_pooledConnectionString is { } poolConnectionString)
                {
                    await using var poolKey = new MySqlConnection(poolConnectionString);
                    await MySqlConnection.ClearPoolAsync(poolKey, CancellationToken.None);
                }
            }
            finally
            {
                try
                {
                    await Observer.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        await _services.DisposeAsync();
                    }
                    finally
                    {
                        await IntegrationDatabaseUtilities.DropDatabaseAsync(ConnectionString);
                    }
                }
            }
        }

        /// <summary>Creates schema and seed data without executing the CTE API under test.</summary>
        internal static async Task<GraphStore> CreateAsync(
            string target,
            CancellationToken cancellationToken
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var name = "DokaCteGraph_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var connectionString = IntegrationDatabaseUtilities.BuildConnectionString(
                IntegrationTestEnvironment.GetBaseConnectionString(selected),
                name);

            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 8,

                // WHY: This fixture owns teardown; background idle expiry must not conceal a missed pool clear.
                ConnectionIdleTimeout = 0,
                ConnectionReset = true,
                AutoEnlist = false,
                GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            };

            var provider = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            var native =
                new MySqlConnection(new MySqlConnectionStringBuilder(builder.ConnectionString) { Pooling = false }
                    .ConnectionString);

            var store = new GraphStore(builder.ConnectionString,
                IntegrationTestEnvironment.GetServerVersion(selected),
                provider,
                native);

            try
            {
                await IntegrationDatabaseUtilities.EnsureDatabaseExistsAsync(store.ConnectionString, cancellationToken);
                await native.OpenAsync(cancellationToken);
                await using var context = store.CreateContext();

                // WHY: The provider adds transport and pool-name settings. Capture its
                // actual key before Open can redact credentials, then reuse that identity.
                store._pooledConnectionString = context.Database.GetDbConnection()
                    .ConnectionString;

                await store.ExecuteAsync(context.Database.GenerateCreateScript(), cancellationToken);
                await store.SeedAsync(cancellationToken);

                return store;
            }
            catch (Exception primary)
            {
                try
                {
                    await store.DisposeAsync();
                }
                catch (Exception cleanup)
                {
                    primary.Data[nameof(GraphStore)] = cleanup;
                }

                throw;
            }
        }

        /// <summary>Creates a lease over the fixed provider graph, with optional actual command observation or supplied model.</summary>
        internal GraphContext CreateContext(
            GraphCommands? commands = null,
            IModel? model = null,
            bool pooled = true
        )
        {
            var connection = new MySqlConnectionStringBuilder(ConnectionString) { Pooling = pooled }.ConnectionString;
            var options = IntegrationTestDbContextOptions
                .Create<GraphContext>()
                .UseMySql(connection, Version)
                .UseInternalServiceProvider(_services);

            if (commands is not null)
            {
                options.AddInterceptors(commands);
            }

            if (model is not null)
            {
                options.UseModel(model);
            }

            return new GraphContext(options.Options);
        }

        /// <summary>Creates an explicitly owned context pool over the same immutable engine/options graph.</summary>
        internal ServiceProvider CreateContextPool(
            GraphCommands commands
        ) => new ServiceCollection()
            .AddPooledDbContextFactory<GraphContext>(options => options
                    .UseMySql(ConnectionString, Version)
                    .UseInternalServiceProvider(_services)
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .AddInterceptors(commands),
                poolSize: 1)
            .BuildServiceProvider();

        /// <summary>Opens a fresh native connection for independently owned transactional or observation work.</summary>
        internal async Task<MySqlConnection> OpenNativeAsync(
            CancellationToken cancellationToken
        )
        {
            var native = new MySqlConnection(new MySqlConnectionStringBuilder(ConnectionString) { Pooling = false }
                .ConnectionString);

            try
            {
                await native.OpenAsync(cancellationToken);

                return native;
            }
            catch
            {
                await native.DisposeAsync();

                throw;
            }
        }

        /// <summary>Executes fixture-owned SQL with an explicit cancellation boundary.</summary>
        internal async Task ExecuteAsync(
            string sql,
            CancellationToken cancellationToken
        )
        {
            await using var command = new MySqlCommand(sql, Observer);
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>Reads an independent native scalar without using the feature's EF projection or tracking.</summary>
        internal async Task<object?> ReadScalarAsync(
            string sql,
            CancellationToken cancellationToken
        )
        {
            await using var command = new MySqlCommand(sql, Observer);

            return await command.ExecuteScalarAsync(cancellationToken);
        }
    }
}
