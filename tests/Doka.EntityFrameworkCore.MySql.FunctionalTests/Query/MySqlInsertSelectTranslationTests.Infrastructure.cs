namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>Builds an offline translation scope through the existing transient provider policy.</summary>
    private static InsertContext CreateContext(
        bool mariaDb,
        InsertObservation observation,
        IModel? model = null,
        bool forbidExecutionStrategy = false
    )
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        var builder = MySqlFunctionalTestOptions
            .CreateTransientBuilder<InsertContext>()
            .UseMySql("Server=localhost;Database=insert_translation;User ID=unused;", version)
            .AddInterceptors(observation, new InsertCommandObservation(observation));

        if (model is not null)
        {
            builder.UseModel(model);
        }

        if (forbidExecutionStrategy)
        {
            builder.ReplaceService<IExecutionStrategyFactory, ForbiddenExecutionStrategyFactory>();
        }

        return new InsertContext(builder.Options, mariaDb);
    }

    /// <summary>Stops before actual I/O while retaining the command EF fully initialized before opening.</summary>
    private sealed class InsertObservation : DbConnectionInterceptor
    {
        internal Exception OpenFailure { get; } = new InvalidOperationException("offline-insert-open-boundary");
        internal int OpenAttempts { get; private set; }
        internal List<InsertCommandSnapshot> Commands { get; } = [];

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            OpenAttempts++;

            throw OpenFailure;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            OpenAttempts++;

            throw OpenFailure;
        }
    }

    /// <summary>Observes finalized parameter values before the test-owned connection refusal.</summary>
    private sealed class InsertCommandObservation : DbCommandInterceptor
    {
        private readonly InsertObservation _observation;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public InsertCommandObservation(
            InsertObservation observation
        )
        {
            _observation = observation;
        }

        /// <inheritdoc />
        public override DbCommand CommandInitialized(
            CommandEndEventData eventData,
            DbCommand result
        )
        {
            _observation.Commands.Add(new InsertCommandSnapshot(result.CommandText,
                result
                    .Parameters
                    .Cast<DbParameter>()
                    .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value)));

            return result;
        }
    }

    private sealed record InsertCommandSnapshot(
        string Sql,
        IReadOnlyDictionary<string, object?> Parameters
    );

    /// <summary>Provides actual defaults, generation, converters, ownership and independent query filters.</summary>
    private sealed class InsertContext : DbContext
    {
        private readonly bool _mariaDb;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public InsertContext(
            DbContextOptions<InsertContext> options,
            bool mariaDb
        ) : base(options)
        {
            _mariaDb = mariaDb;
        }

        public int TenantId { get; set; } = 7;
        public DbSet<InsertSource> Sources => Set<InsertSource>();
        public DbSet<InsertTarget> Targets => Set<InsertTarget>();
        public DbSet<InsertLookup> Lookups => Set<InsertLookup>();
        public DbSet<InsertDerivedTarget> DerivedTargets => Set<InsertDerivedTarget>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<InsertSource>(entity =>
            {
                entity.ToTable("insert Source");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(120);

                entity
                    .Property(row => row.TokenBinary)
                    .HasColumnType("binary(16)");

                entity
                    .Property(row => row.TokenText)
                    .HasColumnType("char(36)");

                entity
                    .Property(row => row.Status)
                    .HasConversion<string>();

                entity.HasQueryFilter(row => row.TenantId == TenantId);
                entity
                    .HasOne(row => row.Lookup)
                    .WithMany(row => row.Rows)
                    .HasForeignKey(row => row.LookupId);

                if (_mariaDb)
                {
                    entity
                        .Property(row => row.TokenNative)
                        .HasColumnType("uuid");
                }
                else
                {
                    entity.Ignore(row => row.TokenNative);
                }
            });

            modelBuilder.Entity<InsertLookup>(entity =>
            {
                entity.ToTable("insert Lookups");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Name)
                    .HasMaxLength(120);

                entity.HasQueryFilter(row => row.Visible);
            });

            modelBuilder.Entity<InsertTarget>(entity =>
            {
                entity.ToTable("insert`Target");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .UseMySqlAutoIncrementColumn();

                entity
                    .Property(row => row.Name)
                    .HasColumnName("Data`Name")
                    .HasMaxLength(120);

                entity
                    .Property(row => row.Value)
                    .HasColumnName("payload value");

                entity
                    .Property(row => row.DefaultText)
                    .HasDefaultValue("server-default")
                    .HasMaxLength(120);

                entity
                    .Property(row => row.ComputedLength)
                    .HasComputedColumnSql("CHAR_LENGTH(`Data``Name`)");

                entity
                    .Property(row => row.TokenBinary)
                    .HasColumnType("binary(16)")
                    .HasDefaultValueSql("UNHEX(REPLACE(UUID(), '-', ''))");

                entity
                    .Property(row => row.TokenText)
                    .HasColumnType("char(36)")
                    .HasDefaultValueSql("UUID()");

                entity
                    .Property(row => row.Status)
                    .HasConversion<string>()
                    .HasDefaultValue(InsertStatus.Pending);

                entity
                    .Property(row => row.OtherStatus)
                    .HasConversion<int>()
                    .HasDefaultValue(InsertStatus.Pending);

                entity
                    .Property<int>("ShadowCode")
                    .HasDefaultValue(1);

                entity
                    .Property<string>("_secret")
                    .HasColumnName("secret payload")
                    .HasDefaultValue("field-default")
                    .HasMaxLength(120);

                entity.HasQueryFilter(row => row.Name != "target-filter-must-not-authorize-insert");

                if (_mariaDb)
                {
                    entity
                        .Property(row => row.TokenNative)
                        .HasColumnType("uuid")
                        .HasDefaultValueSql("UUID()");
                }
                else
                {
                    entity.Ignore(row => row.TokenNative);
                }
            });

            modelBuilder.Entity<InsertBaseTarget>(entity =>
            {
                entity.ToTable("InsertHierarchy");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Id)
                    .UseMySqlAutoIncrementColumn();

                entity
                    .Property(row => row.Name)
                    .HasMaxLength(120);

                entity
                    .HasDiscriminator<string>("Kind")
                    .HasValue<InsertBaseTarget>("base")
                    .HasValue<InsertDerivedTarget>("derived");
            });
        }
    }

    private enum InsertStatus { Pending, Active }

    private sealed class InsertSource
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int Value { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid TokenBinary { get; set; }
        public Guid TokenText { get; set; }
        public Guid TokenNative { get; set; }
        public InsertStatus Status { get; set; }
        public int LookupId { get; set; }
        public InsertLookup Lookup { get; set; } = null!;
    }

    private sealed class InsertLookup
    {
        public int Id { get; set; }
        public bool Visible { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<InsertSource> Rows { get; set; } = [];
    }

    private sealed class InsertTarget
    {
        private string _secret = string.Empty;

        public InsertTarget()
        {
            throw new InvalidOperationException("INSERT SELECT must never invoke the target constructor.");
        }

        public int Id { get; set; }
        public int Value { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? DefaultText { get; set; }
        public int ComputedLength { get; set; }
        public Guid TokenBinary { get; set; }
        public Guid TokenText { get; set; }
        public Guid TokenNative { get; set; }
        public InsertStatus Status { get; set; }
        public InsertStatus OtherStatus { get; set; }
    }

    private class InsertBaseTarget
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class InsertDerivedTarget : InsertBaseTarget
    {
        public int Detail { get; set; }
    }
}
