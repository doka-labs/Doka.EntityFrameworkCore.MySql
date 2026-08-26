using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated text literals under the SQL modes whose string
/// parsing rules differ across supported MySQL and MariaDB engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "ConfigurationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlSqlModeContractTests
{
    private static readonly string[] s_sqlModes =
    [
        string.Empty,
        "NO_BACKSLASH_ESCAPES",
        "ANSI_QUOTES",
        "ANSI_QUOTES,NO_BACKSLASH_ESCAPES,STRICT_TRANS_TABLES",
    ];

    /// <summary>
    /// Verifies every configured SQL mode against MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MySql97,
                MySqlServerVersion.MySql(new Version(9, 7, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                MySqlServerVersion.MariaDb(new Version(10, 11, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies every configured SQL mode against MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_preserves_text_literals_across_supported_sql_modes()
    {
        await AssertTextLiteralContractAsync(
                IntegrationDatabaseTarget.MariaDb123,
                MySqlServerVersion.MariaDb(new Version(12, 3, 0)))
            .ConfigureAwait(false);
    }

    private static async Task AssertTextLiteralContractAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        const string payload = "path\\segment 'quoted'\nemoji:\U0001F680";
        var connectionString =
            new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetConnectionString(target))
            {
                Pooling = false,
            }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions.Create<SqlModeContractContext>().UseMySql(connection, serverVersion)
                .Options);

        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(string));

        Assert.NotNull(mapping);

        var literal = mapping.GenerateSqlLiteral(payload);
        var expectedHex = Convert.ToHexString(Encoding.UTF8.GetBytes(payload));

        foreach (var sqlMode in s_sqlModes)
        {
            await SetSqlModeAsync(connection, sqlMode)
                .ConfigureAwait(false);
            var expectedSqlMode = await ReadSqlModeAsync(connection)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT HEX({literal})";

            var actualHex = Assert.IsType<string>(
                await command
                    .ExecuteScalarAsync(CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(expectedHex, actualHex);

            await AssertJsonPathLiteralContractAsync(connection, context)
                .ConfigureAwait(false);

            await AssertMigrationLiteralContractAsync(connection, context)
                .ConfigureAwait(false);

            await AssertFailedMigrationRestoresSessionAsync(connection, context, expectedSqlMode)
                .ConfigureAwait(false);

            Assert.Equal(
                expectedSqlMode,
                await ReadSqlModeAsync(connection)
                    .ConfigureAwait(false));
        }

        // Keep a distinct synchronous invocation: the async assertion above cannot
        // qualify MigrationCommand.ExecuteNonQuery and its synchronous cleanup path.
        QualifySynchronousFailedMigrationSessionRestoration(connection, context);
        await AssertCanceledMigrationRestoresSessionAsync(
                connectionString,
                connection,
                context)
            .ConfigureAwait(false);
        await AssertPoolReuseAfterFailureAsync(connectionString, serverVersion)
            .ConfigureAwait(false);
        await AssertCleanupFailureEvictsCallerConnectionAsync(connectionString, serverVersion)
            .ConfigureAwait(false);

        // Keep the synchronous pool-eviction path qualified independently from
        // ExecuteNonQueryAsync and MySqlConnector's asynchronous pool APIs.
        // ReSharper disable once MethodHasAsyncOverload
        QualifySynchronousCleanupFailurePoolEviction(connectionString, serverVersion);
    }

    private static async Task AssertJsonPathLiteralContractAsync(
        MySqlConnection connection,
        DbContext context
    )
    {
        const string json = """{"has\\back":"backslash","has\"quote":"quote","apo'stroph":"apostrophe"}""";
        (string PropertyName, string Expected)[] cases =
        [
            ("has\\back", "backslash"),
            ("has\"quote", "quote"),
            ("apo'stroph", "apostrophe"),
        ];

        var queryGenerator = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create();

        var escapeMethod = queryGenerator
            .GetType()
            .GetMethod(
                "EscapeJsonPathPropertyName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(escapeMethod);

        foreach (var @case in cases)
        {
            var escapedSegment = Assert.IsType<string>(escapeMethod.Invoke(null, [@case.PropertyName]));
            var jsonLiteral = MySqlSqlLiteralGenerator.Generate(json);
            var pathLiteral = MySqlSqlLiteralGenerator.Generate($"$.{escapedSegment}");
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT JSON_UNQUOTE(JSON_EXTRACT({jsonLiteral}, {pathLiteral}));";

            Assert.Equal(
                @case.Expected,
                Assert.IsType<string>(
                    await command
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false)));
        }
    }

    private static async Task AssertMigrationLiteralContractAsync(
        MySqlConnection connection,
        DbContext context
    )
    {
        const string tableComment = "table\\comment 'quoted'";
        const string columnComment = "column\\comment 'quoted'";
        const string alteredTableComment = "altered\\table 'quoted'";
        const string addedColumnComment = "added\\column 'quoted'";
        const string alteredColumnComment = "altered\\column 'quoted'";
        const string insertedValue = "inserted\\value 'quoted'";
        const string updatedValue = "updated\\value 'quoted'";
        var tableName = $"SqlModeLiteral_{Guid.NewGuid():N}";
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var createTable = new CreateTableOperation
        {
            Name = tableName,
            Comment = tableComment,
        };

        createTable.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = false,
                Comment = columnComment,
            });

        try
        {
            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    context,
                    [
                        createTable,
                        new InsertDataOperation
                        {
                            Table = tableName,
                            Columns = ["Value"],
                            ColumnTypes = ["longtext"],
                            Values = new object[,]
                            {
                                { insertedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                tableComment,
                await ReadTableCommentAsync(connection, tableName)
                    .ConfigureAwait(false));
            Assert.Equal(
                columnComment,
                await ReadColumnCommentAsync(connection, tableName, "Value")
                    .ConfigureAwait(false));
            Assert.Equal(
                Convert.ToHexString(Encoding.UTF8.GetBytes(insertedValue)),
                await ReadStoredValueHexAsync(connection, tableName)
                    .ConfigureAwait(false));

            var alterTable = new AlterTableOperation
            {
                Name = tableName,
                Comment = alteredTableComment,
            };

            alterTable.OldTable.Comment = tableComment;
            var addColumn = new AddColumnOperation
            {
                Table = tableName,
                Name = "AdditionalValue",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = true,
                Comment = addedColumnComment,
            };

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    context,
                    [
                        alterTable,
                        addColumn
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                alteredTableComment,
                await ReadTableCommentAsync(connection, tableName)
                    .ConfigureAwait(false));
            Assert.Equal(
                addedColumnComment,
                await ReadColumnCommentAsync(connection, tableName, addColumn.Name)
                    .ConfigureAwait(false));

            var alterColumn = new AlterColumnOperation
            {
                Table = tableName,
                Name = addColumn.Name,
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = true,
                Comment = alteredColumnComment,
            };

            alterColumn.OldColumn.ClrType = typeof(string);
            alterColumn.OldColumn.ColumnType = "longtext";
            alterColumn.OldColumn.IsNullable = true;
            alterColumn.OldColumn.Comment = addedColumnComment;

            await ExecuteOperationsAsync(generator, context.Model, context, [alterColumn])
                .ConfigureAwait(false);

            Assert.Equal(
                alteredColumnComment,
                await ReadColumnCommentAsync(connection, tableName, addColumn.Name)
                    .ConfigureAwait(false));

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    context,
                    [
                        new UpdateDataOperation
                        {
                            Table = tableName,
                            Columns = ["Value"],
                            ColumnTypes = ["longtext"],
                            Values = new object[,]
                            {
                                { updatedValue },
                            },
                            KeyColumns = ["Value"],
                            KeyColumnTypes = ["longtext"],
                            KeyValues = new object[,]
                            {
                                { insertedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            Assert.Equal(
                Convert.ToHexString(Encoding.UTF8.GetBytes(updatedValue)),
                await ReadStoredValueHexAsync(connection, tableName)
                    .ConfigureAwait(false));

            await ExecuteOperationsAsync(
                    generator,
                    context.Model,
                    context,
                    [
                        new DeleteDataOperation
                        {
                            Table = tableName,
                            KeyColumns = ["Value"],
                            KeyColumnTypes = ["longtext"],
                            KeyValues = new object[,]
                            {
                                { updatedValue },
                            },
                        },
                    ])
                .ConfigureAwait(false);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM `{tableName}`;";

            Assert.Equal(
                0L,
                Convert.ToInt64(
                    await countCommand
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = await dropCommand
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExecuteOperationsAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        DbContext context,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var relationalConnection = context.GetService<IRelationalConnection>();

        foreach (var migrationCommand in generator.Generate(operations, model))
        {
            _ = await migrationCommand
                .ExecuteNonQueryAsync(
                    relationalConnection,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertFailedMigrationRestoresSessionAsync(
        MySqlConnection connection,
        DbContext context,
        string expectedSqlMode
    )
    {
        const string callerVariableValue = "caller-owned";
        var missingTable = $"MissingSqlModeTarget_{Guid.NewGuid():N}";
        var connectionId = connection.ServerThread;
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();
        var operation = new AddColumnOperation
        {
            Table = missingTable,
            Name = "Value",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = "path\\segment",
        };

        await using (var setVariable = connection.CreateCommand())
        {
            setVariable.CommandText = "/*! SET @__doka_previous_sql_mode = 'caller-owned' */;";
            _ = await setVariable
                .ExecuteNonQueryAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));
        var exception = await Assert.ThrowsAsync<MySqlException>(async () =>
            await migrationCommand
                .ExecuteNonQueryAsync(
                    relationalConnection,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false));

        Assert.NotEqual(0, exception.Number);
        Assert.Equal(connectionId, connection.ServerThread);
        Assert.Equal(expectedSqlMode, await ReadSqlModeAsync(connection).ConfigureAwait(false));

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT /*! @__doka_previous_sql_mode */, 1;";

        await using var reader = await verify
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(callerVariableValue, reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    private static void QualifySynchronousFailedMigrationSessionRestoration(
        MySqlConnection connection,
        DbContext context
    )
    {
        const string expectedSqlMode = "ANSI_QUOTES,STRICT_TRANS_TABLES";
        using (var setMode = connection.CreateCommand())
        {
            setMode.CommandText = "SET SESSION sql_mode = @sqlMode;";
            setMode.Parameters.AddWithValue("@sqlMode", expectedSqlMode);
            _ = setMode.ExecuteNonQuery();
        }

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();
        var operation = new AddColumnOperation
        {
            Table = $"MissingSyncSqlModeTarget_{Guid.NewGuid():N}",
            Name = "Value",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsNullable = true,
            Comment = "path\\segment",
        };
        var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));

        _ = Assert.Throws<MySqlException>(() =>
            migrationCommand.ExecuteNonQuery(relationalConnection));

        using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT @@SESSION.sql_mode, 1;";

        using var reader = verify.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(expectedSqlMode, reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    private static async Task AssertCanceledMigrationRestoresSessionAsync(
        string connectionString,
        MySqlConnection connection,
        DbContext context
    )
    {
        const string expectedSqlMode = "ANSI_QUOTES,STRICT_TRANS_TABLES";
        var tableName = $"SqlModeCancellation_{Guid.NewGuid():N}";
        await using var blocker = new MySqlConnection(connectionString);
        await using var observer = new MySqlConnection(connectionString);
        await blocker.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await observer.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await SetSqlModeAsync(connection, expectedSqlMode).ConfigureAwait(false);
        await using (var createTable = connection.CreateCommand())
        {
            createTable.CommandText = $"CREATE TABLE `{tableName}` (`Id` int NOT NULL);";
            _ = await createTable.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var lockHeld = false;

        try
        {
            await using (var lockCommand = blocker.CreateCommand())
            {
                lockCommand.CommandText = $"LOCK TABLES `{tableName}` WRITE;";
                _ = await lockCommand.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                lockHeld = true;
            }

            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();
            var operation = new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = "path\\segment",
            };
            var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));
            using var cancellation = new CancellationTokenSource();
            var execution = migrationCommand.ExecuteNonQueryAsync(
                relationalConnection,
                cancellationToken: cancellation.Token);

            await WaitForMetadataLockAsync(observer, connection.ServerThread)
                .ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await execution.ConfigureAwait(false))
                .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Equal(
                expectedSqlMode,
                await ReadSqlModeAsync(connection).ConfigureAwait(false));

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT 1;";
            Assert.Equal(
                1,
                Convert.ToInt32(
                    await verify.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            if (lockHeld)
            {
                await using var unlock = blocker.CreateCommand();
                unlock.CommandText = "UNLOCK TABLES;";
                _ = await unlock.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await using var dropTable = connection.CreateCommand();
            dropTable.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = await dropTable.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task WaitForMetadataLockAsync(
        MySqlConnection observer,
        int connectionId
    )
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT STATE FROM information_schema.PROCESSLIST WHERE ID = @connectionId;";
            command.Parameters.AddWithValue("@connectionId", connectionId);
            var state = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false) as string;

            if (state?.Contains("metadata lock", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None).ConfigureAwait(false);
        }

        throw new TimeoutException("The migration command did not enter the expected metadata-lock wait state.");
    }

    private static async Task AssertPoolReuseAfterFailureAsync(
        string baseConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        const string expectedSqlMode = "ANSI_QUOTES,STRICT_TRANS_TABLES";
        var pooledConnectionString = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = true,
            MaximumPoolSize = 1,
            MinimumPoolSize = 0,
            ConnectionReset = false,
            ConnectionIdleTimeout = 37,
        }.ConnectionString;
        var firstConnectionId = 0;

        await using (var first = new MySqlConnection(pooledConnectionString))
        {
            await MySqlConnection.ClearPoolAsync(first, CancellationToken.None).ConfigureAwait(false);
            await first.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            firstConnectionId = first.ServerThread;
            await SetSqlModeAsync(first, expectedSqlMode).ConfigureAwait(false);
            await using var context = new SqlModeContractContext(
                IntegrationTestDbContextOptions.Create<SqlModeContractContext>()
                    .UseMySql(first, serverVersion)
                    .Options);
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();
            var operation = new AddColumnOperation
            {
                Table = $"MissingPooledSqlModeTarget_{Guid.NewGuid():N}",
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = "path\\segment",
            };
            var command = Assert.Single(generator.Generate([operation], context.Model));

            _ = await Assert.ThrowsAsync<MySqlException>(async () =>
                await command
                    .ExecuteNonQueryAsync(
                        relationalConnection,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(expectedSqlMode, await ReadSqlModeAsync(first).ConfigureAwait(false));
        }

        await using var second = new MySqlConnection(pooledConnectionString);
        await second.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(firstConnectionId, second.ServerThread);
        Assert.Equal(expectedSqlMode, await ReadSqlModeAsync(second).ConfigureAwait(false));

        await MySqlConnection.ClearPoolAsync(second, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task AssertCleanupFailureEvictsCallerConnectionAsync(
        string baseConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        var tableName = $"SqlModeCleanupFailure_{Guid.NewGuid():N}";
        var connectionString = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = true,
            MaximumPoolSize = 1,
            MinimumPoolSize = 0,
            ConnectionReset = false,
        }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await MySqlConnection.ClearPoolAsync(connection, CancellationToken.None).ConfigureAwait(false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var originalConnectionId = connection.ServerThread;

        await using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions.Create<SqlModeContractContext>()
                .AddInterceptors(new RestoreFailureInterceptor())
                .UseMySql(connection, serverVersion)
                .Options);

        try
        {
            await using (var createTable = connection.CreateCommand())
            {
                createTable.CommandText = $"CREATE TABLE `{tableName}` (`Id` int NOT NULL);";
                _ = await createTable.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var operation = new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = "path\\segment",
            };
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();
            var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));

            var exception = await Assert.ThrowsAsync<MySqlMigrationSessionCleanupException>(async () =>
                await migrationCommand
                    .ExecuteNonQueryAsync(
                        relationalConnection,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.Equal(RestoreFailureInterceptor.FailureMessage, exception.InnerException?.Message);
            Assert.Equal(ConnectionState.Closed, connection.State);

            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.NotEqual(originalConnectionId, connection.ServerThread);
        }
        finally
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await using var dropTable = connection.CreateCommand();
            dropTable.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = await dropTable.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

            await MySqlConnection.ClearPoolAsync(connection, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void QualifySynchronousCleanupFailurePoolEviction(
        string baseConnectionString,
        MySqlServerVersion serverVersion
    )
    {
        var tableName = $"SyncSqlModeCleanupFailure_{Guid.NewGuid():N}";
        var connectionString = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = true,
            MaximumPoolSize = 1,
            MinimumPoolSize = 0,
            ConnectionReset = false,
        }.ConnectionString;

        using var connection = new MySqlConnection(connectionString);
        MySqlConnection.ClearPool(connection);
        connection.Open();
        var originalConnectionId = connection.ServerThread;

        using var context = new SqlModeContractContext(
            IntegrationTestDbContextOptions.Create<SqlModeContractContext>()
                .AddInterceptors(new RestoreFailureInterceptor())
                .UseMySql(connection, serverVersion)
                .Options);

        try
        {
            using (var createTable = connection.CreateCommand())
            {
                createTable.CommandText = $"CREATE TABLE `{tableName}` (`Id` int NOT NULL);";
                _ = createTable.ExecuteNonQuery();
            }

            var operation = new AddColumnOperation
            {
                Table = tableName,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "varchar(64)",
                IsNullable = true,
                Comment = "path\\segment",
            };
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();
            var migrationCommand = Assert.Single(generator.Generate([operation], context.Model));

            var exception = Assert.Throws<MySqlMigrationSessionCleanupException>(() =>
                migrationCommand.ExecuteNonQuery(relationalConnection));

            Assert.Equal(RestoreFailureInterceptor.FailureMessage, exception.InnerException?.Message);
            Assert.Equal(ConnectionState.Closed, connection.State);

            connection.Open();

            Assert.NotEqual(originalConnectionId, connection.ServerThread);
        }
        finally
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var dropTable = connection.CreateCommand();
            dropTable.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = dropTable.ExecuteNonQuery();

            MySqlConnection.ClearPool(connection);
        }
    }

    private static async Task<string> ReadTableCommentAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_COMMENT FROM information_schema.TABLES "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;";
        command.Parameters.AddWithValue("@tableName", tableName);

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task<string> ReadColumnCommentAsync(
        MySqlConnection connection,
        string tableName,
        string columnName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;";
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task<string> ReadStoredValueHexAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`Value`) FROM `{tableName}`;";

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

    private static async Task SetSqlModeAsync(
        MySqlConnection connection,
        string sqlMode
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode";
        command.Parameters.AddWithValue("@sqlMode", sqlMode);

        _ = await command
            .ExecuteNonQueryAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadSqlModeAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command
                .ExecuteScalarAsync(CancellationToken.None)
                .ConfigureAwait(false));
    }

    private sealed class SqlModeContractContext : DbContext
    {
        public SqlModeContractContext(
            DbContextOptions<SqlModeContractContext> options
        ) : base(options) { }
    }

    private sealed class RestoreFailureInterceptor : DbCommandInterceptor
    {
        internal const string FailureMessage = "Injected sql_mode restore failure.";

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            ThrowWhenRestoreCommand(command);

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            ThrowWhenRestoreCommand(command);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void ThrowWhenRestoreCommand(
            DbCommand command
        )
        {
            if (command.CommandText.Contains("@__doka_previous_sql_mode", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }
    }
}
