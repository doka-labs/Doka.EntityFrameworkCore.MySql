namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private const string SourceTable = "Insert select source";
    private const string TargetTable = "Insert select target";
    private const string ParentsTable = "InsertSelectParents";
    private static readonly Guid s_firstGuid = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid s_secondGuid = new("fedcba98-7654-3210-8877-665544332211");

    /// <summary>Bounds hangs only; elapsed time is never a correctness or throughput oracle.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(3));

    /// <summary>Executes fixture-owned SQL independently of the provider's command interceptor.</summary>
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

    /// <summary>Reads complete ordered scalar rows without hiding an extra provider readback.</summary>
    private static async Task<string[]> ReadNativeAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null
    )
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<string>();

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var columns = new string[reader.FieldCount];

            for (var index = 0; index < columns.Length; index++)
            {
                // WHY: ReadAsync has already buffered this row; field access performs no extra database I/O.
                // ReSharper disable once MethodHasAsyncOverload
                columns[index] = reader.IsDBNull(index)
                    ? "<NULL>"
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)!;
            }

            rows.Add(string.Join("|", columns));
        }

        return rows.ToArray();
    }

    private sealed class InsertContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public InsertContext(
            DbContextOptions<InsertContext> options
        ) : base(options) { }

        public int TenantId { get; set; } = 1;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InsertParent>(entity =>
            {
                entity
                    .ToTable(ParentsTable)
                    .UseStorageEngine("InnoDB");

                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity.HasQueryFilter(row => row.Visible);
            });

            modelBuilder.Entity<InsertSource>(entity =>
            {
                entity
                    .ToTable(SourceTable)
                    .UseStorageEngine("InnoDB");

                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Label)
                    .HasColumnName("quoted ` source")
                    .HasMaxLength(100);

                entity
                    .HasOne(row => row.Parent)
                    .WithMany(row => row.Children)
                    .HasForeignKey(row => row.ParentId);

                entity.HasQueryFilter(row => !row.IsDeleted && row.TenantId == TenantId);
            });

            modelBuilder.Entity<InsertTarget>(entity =>
            {
                entity
                    .ToTable(TargetTable)
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Code)
                    .HasColumnName("order")
                    .HasMaxLength(100);

                entity
                    .Property(row => row.TenantId)
                    .HasDefaultValue(1);

                entity
                    .Property(row => row.Optional)
                    .HasDefaultValue("server-default")
                    .HasMaxLength(100);

                entity
                    .Property(row => row.Computed)
                    .HasComputedColumnSql("`Value` * 2", stored: true);

                entity.Ignore(row => row.Unmapped);
                entity.HasQueryFilter(row => row.TenantId == TenantId);
            });

            modelBuilder.Entity<ShadowTarget>(entity =>
            {
                entity
                    .ToTable("InsertShadow")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property<string>("Payload")
                    .HasMaxLength(100)
                    .IsRequired();
            });

            modelBuilder.Entity<FieldTarget>(entity =>
            {
                entity
                    .ToTable("InsertField")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property<string>("_payload")
                    .HasField("_payload")
                    .HasColumnName("Payload")
                    .HasMaxLength(100)
                    .IsRequired();
            });

            modelBuilder.Entity<TphTarget>(entity =>
            {
                entity
                    .ToTable("InsertTph")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Code)
                    .HasMaxLength(100);

                entity
                    .HasDiscriminator<string>("Kind")
                    .HasValue<TphTarget>("base")
                    .HasValue<SpecialTarget>("special");
            });

            modelBuilder.Entity<GuidSource>(entity =>
            {
                entity
                    .ToTable("InsertGuidSource")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Binary)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.Text)
                    .HasColumnType("char(36)");

                if (Database.IsMariaDb())
                {
                    entity
                        .Property(row => row.Native)
                        .HasColumnType("uuid");
                }
                else
                {
                    entity.Ignore(row => row.Native);
                }
            });

            modelBuilder.Entity<GuidTarget>(entity =>
            {
                entity
                    .ToTable("InsertGuidTarget")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Binary)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.Text)
                    .HasColumnType("char(36)");

                if (Database.IsMariaDb())
                {
                    entity
                        .Property(row => row.Native)
                        .HasColumnType("uuid");
                }
                else
                {
                    entity.Ignore(row => row.Native);
                }
            });

            modelBuilder.Entity<GuidDefaultTarget>(entity =>
            {
                entity
                    .ToTable("InsertGuidDefault")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .HasColumnType("char(36)")
                    .HasDefaultValueSql("UUID()");
            });

            modelBuilder.Entity<NonTransactionalTarget>(entity =>
            {
                entity
                    .ToTable("InsertMyIsam")
                    .UseStorageEngine("MyISAM");
            });

            modelBuilder.Entity<SharedTarget>(entity =>
            {
                entity
                    .ToTable("InsertShared")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Code)
                    .HasColumnName("SharedCode")
                    .HasMaxLength(100);

                entity.OwnsOne(row => row.Details,
                    owned =>
                    {
                        owned
                            .Property(row => row.Code)
                            .HasColumnName("SharedCode")
                            .HasMaxLength(100);

                        owned
                            .Property(row => row.Note)
                            .HasColumnName("CompanionNote")
                            .HasMaxLength(100)
                            .IsRequired();
                    });

                entity
                    .Navigation(row => row.Details)
                    .IsRequired();
            });

            modelBuilder.Entity<NarrowTarget>(entity =>
            {
                entity
                    .ToTable("InsertNarrow")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Amount)
                    .HasPrecision(5, 2);
            });

            ConfigureCustomGuidModel(modelBuilder);
            ConfigureFacetModel(modelBuilder);
            ConfigureWideModel(modelBuilder);
            if (Database.IsMariaDb())
            {
                modelBuilder.Entity<NativeGuidDefaultTarget>(entity =>
                {
                    entity
                        .ToTable("InsertNativeGuidDefault")
                        .UseStorageEngine("InnoDB");

                    entity
                        .Property(row => row.Id)
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("UUID()");
                });
            }

            modelBuilder.Entity<ClientGuidTarget>(entity =>
            {
                entity
                    .ToTable("InsertClientGuid")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Id)
                    .HasColumnType("binary(16)");
            });
        }
    }

    private sealed class InsertSource
    {
        public int Id { get; set; }

        public int Value { get; set; }

        public string Label { get; set; } = string.Empty;

        public int TenantId { get; set; }

        public bool IsDeleted { get; set; }

        public int ParentId { get; set; }

        public InsertParent Parent { get; set; } = null!;
    }

    private sealed class InsertParent
    {
        public int Id { get; set; }

        public bool Visible { get; set; }

        public List<InsertSource> Children { get; set; } = [];
    }

    private sealed class InsertTarget
    {
        public int Id { get; set; }

        public int Value { get; set; }

        public string Code { get; set; } = string.Empty;

        public int TenantId { get; set; }

        public string? Optional { get; set; }

        public int Computed { get; set; }

        public string Unmapped { get; set; } = string.Empty;
    }

    private sealed class ShadowTarget
    {
        public int Id { get; set; }
    }

    private sealed class FieldTarget
    {
        private string _payload = string.Empty;
        public int Id { get; set; }
    }

    private class TphTarget
    {
        public int Id { get; set; }

        public int Value { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class SpecialTarget : TphTarget;

    private sealed class GuidSource
    {
        public int Id { get; set; }

        public Guid Binary { get; set; }

        public Guid Text { get; set; }

        public Guid Native { get; set; }
    }

    private sealed class GuidTarget
    {
        public int Id { get; set; }

        public Guid Binary { get; set; }

        public Guid Text { get; set; }

        public Guid Native { get; set; }
    }

    private sealed class GuidDefaultTarget
    {
        public Guid Id { get; set; }

        public int Value { get; set; }
    }

    private sealed class NonTransactionalTarget
    {
        public int Id { get; set; }

        public int Value { get; set; }
    }

    private sealed class SharedTarget
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public SharedDetails Details { get; set; } = new();
    }

    private sealed class SharedDetails
    {
        public string Code { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }

    private sealed class NarrowTarget
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    private sealed class NativeGuidDefaultTarget
    {
        public Guid Id { get; set; }

        public int Value { get; set; }
    }

    private sealed class ClientGuidTarget
    {
        public Guid Id { get; set; }

        public int Value { get; set; }
    }

    /// <summary>Owns one schema and provider graph; actor connections never borrow another test's session.</summary>
    private sealed class InsertStore : IAsyncDisposable
    {
        private readonly string _database;
        private readonly ServiceProvider _services;

        private InsertStore(
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
            // WHY: Protected teardown must complete after a canceled write without claiming its outcome.
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

        /// <summary>Creates a generated schema and deterministic seed before command observation begins.</summary>
        public static async Task<InsertStore> CreateAsync(
            string target,
            CancellationToken cancellationToken,
            string? baseConnectionString = null
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var database = "DokaInsertSelect_"
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

            InsertStore? store = null;

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
                store = new InsertStore(selected, database, builder.ConnectionString, observer, services);
                await observer
                    .ChangeDatabaseAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await using var context = store.CreateContext();
                await context
                    .Database
                    .ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteNativeAsync(observer,
                        $"INSERT INTO `{ParentsTable}` (`Id`,`Visible`) VALUES (1,1),(2,0); "
                        + $"INSERT INTO `{SourceTable}` (`Id`,`Value`,`quoted `` source`,`TenantId`,`IsDeleted`,`ParentId`) "
                        + "VALUES (1,10,'alpha',1,0,1),(2,20,'beta',1,0,1),(3,30,'gamma',1,0,2),(4,40,'deleted',1,1,1),(5,50,'other',2,0,1);",
                        cancellationToken)
                    .ConfigureAwait(false);

                var engines = await ReadNativeAsync(observer,
                        "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='Insert select target';",
                        cancellationToken)
                    .ConfigureAwait(false);

                if (engines.Length != 1
                    || !engines[0]
                        .Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Native atomicity controls require an actual InnoDB insertion target.");
                }

                return store;
            }
            catch
            {
                // WHY: Forward cancellation cannot abandon the schema or native connection owned by setup.
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

        /// <summary>Creates one context with this store's immutable model/provider identity.</summary>
        public InsertContext CreateContext(
            InsertCommands? commands = null,
            MySqlConnection? connection = null
        ) => new(CreateOptions(commands, connection));

        /// <summary>Preserves optional caller connection ownership and isolates the global provider cache.</summary>
        public DbContextOptions<InsertContext> CreateOptions(
            InsertCommands? commands = null,
            MySqlConnection? connection = null
        )
        {
            var builder = IntegrationTestDbContextOptions
                .Create<InsertContext>()
                .UseInternalServiceProvider(_services);

            if (connection is null)
            {
                builder.UseMySql(ConnectionString, Version);
            }
            else
            {
                builder.UseMySql(connection, Version);
            }

            if (commands is not null)
            {
                builder.AddInterceptors(commands);
            }

            return builder.Options;
        }

        /// <summary>Creates a native actor without implicit ambient enlistment.</summary>
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

        /// <summary>Creates a one-entry context pool without changing the internal provider graph.</summary>
        public ServiceProvider CreatePooledServices(
            InsertCommands commands
        ) => new ServiceCollection()
            .AddPooledDbContextFactory<InsertContext>(builder => builder
                    .UseInternalServiceProvider(_services)
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .UseMySql(ConnectionString, Version)
                    .AddInterceptors(commands),
                poolSize: 1)
            .BuildServiceProvider();
    }

    /// <summary>Observes command kind, binding and ownership without altering execution.</summary>
    private sealed class InsertCommands : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public List<string> TransactionControls { get; } = [];

        public List<object?[]> Parameters { get; } = [];
        public List<DbConnection?> Connections { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public int Readers { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken
        )
        {
            var text = command.CommandText.TrimStart();

            // WHY: The active-local-transaction receipt uses only its own savepoint controls.
            // Every other nonquery remains DATA here, so an unexpected write or hidden batch still fails the one-statement oracle.
            if (text.StartsWith("SAVEPOINT ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("ROLLBACK TO ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("RELEASE SAVEPOINT ", StringComparison.OrdinalIgnoreCase))
            {
                TransactionControls.Add(command.CommandText);

                return new ValueTask<InterceptionResult<int>>(result);
            }

            Statements.Add(command.CommandText);
            Parameters.Add(command
                .Parameters
                .Cast<DbParameter>()
                .Select(parameter => parameter.Value)
                .ToArray());

            Connections.Add(command.Connection);
            Tokens.Add(cancellationToken);

            return new ValueTask<InterceptionResult<int>>(result);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken
        )
        {
            Readers++;

            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
