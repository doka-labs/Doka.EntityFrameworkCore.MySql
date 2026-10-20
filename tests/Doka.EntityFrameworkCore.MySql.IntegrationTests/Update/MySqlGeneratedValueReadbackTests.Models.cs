using ConnectorGuidFormat = MySqlConnector.MySqlGuidFormat;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    private const string TableName = "GeneratedReadbackRows";

    private static ReadbackContext<TKey, TMode> CreateContext<TKey, TMode>(
        IntegrationDatabaseTarget target,
        ReadbackCapture? capture = null
    ) where TKey : notnull
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = ConnectorGuidFormat.Binary16,
        }.ConnectionString;

        var builder = IntegrationTestDbContextOptions.Create<ReadbackContext<TKey, TMode>>();
        builder.UseMySql(connectionString, IntegrationTestEnvironment.GetServerVersion(target),
            mysql => mysql.MaxBatchSize(1000));

        if (capture is not null)
        {
            builder.AddInterceptors(capture);
        }

        return new ReadbackContext<TKey, TMode>(builder.Options);
    }

    private static async Task RecreateTableAsync(DbContext context)
    {
        await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
    }

    private static void AddRows<TKey, TMode>(
        ReadbackContext<TKey, TMode> context,
        IEnumerable<GeneratedRow<TKey>> rows
    ) where TKey : notnull
    {
        foreach (var row in rows)
        {
            if (typeof(TMode) == typeof(ShadowCompositeMode))
            {
                // WHY: A required shadow key must exist before EF enters the identity map.
                var entry = context.Entry(row);
                entry.Property("Tenant").CurrentValue = row.Input % 2 == 0 ? "CafE   " : "caf\u00e9  ";
                entry.Property("ShadowId").CurrentValue = Convert.ToInt64(row.Id, CultureInfo.InvariantCulture);
                entry.State = EntityState.Added;
            }
            else
            {
                context.Rows.Add(row);
            }
        }
    }

    private sealed class ReadbackContext<TKey, TMode>(DbContextOptions<ReadbackContext<TKey, TMode>> options)
        : DbContext(options) where TKey : notnull
    {
        internal DbSet<GeneratedRow<TKey>> Rows => Set<GeneratedRow<TKey>>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<GeneratedRow<TKey>>();
            entity.UseStorageEngine("InnoDB");
            entity.ToTable(TableName, table =>
            {
                table.HasCheckConstraint("CK_Readback_Input", "`Input` >= 0");

                if (typeof(TMode) == typeof(DeclaredTriggerMode))
                {
                    table.HasTrigger("ReadbackBeforeInsert");
                }
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Stamp).HasComputedColumnSql("`Input` * 17 + 3", stored: true);
            entity.Property(row => row.IsEven).HasComputedColumnSql("MOD(`Input`, 2) = 0", stored: true);
            entity.Property(row => row.NullableValue).HasMaxLength(32)
                .HasComputedColumnSql(
                    "CASE WHEN MOD(`Input`, 3) = 0 THEN NULL ELSE CONCAT('v', `Input`) END", stored: true);
            entity.Property(row => row.DefaultCode).HasMaxLength(32).HasDefaultValue("server-default");
            entity.Property(row => row.Version).HasComputedColumnSql("`Input` * 31", stored: true)
                .IsConcurrencyToken();

            if (typeof(TKey) == typeof(Guid))
            {
                entity.Property(row => row.Id).HasColumnType("binary(16)");
            }

            if (typeof(TMode) == typeof(CharacterGuidMode))
            {
                entity.Property<Guid>(nameof(GeneratedRow<TKey>.Id))
                    .HasConversion(value => value.ToString(), value => Guid.Parse(value))
                    .HasColumnType("char(36)");
            }

            if (typeof(TKey) == typeof(StoredKey))
            {
                entity.Property<StoredKey>(nameof(GeneratedRow<TKey>.Id))
                    .HasConversion(value => value.Value, value => new StoredKey(value));
            }

            if (typeof(TMode) == typeof(ShadowCompositeMode))
            {
                entity.Ignore(row => row.Id);
                entity.Property<string>("Tenant").HasColumnType("char(32)").HasMaxLength(32)
                    .UseCollation("utf8mb4_unicode_ci");
                entity.Property<long>("ShadowId").ValueGeneratedNever();
                entity.HasKey("Tenant", "ShadowId");
            }

            if (typeof(TMode) == typeof(IdentityMode) || typeof(TMode) == typeof(IdentityZeroMode))
            {
                entity.Property(row => row.Id).ValueGeneratedOnAdd();
            }

            if (typeof(TMode) == typeof(IdentityZeroMode))
            {
                entity.Property(row => row.Id).HasSentinel(-1);
            }
        }
    }

    private sealed class GeneratedRow<TKey> where TKey : notnull
    {
        public TKey Id { get; set; } = default!;
        public int Input { get; set; }
        public long Stamp { get; set; }
        public bool IsEven { get; set; }
        public string? NullableValue { get; set; }
        public string? DefaultCode { get; set; }
        public ulong Version { get; set; }
    }

    private sealed class ReadbackCapture : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal List<string> Commands { get; } = [];
        internal Func<DbCommand, CancellationToken, Task>? BeforeExecution { get; set; }
        internal Func<DbCommand, DbDataReader, CancellationToken, ValueTask<DbDataReader>>? AfterExecution { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (Enabled)
            {
                Commands.Add(command.CommandText);
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Commands.Add(command.CommandText);

                if (BeforeExecution is not null)
                {
                    await BeforeExecution(command, cancellationToken);
                }
            }

            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            return Enabled && AfterExecution is not null
                ? AfterExecution(command, result, cancellationToken)
                : ValueTask.FromResult(result);
        }
    }

    private readonly record struct StoredKey(int Value);
    private sealed class StandardMode { }
    private sealed class CharacterGuidMode { }
    private sealed class ShadowCompositeMode { }
    private sealed class DeclaredTriggerMode { }
    private sealed class IdentityMode { }
    private sealed class IdentityZeroMode { }
}
