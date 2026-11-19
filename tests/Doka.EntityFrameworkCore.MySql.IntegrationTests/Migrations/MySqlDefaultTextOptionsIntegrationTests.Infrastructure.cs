namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlDefaultTextOptionsIntegrationTests
{
    private const string UnicodeCollation = "utf8mb4_unicode_ci";
    private const string BinaryCollation = "utf8mb4_bin";
    private const string MainTable = "DefaultTextRecords";
    private const string OverrideTable = "DefaultTextOverrides";
    private static readonly Guid s_externalId = new("0a7c04fe-1dde-4bd4-92ca-f306942b8c48");

    /// <summary>Parses only the repository's explicitly selected supported engine cases.</summary>
    private static IntegrationDatabaseTarget ParseTarget(
        string target
    ) => Enum.Parse<IntegrationDatabaseTarget>(target);

    /// <summary>Creates a fresh provider graph while borrowing the fixture's open connection.</summary>
    private static TextOptionsContext CreateContext(
        TextDatabase database,
        TextSchema? schema = null,
        string collation = UnicodeCollation,
        DbCommandInterceptor? interceptor = null
    )
    {
        var options = IntegrationTestDbContextOptions
            .Create<TextOptionsContext>()
            .EnableServiceProviderCaching(false)
            .UseMySql(
                database.Connection,
                IntegrationTestEnvironment.GetServerVersion(database.Target),
                provider => provider
                    .DefaultGuidFormat(MySqlGuidFormat.Char36)
                    .DefaultCharSet("utf8mb4")
                    .DefaultCollation(collation));

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new TextOptionsContext(options.Options, schema ?? new TextSchema());
    }

    /// <summary>Generates and executes real migration operations with their recorded model metadata.</summary>
    private static async Task ApplyModelAsync(
        TextOptionsContext target,
        TextOptionsContext? source,
        CancellationToken cancellationToken
    )
    {
        var model = target.GetService<IDesignTimeModel>()
            .Model;

        var sourceModel = source
            ?.GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(sourceModel, model.GetRelationalModel());

        await ExecuteOperationsAsync(target, operations, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Executes captured historical operations without filling them from current defaults.</summary>
    private static async Task ExecuteOperationsAsync(
        TextOptionsContext context,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        var connection = context.GetService<IRelationalConnection>();

        foreach (var command in context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model))
        {
            _ = await command
                .ExecuteNonQueryAsync(connection, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reads one current schema fact using a bound table/column name.</summary>
    private static async Task<string?> ReadColumnFactAsync(
        MySqlConnection connection,
        string table,
        string column,
        bool charset,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = charset
            ? "SELECT CHARACTER_SET_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column;"
            : "SELECT COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column;";

        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var value = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Executes controlled fixture SQL; application values use provider parameter binding elsewhere.</summary>
    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads a fixture-owned scalar query without converting unknown defaults into model facts.</summary>
    private static async Task<string> ReadScalarAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Reads the native table definition before or after a schema transition.</summary>
    private static async Task<string> ReadCreateTableAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SHOW CREATE TABLE `{tableName}`;";
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException("The fixture table has no native schema definition.");
        }

        return reader.GetString(1);
    }

    /// <summary>Reads canonical physical column facts independently of optional SHOW CREATE spelling.</summary>
    private static async Task<string[]> ReadPhysicalColumnDefinitionsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT CONCAT(COLUMN_NAME, '|', COLUMN_TYPE, '|', IS_NULLABLE, '|',
                      COALESCE(COLUMN_DEFAULT, '<NULL>'), '|',
                      COALESCE(CHARACTER_SET_NAME, '<NULL>'), '|',
                      COALESCE(COLLATION_NAME, '<NULL>'), '|', COLUMN_KEY, '|', EXTRA)
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
        ORDER BY ORDINAL_POSITION;
        """;

        command.Parameters.AddWithValue("@table", tableName);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var definitions = new List<string>();

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            definitions.Add(reader.GetString(0));
        }

        return definitions.ToArray();
    }

    /// <summary>Inspects actual database metadata through the provider's production loader.</summary>
    private static DatabaseModel ReverseEngineer(
        TextDatabase database,
        IServiceProvider services
    ) => services
        .GetRequiredService<IDatabaseModelFactory>()
        .Create(
            database.Connection.ConnectionString,
            new DatabaseModelFactoryOptions([MainTable, OverrideTable], []));

    private sealed record TextSchema
    {
        public string? TableCollation { get; init; }

        public bool ExplicitColumnCollation { get; init; }

        public bool GuidColumn { get; init; }
    }

    /// <summary>Models normal text, explicit overrides and the existing JSON/GUID/binary contracts.</summary>
    private sealed class TextOptionsContext : DbContext
    {
        private readonly TextSchema _schema;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public TextOptionsContext(
            DbContextOptions<TextOptionsContext> options,
            TextSchema schema
        ) : base(options)
        {
            _schema = schema;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var row = modelBuilder.Entity<TextRow>();
            row.ToTable(MainTable);
            row.HasKey(entity => entity.Id);
            row
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            row
                .Property(entity => entity.Name)
                .HasMaxLength(64);

            row
                .HasIndex(entity => entity.Name)
                .HasDatabaseName("IX_DefaultText_Name");

            row
                .Property(entity => entity.ExternalId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

            row
                .Property(entity => entity.Document)
                .HasColumnType("json");

            row
                .Property(entity => entity.Json)
                .HasColumnType("longtext");

            // WHY: Use the supported BLOB contract for variable payloads, independently of the connector's 16-byte GUID inference.
            row
                .Property(entity => entity.Bytes)
                .HasColumnType("longblob");

            if (_schema.TableCollation is not null)
            {
                row.Metadata.SetAnnotation(RelationalAnnotationNames.Collation, _schema.TableCollation);
            }

            if (_schema.ExplicitColumnCollation)
            {
                var property = _schema.GuidColumn
                    ? row.Property(entity => entity.ExternalId)
                        .Metadata
                    : row.Property(entity => entity.Name)
                        .Metadata;

                property.SetCollation(UnicodeCollation);
            }

            var explicitTable = modelBuilder.Entity<OverrideRow>();
            explicitTable.ToTable(OverrideTable);
            explicitTable.HasKey(entity => entity.Id);
            explicitTable
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            explicitTable.Metadata.SetAnnotation(RelationalAnnotationNames.Collation, BinaryCollation);
            explicitTable
                .Property(entity => entity.Name)
                .HasMaxLength(64);

            explicitTable
                .Property(entity => entity.ExternalText)
                .HasMaxLength(64)
                .UseCollation("latin1_swedish_ci");
        }
    }

    private sealed class TextRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Guid ExternalId { get; set; }

        public string Document { get; set; } = "{}";

        public string Json { get; set; } = string.Empty;

        public byte[] Bytes { get; set; } = [];
    }

    private sealed class OverrideRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ExternalText { get; set; } = string.Empty;
    }

    /// <summary>Owns an isolated database so ALTER DATABASE tests cannot affect another fixture or parallel case.</summary>
    private sealed class TextDatabase : IAsyncDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly string _databaseName;

        private TextDatabase(
            IntegrationDatabaseTarget target,
            string databaseName,
            MySqlConnection admin,
            MySqlConnection connection
        )
        {
            Target = target;
            _databaseName = databaseName;
            _admin = admin;
            Connection = connection;
        }

        public IntegrationDatabaseTarget Target { get; }

        public MySqlConnection Connection { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // WHY: Framework disposal is uncancellable so a canceled test still drops its private schema.
            await Connection
                .DisposeAsync()
                .ConfigureAwait(false);

            try
            {
                await ExecuteAsync(_admin, $"DROP DATABASE IF EXISTS `{_databaseName}`;", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await _admin
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Creates a deliberately different database default before the provider migration runs.</summary>
        public static async Task<TextDatabase> CreateAsync(
            IntegrationDatabaseTarget target,
            CancellationToken cancellationToken
        )
        {
            var name = "DokaTextDefaults_"
                + Guid
                    .NewGuid()
                    .ToString("N", CultureInfo.InvariantCulture);

            var builder = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(target))
            {
                Database = string.Empty,
                Pooling = false,

                // WHY: Doka's wire contract stays Binary16; the EF mapping converts Char36 properties independently.
                GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            };

            var admin = new MySqlConnection(builder.ConnectionString);
            MySqlConnection? connection = null;
            var created = false;

            try
            {
                await admin
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteAsync(admin,
                        $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;",
                        cancellationToken)
                    .ConfigureAwait(false);

                created = true;
                builder.Database = name;
                connection = new MySqlConnection(builder.ConnectionString);
                await connection
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                return new TextDatabase(target, name, admin, connection);
            }
            catch
            {
                // WHY: Setup failure must not leave a database or borrowed server resources behind after cancellation.
                if (connection is not null)
                {
                    await connection
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }

                if (created)
                {
                    await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS `{name}`;", CancellationToken.None)
                        .ConfigureAwait(false);
                }

                await admin
                    .DisposeAsync()
                    .ConfigureAwait(false);

                throw;
            }
        }
    }

    /// <summary>Records the one query under test without observing fixture setup/scaffolding traffic.</summary>
    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken
        )
        {
            Statements.Add(command.CommandText);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
