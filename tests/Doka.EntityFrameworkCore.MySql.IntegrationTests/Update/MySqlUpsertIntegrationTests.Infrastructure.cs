namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    private static readonly string[] s_wideColumns = Enumerable
        .Range(1, 32)
        .Select(index => "C" + index.ToString("D2", CultureInfo.InvariantCulture))
        .ToArray();

    /// <summary>Bounds a hung fixture; no elapsed time is used as a correctness or performance oracle.</summary>
    private static CancellationTokenSource CreateSafetyCancellation() => new(TimeSpan.FromMinutes(2));

    private static UpsertRow NewRow(
        int id,
        string? name = null
    ) => new()
    {
        Id = id,
        UniqueKey = "key-" + id.ToString(CultureInfo.InvariantCulture),
        Name = name ?? "name-" + id.ToString(CultureInfo.InvariantCulture),
        Unchanged = "preserved",
        Number = id,
        Amount = id + 0.1250m,
        Timestamp = new DateTime(2026,
            11,
            17,
            12,
            30,
            45,
            123,
            DateTimeKind.Unspecified),
        Payload = [0, 1, 39, 34, 92, 127, 128, 255],
        GuidValue = new Guid("00112233-4455-6677-8899-aabbccddeeff"),
        NullableGuid = new Guid("10213243-5465-7687-98a9-bacbdcedfe0f"),
        Status = RowStatus.Ready,
        TenantId = 7,
        Json = "{\"text\":\"Hallo 'Test'\",\"items\":[1,null,3]}",
    };

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

    private static string RejectControlledValue(
        string value
    ) => value == "controlled-converter-failure"
        ? throw new ControlledBindFailure()
        : value;

    private sealed class UpsertStore : IAsyncDisposable
    {
        private readonly string _database;
        private readonly ServiceProvider _services;

        private UpsertStore(
            IntegrationDatabaseTarget target,
            string mode,
            string database,
            string connectionString,
            MySqlConnection observer,
            ServiceProvider services
        )
        {
            Target = target;
            Mode = mode;
            _database = database;
            ConnectionString = connectionString;
            Observer = observer;
            _services = services;
        }

        internal IntegrationDatabaseTarget Target { get; }
        internal string Mode { get; }
        internal string ConnectionString { get; }
        internal MySqlConnection Observer { get; }
        internal MySqlServerVersion Version => IntegrationTestEnvironment.GetServerVersion(Target);

        public async ValueTask DisposeAsync()
        {
            try
            {
                // WHY: Fixture cleanup owns its schema even after a cancelled write; it is not request work.
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

        internal static async Task<UpsertStore> CreateAsync(
            string target,
            string mode,
            CancellationToken cancellationToken,
            string? baseConnectionString = null
        )
        {
            var selected = Enum.Parse<IntegrationDatabaseTarget>(target);
            var database = "DokaUpsert_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var connection =
                new MySqlConnectionStringBuilder(baseConnectionString
                    ?? IntegrationTestEnvironment.GetBaseConnectionString(selected))
                {
                    Database = string.Empty,
                    Pooling = false,
                    AutoEnlist = false,
                    GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
                };

            var observer = new MySqlConnection(connection.ConnectionString);
            var services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();

            UpsertStore? store = null;

            try
            {
                await observer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteNativeAsync(observer,
                        $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                        cancellationToken)
                    .ConfigureAwait(false);

                connection.Database = database;
                connection.AutoEnlist = true;
                store = new UpsertStore(selected, mode, database, connection.ConnectionString, observer, services);
                await observer
                    .ChangeDatabaseAsync(database, cancellationToken)
                    .ConfigureAwait(false);

                await using var context = store.CreateContext();
                await context
                    .Database
                    .ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), cancellationToken)
                    .ConfigureAwait(false);

                return store;
            }
            catch
            {
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

        internal UpsertContext CreateContext(
            Writes? capture = null,
            MySqlConnection? connection = null,
            IInterceptor? additionalInterceptor = null
        )
        {
            var builder = IntegrationTestDbContextOptions
                .Create<UpsertContext>()
                .UseInternalServiceProvider(_services);

            if (connection is null)
            {
                builder.UseMySql(ConnectionString, Version);
            }
            else
            {
                builder.UseMySql(connection, Version);
            }

            if (capture is not null)
            {
                builder.AddInterceptors(capture);
            }

            if (additionalInterceptor is not null)
            {
                builder.AddInterceptors(additionalInterceptor);
            }

            return new UpsertContext(builder.Options, Mode);
        }

        internal UpsertContext CreateDataSourceContext(
            MySqlDataSource dataSource,
            Writes capture
        ) => new(IntegrationTestDbContextOptions
                .Create<UpsertContext>()

                // WHY: Data-source identity is a singleton contract. Its independently owned graph
                // must not reuse the fixture graph already initialized with a connection string.
                .EnableServiceProviderCaching(false)
                .UseMySql(dataSource, Version)
                .AddInterceptors(capture)
                .Options,
            Mode);

        internal UpsertContext CreatePreparedModelContext(
            IModel model,
            Writes capture
        ) => new(IntegrationTestDbContextOptions
                .Create<UpsertContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(ConnectionString, Version)
                .UseModel(model)
                .AddInterceptors(capture)
                .Options,
            "supplied-only");
    }

    private sealed class Writes : DbCommandInterceptor
    {
        internal List<string> AllStatements { get; } = [];
        internal List<string> Statements { get; } = [];
        internal List<int> ParameterCounts { get; } = [];
        internal List<int> ServerThreads { get; } = [];
        internal Action<int>? BeforeWrite { get; set; }
        internal Func<int, CancellationToken, ValueTask>? BeforeWriteAsync { get; set; }
        internal Func<int, Task>? AfterWrite { get; set; }

        public override DbCommand CommandInitialized(
            CommandEndEventData eventData,
            DbCommand result
        )
        {
            AllStatements.Add(result.CommandText);

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal))
            {
                Statements.Add(command.CommandText);
                ParameterCounts.Add(command.Parameters.Count);
                ServerThreads.Add(((MySqlConnection)command.Connection!).ServerThread);
                BeforeWrite?.Invoke(Statements.Count);

                if (BeforeWriteAsync is { } beforeWrite)
                {
                    await beforeWrite(Statements.Count, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal)
                && AfterWrite is { } afterWrite)
            {
                await afterWrite(Statements.Count)
                    .ConfigureAwait(false);
            }

            return result;
        }
    }

    private sealed class UpsertContext : DbContext
    {
        private readonly string _mode;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public UpsertContext(
            DbContextOptions<UpsertContext> options,
            string mode
        ) : base(options)
        {
            _mode = mode;
        }

        internal DbSet<UpsertRow> Rows => Set<UpsertRow>();
        internal int ModelBuilds { get; private set; }

        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            ModelBuilds++;

            if (_mode == "supplied-only")
            {
                throw new InvalidOperationException("The supplied runtime model must not be rebuilt.");
            }

            builder.HasCharSet("utf8mb4");
            builder.UseCollation("utf8mb4_unicode_ci");

            if (_mode == "unique-control")
            {
                builder.Ignore<UpsertRow>();
                var unique = builder.Entity<UniqueRow>();
                unique.ToTable("UpsertUnique");
                unique.HasKey(value => value.Id);
                unique
                    .Property(value => value.Id)
                    .ValueGeneratedNever();

                unique
                    .Property(value => value.First)
                    .HasMaxLength(120);

                unique
                    .Property(value => value.Second)
                    .HasMaxLength(120);

                unique
                    .HasIndex(value => value.First)
                    .IsUnique();

                unique
                    .HasIndex(value => value.Second)
                    .IsUnique();

                return;
            }

            if (_mode == "guid-default")
            {
                builder.Ignore<UpsertRow>();
                var generated = builder.Entity<GuidDefaultRow>();
                generated.ToTable("UpsertGuidDefaults");
                generated.HasKey(value => value.Id);
                generated
                    .Property(value => value.Id)
                    .HasColumnType("char(36)")
                    .HasDefaultValueSql("UUID()");

                generated
                    .Property(value => value.NaturalKey)
                    .HasMaxLength(120);

                generated
                    .HasIndex(value => value.NaturalKey)
                    .IsUnique();

                return;
            }

            var row = builder.Entity<UpsertRow>();
            row.ToTable(_mode == "quoted"
                ? "Upsert`Rows"
                : "UpsertRows");

            if (_mode == "composite-safe")
            {
                row.HasKey(value => new
                {
                    value.TenantId,
                    value.Id,
                });
            }
            else
            {
                row.HasKey(value => value.Id);
            }

            if (_mode != "identity")
            {
                row
                    .Property(value => value.Id)
                    .ValueGeneratedNever();
            }

            row
                .Property(value => value.UniqueKey)
                .HasMaxLength(120);

            row
                .HasIndex(value => value.UniqueKey)
                .IsUnique();

            row
                .Property(value => value.Name)
                .IsRequired();

            row
                .Property(value => value.Unchanged)
                .IsRequired();

            row
                .Property(value => value.ServerText)
                .HasDefaultValueSql("'server-default'");

            row
                .Property(value => value.Amount)
                .HasPrecision(18, 4);

            row
                .Property(value => value.Timestamp)
                .HasPrecision(3);

            row
                .Property(value => value.Computed)
                .HasComputedColumnSql("`Number` + 1", stored: true);

            row
                .Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(24);

            row
                .Property(value => value.Json)
                .HasColumnType("json");

            row
                .Property<string>("_onlyField")
                .HasField("_onlyField")
                .HasMaxLength(512)
                .IsRequired();

            if (_mode != "wide")
            {
                foreach (var name in s_wideColumns)
                {
                    row.Ignore(name);
                }
            }

            switch (_mode)
            {
                case "quoted":
                    row
                        .Property(value => value.Name)
                        .HasColumnName("Name`quoted");

                    break;
                case "char-guid":
                    row
                        .Property(value => value.GuidValue)
                        .HasColumnType("char(36)");

                    row
                        .Property(value => value.NullableGuid)
                        .HasColumnType("char(36)");

                    break;
                case "converted-guid":
                    row
                        .Property(value => value.GuidValue)
                        .HasConversion<string>()
                        .HasColumnType("char(36)");

                    row
                        .Property(value => value.NullableGuid)
                        .HasConversion<string>()
                        .HasColumnType("char(36)");

                    break;
                case "uuid":
                    row
                        .Property(value => value.GuidValue)
                        .HasColumnType("uuid");

                    row
                        .Property(value => value.NullableGuid)
                        .HasColumnType("uuid");

                    break;
                case "sentinel":
                    row
                        .Property(value => value.ServerText)
                        .HasSentinel("missing");

                    break;
                case "filter":
                    row.HasQueryFilter(value => value.TenantId == 7 && !value.IsDeleted);
                    break;
                case "throw-converter":
                    row
                        .Property(value => value.Name)
                        .HasConversion(new ValueConverter<string, string>(
                            value => RejectControlledValue(value),
                            value => value));

                    break;
                case "two-unique":
                    row
                        .Property(value => value.Name)
                        .HasMaxLength(160);

                    row
                        .HasIndex(value => value.Name)
                        .IsUnique();

                    break;
                case "prefix":
                    row
                        .HasIndex(value => value.UniqueKey)
                        .HasPrefixLength(8);

                    break;
                case "composite-safe":
                    row
                        .HasIndex(value => value.UniqueKey)
                        .IsUnique(false);

                    row
                        .HasIndex(value => new
                        {
                            value.TenantId,
                            value.UniqueKey,
                        })
                        .IsUnique();

                    break;
                case "tenant-unique-only":
                    row
                        .HasIndex(value => value.UniqueKey)
                        .IsUnique(false);

                    row
                        .HasIndex(value => new
                        {
                            value.TenantId,
                            value.UniqueKey,
                        })
                        .IsUnique();

                    row.HasQueryFilter(value => value.TenantId == 7 && !value.IsDeleted);
                    break;
                case "check":
                    row.ToTable(table => table.HasCheckConstraint("CK_Upsert_Number", "`Number` >= 0"));
                    break;
                case "null-converter":
                    row
                        .Property(value => value.Name)
                        .HasConversion(new ValueConverter<string, string?>(
                            value => value == "force-null"
                                ? null
                                : value,
                            value => value ?? string.Empty));

                    break;
            }
        }
    }

    private sealed class ControlledBindFailure : Exception { }

    private sealed class UniqueRow
    {
        public int Id { get; set; }
        public string First { get; set; } = string.Empty;
        public string Second { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class GuidDefaultRow
    {
        public Guid Id { get; set; }
        public string NaturalKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private enum RowStatus { New, Ready }

    private sealed class UpsertRow
    {
        private string _onlyField = "field-value";

        public int Id { get; set; }
        public string? UniqueKey { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unchanged { get; set; } = "preserved";
        public string? ServerText { get; set; }
        public int Computed { get; set; }
        public int Number { get; set; }
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; } = new(2026, 11, 17);
        public byte[] Payload { get; set; } = [];
        public Guid GuidValue { get; set; }
        public Guid? NullableGuid { get; set; }
        public RowStatus Status { get; set; }
        public bool IsDeleted { get; set; }
        public int TenantId { get; set; }
        public string Json { get; set; } = "{}";
        public string PrivateValue { get; private set; } = "private-value";
        public int C01 { get; set; }
        public int C02 { get; set; }
        public int C03 { get; set; }
        public int C04 { get; set; }
        public int C05 { get; set; }
        public int C06 { get; set; }
        public int C07 { get; set; }
        public int C08 { get; set; }
        public int C09 { get; set; }
        public int C10 { get; set; }
        public int C11 { get; set; }
        public int C12 { get; set; }
        public int C13 { get; set; }
        public int C14 { get; set; }
        public int C15 { get; set; }
        public int C16 { get; set; }
        public int C17 { get; set; }
        public int C18 { get; set; }
        public int C19 { get; set; }
        public int C20 { get; set; }
        public int C21 { get; set; }
        public int C22 { get; set; }
        public int C23 { get; set; }
        public int C24 { get; set; }
        public int C25 { get; set; }
        public int C26 { get; set; }
        public int C27 { get; set; }
        public int C28 { get; set; }
        public int C29 { get; set; }
        public int C30 { get; set; }
        public int C31 { get; set; }
        public int C32 { get; set; }

        internal string FieldValue => _onlyField;

        internal void SetFields(
            string value
        )
        {
            _onlyField = value;
            PrivateValue = value;
        }
    }
}
