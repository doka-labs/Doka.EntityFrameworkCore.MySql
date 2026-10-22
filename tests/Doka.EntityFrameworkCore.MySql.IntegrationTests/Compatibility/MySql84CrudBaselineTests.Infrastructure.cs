namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySql84CrudBaselineTests
{
    private static DbContextOptions<MySql84CrudContext> CreateOptions(
        string connectionString
    ) => CreateOptions(connectionString, interceptor: null);

    private static DbContextOptions<MySql84CrudContext> CreateOptions(
        string connectionString,
        DbCommandInterceptor? interceptor
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<MySql84CrudContext>();

        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }

    private static async Task ResetDatabaseObjectsAsync(
        string connectionString
    )
    {
        await IntegrationDatabaseUtilities
            .EnsureDatabaseExistsAsync(connectionString)
            .ConfigureAwait(false);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               DROP TABLE IF EXISTS `{CrudTableName}`;
                               DROP TABLE IF EXISTS `{MigrationProbeTableName}`;
                               DROP TABLE IF EXISTS `{HistoryTableName}`;
                               DROP TABLE IF EXISTS `{BinaryGuidTableName}`;
                               DROP TABLE IF EXISTS `{CharGuidTableName}`;
                               DROP TABLE IF EXISTS `{JsonTableName}`;
                               """;

        await command
            .ExecuteNonQueryAsync()
            .ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(
        string connectionString,
        string tableName
    )
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT CASE
                                  WHEN EXISTS (
                                      SELECT 1
                                      FROM information_schema.tables
                                      WHERE table_schema = DATABASE()
                                        AND table_name = @tableName
                                  ) THEN 1
                                  ELSE 0
                              END;
                              """;
        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command
            .ExecuteScalarAsync()
            .ConfigureAwait(false);

        return result switch
        {
            bool boolValue => boolValue,
            sbyte sbyteValue => sbyteValue != 0,
            byte byteValue => byteValue != 0,
            short shortValue => shortValue != 0,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            decimal decimalValue => decimalValue != 0,
            _ => false,
        };
    }

    private sealed class MySql84CrudContext : DbContext
    {
        public MySql84CrudContext(
            DbContextOptions<MySql84CrudContext> options
        ) : base(options) { }

        public DbSet<MySql84CrudEntity> Entities => Set<MySql84CrudEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<MySql84CrudEntity>(entity =>
            {
                entity.ToTable(CrudTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedOnAdd();
                entity
                    .Property(item => item.Name)
                    .IsRequired();
            });
        }
    }

    private sealed class MySql84CrudEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private static DbContextOptions<GuidRoundtripContext> CreateGuidRoundtripOptions(
        string connectionString
    )
    {
        var builder = IntegrationTestDbContextOptions.Create<GuidRoundtripContext>();

        builder.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private sealed class GuidRoundtripContext : DbContext
    {
        public GuidRoundtripContext(
            DbContextOptions<GuidRoundtripContext> options
        ) : base(options) { }

        public DbSet<BinaryGuidEntity> BinaryGuidEntities => Set<BinaryGuidEntity>();

        public DbSet<CharGuidEntity> CharGuidEntities => Set<CharGuidEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<BinaryGuidEntity>(entity =>
            {
                entity.ToTable(BinaryGuidTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .UseMySqlClientGuidValueGeneration();
                entity
                    .Property(item => item.Name)
                    .IsRequired();
            });

            modelBuilder.Entity<CharGuidEntity>(entity =>
            {
                entity.ToTable(CharGuidTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36)
                    .UseMySqlClientGuidValueGeneration();
                entity
                    .Property(item => item.Name)
                    .IsRequired();
            });
        }
    }

    private sealed class BinaryGuidEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CharGuidEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = new();

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Capture(command);

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Capture(command);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Capture(command);

            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Capture(command);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Capture(
            DbCommand command
        ) => Commands.Add(new CapturedCommand(command.CommandText, command.Parameters.Count));
    }

    private sealed record CapturedCommand(
        string CommandText,
        int ParameterCount
    );
}
