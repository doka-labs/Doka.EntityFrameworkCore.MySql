namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies the first real MySQL 8.4 CRUD and migration baseline against a live server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySql84CrudBaselineTests
{
    private const string CrudTableName = "Phase1CrudEntities";
    private const string MigrationProbeTableName = "Phase1MigrationProbe";
    private const string HistoryTableName = "__EFMigrationsHistory";
    private const string BinaryGuidTableName = "Phase2BinaryGuidEntities";
    private const string CharGuidTableName = "Phase2CharGuidEntities";
    private const string JsonTableName = "Phase2JsonEntities";

    /// <summary>
    /// Verifies that the provider baseline can execute migration-style DDL and CRUD against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_crud_smoke_path_and_migration_baseline_succeed()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString));
            var historyRepository = context.GetService<IHistoryRepository>();
            var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();

            await historyRepository
                .CreateIfNotExistsAsync()
                .ConfigureAwait(false);

            var migrationCommands = migrationsSqlGenerator.Generate(
                new MigrationOperation[]
                {
                    new CreateTableOperation
                    {
                        Name = MigrationProbeTableName,
                        Columns =
                        {
                            new AddColumnOperation
                            {
                                Name = "Id",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                IsNullable = false,
                            },
                        },
                    },
                },
                context.Model);

            foreach (var command in migrationCommands)
            {
                // Safe: SQL emitted by IMigrationsSqlGenerator from fixture-controlled operations.
                await context
                    .Database.ExecuteSqlRawAsync(command.CommandText)
                    .ConfigureAwait(false);
            }

            await context
                .Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE `Phase1CrudEntities` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `Name` longtext NOT NULL,
                        CONSTRAINT `PK_Phase1CrudEntities` PRIMARY KEY (`Id`)
                    ) CHARACTER SET utf8mb4;
                    """)
                .ConfigureAwait(false);

            var createdEntity = new MySql84CrudEntity
            {
                Name = "initial",
            };

            context.Entities.Add(createdEntity);
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            Assert.True(createdEntity.Id > 0);

            context.ChangeTracker.Clear();

            var queriedEntity = await context
                .Entities.SingleAsync(entity => entity.Id == createdEntity.Id)
                .ConfigureAwait(false);

            Assert.Equal("initial", queriedEntity.Name);

            queriedEntity.Name = "updated";
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            context.ChangeTracker.Clear();

            var updatedEntity = await context
                .Entities.SingleAsync(entity => entity.Id == createdEntity.Id)
                .ConfigureAwait(false);

            Assert.Equal("updated", updatedEntity.Name);

            context.Entities.Remove(updatedEntity);
            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var remainingCount = await context
                .Entities.CountAsync()
                .ConfigureAwait(false);

            Assert.Equal(0, remainingCount);
            Assert.True(
                await TableExistsAsync(connectionString, HistoryTableName)
                    .ConfigureAwait(false));
            Assert.True(
                await TableExistsAsync(connectionString, MigrationProbeTableName)
                    .ConfigureAwait(false));
            Assert.True(
                await TableExistsAsync(connectionString, CrudTableName)
                    .ConfigureAwait(false));
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that database-existence checks return false only for a missing database and still surface real access failures.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Database_creator_exists_distinguishes_missing_database_from_access_denied()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var missingDatabaseBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            Database = "doka_provider_missing_phase1_exists_check",
        };

        await using var missingDatabaseContext =
            new MySql84CrudContext(CreateOptions(missingDatabaseBuilder.ConnectionString));
        var missingDatabaseCreator = missingDatabaseContext.GetService<IRelationalDatabaseCreator>();

        Assert.False(missingDatabaseCreator.Exists());
        Assert.False(
            await missingDatabaseCreator
                .ExistsAsync()
                .ConfigureAwait(false));

        var invalidCredentialsBuilder = new MySqlConnectionStringBuilder(connectionString)
        {
            Password = "definitely_wrong_password",
        };

        await using var invalidCredentialsContext =
            new MySql84CrudContext(CreateOptions(invalidCredentialsBuilder.ConnectionString));
        var invalidCredentialsCreator = invalidCredentialsContext.GetService<IRelationalDatabaseCreator>();

        var syncException = Assert.Throws<MySqlException>(() => invalidCredentialsCreator.Exists());
        var exception = await Assert
            .ThrowsAsync<MySqlException>(() => invalidCredentialsCreator.ExistsAsync())
            .ConfigureAwait(false);

        Assert.NotEqual(MySqlErrorCode.NoSuchDb, syncException.ErrorCode);
        Assert.NotEqual(MySqlErrorCode.NoSuchDb, exception.ErrorCode);
    }

    /// <summary>
    /// Verifies that the batching baseline stays singular and parameterized for generated-value inserts.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Save_changes_uses_singular_parameterized_insert_commands_for_generated_values()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);
        var interceptor = new CommandCaptureInterceptor();

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString, interceptor));

            await context
                .Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE `Phase1CrudEntities` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `Name` longtext NOT NULL,
                        CONSTRAINT `PK_Phase1CrudEntities` PRIMARY KEY (`Id`)
                    ) CHARACTER SET utf8mb4;
                    """)
                .ConfigureAwait(false);

            context.Entities.AddRange(
                new MySql84CrudEntity
                {
                    Name = "first",
                },
                new MySql84CrudEntity
                {
                    Name = "second",
                });

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            var insertCommands = interceptor
                .Commands.Where(command => command.CommandText.Contains(
                    "INSERT INTO `Phase1CrudEntities`",
                    StringComparison.Ordinal))
                .ToList();

            // With AffectedCountModificationCommandBatch, inserts may be batched into fewer commands.
            Assert.True(insertCommands.Count >= 1, $"Expected at least 1 insert command, got {insertCommands.Count}");
            Assert.All(
                insertCommands,
                command =>
                {
                    Assert.Contains("@", command.CommandText, StringComparison.Ordinal);
                    Assert.DoesNotContain("'first'", command.CommandText, StringComparison.Ordinal);
                    Assert.DoesNotContain("'second'", command.CommandText, StringComparison.Ordinal);
                    Assert.True(command.ParameterCount > 0);
                });
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that Binary16 and Char36 GUID mappings roundtrip together in the same model.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Binary16_and_char36_guid_mappings_roundtrip_together()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new GuidRoundtripContext(CreateGuidRoundtripOptions(connectionString));

            await context
                .Database.ExecuteSqlRawAsync(
                    $"""
                     CREATE TABLE `{BinaryGuidTableName}` (
                         `Id` binary(16) NOT NULL,
                         `Name` longtext NOT NULL,
                         CONSTRAINT `PK_{BinaryGuidTableName}` PRIMARY KEY (`Id`)
                     ) CHARACTER SET utf8mb4;

                     CREATE TABLE `{CharGuidTableName}` (
                         `Id` char(36) NOT NULL,
                         `Name` longtext NOT NULL,
                         CONSTRAINT `PK_{CharGuidTableName}` PRIMARY KEY (`Id`)
                     ) CHARACTER SET utf8mb4;
                     """)
                .ConfigureAwait(false);

            var binaryEntity = new BinaryGuidEntity
            {
                Name = "binary",
            };
            var charEntity = new CharGuidEntity
            {
                Name = "char36",
            };

            context.Add(binaryEntity);
            context.Add(charEntity);

            await context
                .SaveChangesAsync()
                .ConfigureAwait(false);

            Assert.NotEqual(Guid.Empty, binaryEntity.Id);
            Assert.NotEqual(Guid.Empty, charEntity.Id);

            context.ChangeTracker.Clear();

            var loadedBinary = await context
                .BinaryGuidEntities.SingleAsync(entity => entity.Id == binaryEntity.Id)
                .ConfigureAwait(false);
            var loadedChar = await context
                .CharGuidEntities.SingleAsync(entity => entity.Id == charEntity.Id)
                .ConfigureAwait(false);

            Assert.Equal(binaryEntity.Id, loadedBinary.Id);
            Assert.Equal("binary", loadedBinary.Name);
            Assert.Equal(charEntity.Id, loadedChar.Id);
            Assert.Equal("char36", loadedChar.Name);
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies that the provider-generated MySQL 8.4 JSON and generated-column DDL executes successfully.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_json_and_generated_column_baseline_succeeds()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        await ResetDatabaseObjectsAsync(connectionString)
            .ConfigureAwait(false);

        try
        {
            await using var context = new MySql84CrudContext(CreateOptions(connectionString));
            var migrationsSqlGenerator = context.GetService<IMigrationsSqlGenerator>();
            var commands = migrationsSqlGenerator.Generate(
                [
                    new CreateTableOperation
                    {
                        Name = JsonTableName,
                        Columns =
                        {
                            new AddColumnOperation
                            {
                                Name = "Id",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                IsNullable = false,
                            },
                            new AddColumnOperation
                            {
                                Name = "Payload",
                                ClrType = typeof(string),
                                ColumnType = "json",
                                IsNullable = false,
                            },
                            new AddColumnOperation
                            {
                                Name = "StoredCount",
                                ClrType = typeof(int),
                                ColumnType = "int",
                                ComputedColumnSql = "JSON_LENGTH(`Payload`)",
                                IsStored = true,
                                IsNullable = false,
                            },
                        },
                        PrimaryKey = new AddPrimaryKeyOperation
                        {
                            Name = $"PK_{JsonTableName}",
                            Table = JsonTableName,
                            Columns = ["Id"],
                        },
                    },
                ],
                context.Model);

            await using var dbConnection = context.Database.GetDbConnection();

            if (dbConnection.State != ConnectionState.Open)
            {
                await dbConnection
                    .OpenAsync()
                    .ConfigureAwait(false);
            }

            foreach (var command in commands)
            {
                await using var ddlCommand = dbConnection.CreateCommand();
                ddlCommand.CommandText = command.CommandText;
                await ddlCommand
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);
            }

            await using var insertCommand = dbConnection.CreateCommand();
            insertCommand.CommandText = $"INSERT INTO `{JsonTableName}` (`Id`, `Payload`) VALUES (@id, @payload);";

            var idParameter = insertCommand.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = 1;
            insertCommand.Parameters.Add(idParameter);

            var payloadParameter = insertCommand.CreateParameter();
            payloadParameter.ParameterName = "@payload";
            payloadParameter.Value = """{"kind":"alpha","items":[1,2,3]}""";
            insertCommand.Parameters.Add(payloadParameter);

            await insertCommand
                .ExecuteNonQueryAsync()
                .ConfigureAwait(false);

            await using var scalarCommand = dbConnection.CreateCommand();
            scalarCommand.CommandText = $"SELECT `StoredCount` FROM `{JsonTableName}` WHERE `Id` = 1;";

            var result = await scalarCommand
                .ExecuteScalarAsync()
                .ConfigureAwait(false);

            Assert.NotNull(result);
            Assert.Equal(2, Convert.ToInt32(result, CultureInfo.InvariantCulture));
            Assert.True(
                await TableExistsAsync(connectionString, JsonTableName)
                    .ConfigureAwait(false));
        }
        finally
        {
            await ResetDatabaseObjectsAsync(connectionString)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<MySql84CrudContext> CreateOptions(
        string connectionString
    )
    {
        return CreateOptions(connectionString, interceptor: null);
    }

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
        )
        {
            Commands.Add(new CapturedCommand(command.CommandText, command.Parameters.Count));
        }
    }

    private sealed record CapturedCommand(
        string CommandText,
        int ParameterCount
    );
}
