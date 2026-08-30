namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Executes provider-generated column defaults against every supported server.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlColumnDefaultIntegrationTests
{
    private const string GuidDefaultHex = "0198BFE2557370008000000000000001";
    private static readonly Guid s_guidDefault = Guid.Parse("0198bfe2-5573-7000-8000-000000000001");
    private static readonly TimeSpan s_maximumTime = TimeSpan.FromHours(838)
        + TimeSpan.FromMinutes(59)
        + TimeSpan.FromSeconds(59);

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MySql84,
                MySqlServerVersion.MySql(new Version(8, 4, 11)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public async Task MySql97_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MySql97,
                MySqlServerVersion.MySql(new Version(9, 7, 2)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public async Task MariaDb1011_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb1011,
                MySqlServerVersion.MariaDb(new Version(10, 11, 18)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public async Task MariaDb114_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb114,
                MySqlServerVersion.MariaDb(new Version(11, 4, 12)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb118,
                MySqlServerVersion.MariaDb(new Version(11, 8, 8)))
            .ConfigureAwait(false);
    }

    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public async Task MariaDb123_executes_column_default_matrix()
    {
        await AssertColumnDefaultMatrixAsync(
                IntegrationDatabaseTarget.MariaDb123,
                MySqlServerVersion.MariaDb(new Version(12, 3, 2)))
            .ConfigureAwait(false);
    }

    private static async Task AssertColumnDefaultMatrixAsync(
        IntegrationDatabaseTarget target,
        MySqlServerVersion serverVersion
    )
    {
        var tableName = $"ColumnDefaults_{Guid.NewGuid():N}";
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var context = new ColumnDefaultContext(
            IntegrationTestDbContextOptions.Create<ColumnDefaultContext>()
                .UseMySql(connection, serverVersion)
                .Options);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var relationalConnection = context.GetService<IRelationalConnection>();

        try
        {
            var createTable = CreateTableOperation(tableName);
            await ExecuteAsync(generator, context.Model, relationalConnection, [createTable])
                .ConfigureAwait(false);

            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Id`) VALUES (1);")
                .ConfigureAwait(false);
            await AssertInitialDefaultsAsync(connection, tableName)
                .ConfigureAwait(false);
            await AssertJsonMetadataAsync(connection, tableName)
                .ConfigureAwait(false);
            await AssertGuidDefaultMetadataAsync(connection, tableName, serverVersion)
                .ConfigureAwait(false);

            var addedDate = CreateColumn(
                tableName,
                "AddedDate",
                typeof(DateOnly),
                "date",
                new DateOnly(2027, 1, 2));

            var addedTime = CreateColumn(
                tableName,
                "AddedTime",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(7_654_321)));

            await ExecuteAsync(generator, context.Model, relationalConnection, [addedDate, addedTime])
                .ConfigureAwait(false);
            await AssertAddedDefaultsAsync(connection, tableName, 1, "03:04:05.765432")
                .ConfigureAwait(false);

            var alterTime = new AlterColumnOperation
            {
                Table = tableName,
                Name = addedTime.Name,
                ClrType = typeof(TimeOnly),
                ColumnType = "time(6)",
                IsNullable = false,
                DefaultValue = new TimeOnly(23, 59, 58).Add(TimeSpan.FromTicks(1_234_567)),
                OldColumn =
                {
                    ClrType = typeof(TimeOnly),
                    ColumnType = "time(6)",
                    IsNullable = false,
                    DefaultValue = addedTime.DefaultValue,
                },
            };

            await ExecuteAsync(generator, context.Model, relationalConnection, [alterTime])
                .ConfigureAwait(false);
            await ExecuteRawAsync(connection, $"INSERT INTO `{tableName}` (`Id`) VALUES (2);")
                .ConfigureAwait(false);

            await AssertAddedDefaultsAsync(connection, tableName, 1, "03:04:05.765432")
                .ConfigureAwait(false);
            await AssertAddedDefaultsAsync(connection, tableName, 2, "23:59:58.123456")
                .ConfigureAwait(false);
            await AssertNullableTemporalRepairAsync(
                    generator,
                    context.Model,
                    relationalConnection,
                    connection,
                    tableName)
                .ConfigureAwait(false);
            await AssertConstrainedNullableRepairAsync(
                    generator,
                    context.Model,
                    relationalConnection,
                    connection,
                    tableName)
                .ConfigureAwait(false);

            if (!serverVersion.IsMariaDb)
            {
                await AssertMySqlRejectsUnparenthesizedTemporalDefaultsAsync(connection)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{tableName}`;")
                .ConfigureAwait(false);
        }

        await AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(connection, serverVersion, useDatabaseFacade: true)
            .ConfigureAwait(false);
        await AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
                connection,
                serverVersion,
                useDatabaseFacade: false)
            .ConfigureAwait(false);
    }

    private static async Task AssertFailedRuntimeMigrationDoesNotAdvanceHistoryAsync(
        MySqlConnection connection,
        MySqlServerVersion serverVersion,
        bool useDatabaseFacade
    )
    {
        await DropTimestampRepairMigrationObjectsAsync(connection)
            .ConfigureAwait(false);

        try
        {
            await using var context = new TimestampRepairMigrationContext(
                IntegrationTestDbContextOptions
                    .Create<TimestampRepairMigrationContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options
                            .MigrationsAssembly(typeof(TimestampRepairMigrationContext).Assembly.FullName!)
                            .MigrationsHistoryTable(TimestampRepairMigrationContract.HistoryTable))
                    .Options);

            var exception = useDatabaseFacade
                ? await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    context.Database.MigrateAsync(CancellationToken.None))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => context
                    .GetService<IMigrator>()
                    .MigrateAsync(cancellationToken: CancellationToken.None));

            Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TimestampRepairMigrationContract.Table, exception.Message, StringComparison.Ordinal);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT GROUP_CONCAT(`MigrationId` ORDER BY `MigrationId`) "
                + $"FROM `{TimestampRepairMigrationContract.HistoryTable}`;";

            Assert.Equal(
                TimestampRepairMigrationContract.InitialMigration,
                Convert.ToString(
                    await command
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));

            command.CommandText = "SELECT COUNT(*) "
                + $"FROM `{TimestampRepairMigrationContract.Table}` "
                + "WHERE `OccurredAt` IS NULL;";

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await command
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            await DropTimestampRepairMigrationObjectsAsync(connection)
                .ConfigureAwait(false);
        }
    }

    private static async Task DropTimestampRepairMigrationObjectsAsync(
        MySqlConnection connection
    )
    {
        await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.Table}`;")
            .ConfigureAwait(false);
        await ExecuteRawAsync(connection, $"DROP TABLE IF EXISTS `{TimestampRepairMigrationContract.HistoryTable}`;")
            .ConfigureAwait(false);
    }

    private static CreateTableOperation CreateTableOperation(
        string tableName
    )
    {
        var operation = new CreateTableOperation { Name = tableName };
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "Id",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = false,
            });
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "DateValue",
                typeof(DateOnly),
                "date",
                new DateOnly(2026, 8, 17)));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "TimeValue",
                typeof(TimeOnly),
                "time(6)",
                new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1_234_567))));
        operation.Columns.Add(
            CreateColumn(tableName, "CharValue", typeof(char), "char(1)", '\\'));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "DurationValue",
                typeof(TimeSpan),
                "time(6)",
                TimeSpan.FromHours(27) + TimeSpan.FromTicks(1_234_567)));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "MaximumDurationValue",
                typeof(TimeSpan),
                "time(6)",
                s_maximumTime));
        operation.Columns.Add(
            CreateColumn(
                tableName,
                "MinimumDurationValue",
                typeof(TimeSpan),
                "time(6)",
                -s_maximumTime));
        var jsonColumn = CreateColumn(tableName, "JsonValue", typeof(string), "json", "{}");
        jsonColumn.Comment = "json\\comment";
        jsonColumn.SetAnnotation(MySqlAnnotationNames.Invisible, true);
        operation.Columns.Add(jsonColumn);
        operation.Columns.Add(
            CreateColumn(tableName, "GuidValue", typeof(Guid), "binary(16)", s_guidDefault));
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairDate",
                ClrType = typeof(DateOnly),
                ColumnType = "date",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairDateTime",
                ClrType = typeof(DateTime),
                ColumnType = "datetime(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairTimestamp",
                ClrType = typeof(DateTime),
                ColumnType = "timestamp(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "InvalidRepairTimestamp",
                ClrType = typeof(DateTime),
                ColumnType = "timestamp(6)",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairJson",
                ClrType = typeof(string),
                ColumnType = "json",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairSpatial",
                ClrType = typeof(byte[]),
                ColumnType = "point",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairEnum",
                ClrType = typeof(string),
                ColumnType = "enum('alpha','beta')",
                IsNullable = true,
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Table = tableName,
                Name = "RepairChecked",
                ClrType = typeof(int),
                ColumnType = "int",
                IsNullable = true,
            });
        operation.CheckConstraints.Add(
            new AddCheckConstraintOperation
            {
                Name = $"CK_{tableName}_RepairChecked",
                Table = tableName,
                Sql = "`RepairChecked` > 0",
            });
        var spatialColumn = new AddColumnOperation
        {
            Table = tableName,
            Name = "SpatialValue",
            ClrType = typeof(byte[]),
            ColumnType = "point",
            IsNullable = false,
            DefaultValueSql = "ST_GeomFromText('POINT(1 2)', 4326)",
            Comment = "spatial metadata",
        };

        spatialColumn.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, 4326);
        spatialColumn.SetAnnotation(MySqlAnnotationNames.Invisible, true);
        operation.Columns.Add(spatialColumn);

        return operation;
    }

    private static AddColumnOperation CreateColumn(
        string tableName,
        string name,
        Type clrType,
        string columnType,
        object defaultValue
    )
    {
        return new AddColumnOperation
        {
            Table = tableName,
            Name = name,
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
        };
    }

    private static async Task ExecuteAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        foreach (var command in generator.Generate(operations, model))
        {
            _ = await command
                .ExecuteNonQueryAsync(
                    connection,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExecuteRawAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task AssertInitialDefaultsAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATE_FORMAT(`DateValue`, '%Y-%m-%d'), "
            + "TIME_FORMAT(`TimeValue`, '%H:%i:%s.%f'), HEX(`CharValue`), "
            + "TIME_FORMAT(`DurationValue`, '%H:%i:%s.%f'), "
            + "CAST(`MaximumDurationValue` AS CHAR), CAST(`MinimumDurationValue` AS CHAR), "
            + "JSON_LENGTH(`JsonValue`), HEX(`GuidValue`), "
            + "ST_X(`SpatialValue`), ST_Y(`SpatialValue`), ST_SRID(`SpatialValue`) "
            + $"FROM `{tableName}` WHERE `Id` = 1;";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("2026-08-17", reader.GetString(0));
        Assert.Equal("12:34:56.123456", reader.GetString(1));
        Assert.Equal("5C", reader.GetString(2));
        Assert.Equal("27:00:00.123456", reader.GetString(3));
        Assert.Equal("838:59:59.000000", reader.GetString(4));
        Assert.Equal("-838:59:59.000000", reader.GetString(5));
        Assert.Equal(0L, reader.GetInt64(6));
        Assert.Equal(GuidDefaultHex, reader.GetString(7));
        Assert.Equal(1D, reader.GetDouble(8));
        Assert.Equal(2D, reader.GetDouble(9));
        Assert.Equal(4326L, reader.GetInt64(10));
    }

    private static async Task AssertJsonMetadataAsync(
        MySqlConnection connection,
        string tableName
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_COMMENT, EXTRA FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = @tableName "
            + "AND COLUMN_NAME = 'JsonValue';";
        command.Parameters.AddWithValue("@tableName", tableName);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("json\\comment", reader.GetString(0));
        Assert.Contains("INVISIBLE", reader.GetString(1), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertGuidDefaultMetadataAsync(
        MySqlConnection connection,
        string tableName,
        MySqlServerVersion serverVersion
    )
    {
        await using var catalogCommand = connection.CreateCommand();
        catalogCommand.CommandText = "SELECT HEX(COLUMN_DEFAULT), OCTET_LENGTH(COLUMN_DEFAULT) "
            + "FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + "AND TABLE_NAME = @tableName "
            + "AND COLUMN_NAME = 'GuidValue';";
        catalogCommand.Parameters.AddWithValue("@tableName", tableName);

        await using (var reader = await catalogCommand.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));

            var catalogDefaultHex = reader.GetString(0);
            var catalogLength = reader.GetInt64(1);

            if (!serverVersion.IsMariaDb)
            {
                Assert.Equal(
                    "30783031393842464532353537333730",
                    catalogDefaultHex,
                    ignoreCase: true);
                Assert.Equal(16L, catalogLength);
            }
            else if (serverVersion.Version >= new Version(11, 8))
            {
                var expectedCatalogText = $"x'{GuidDefaultHex.ToLowerInvariant()}'";
                var expectedCatalogHex = Convert.ToHexString(
                    expectedCatalogText.Select(static character => (byte)character).ToArray());

                Assert.Equal(expectedCatalogHex, catalogDefaultHex, ignoreCase: true);
                Assert.Equal(expectedCatalogText.Length, catalogLength);
            }
            else
            {
                var completeTextHex = Convert.ToHexString(
                    GuidDefaultHex
                        .ToLowerInvariant()
                        .Select(static character => (byte)character)
                        .ToArray());

                Assert.DoesNotContain(GuidDefaultHex, catalogDefaultHex, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(completeTextHex, catalogDefaultHex, StringComparison.OrdinalIgnoreCase);
                Assert.True(catalogLength < 35L);
            }
        }

        await using var showCreateCommand = connection.CreateCommand();
        showCreateCommand.CommandText = $"SHOW CREATE TABLE `{tableName}`;";
        await using var showCreateReader = await showCreateCommand
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(await showCreateReader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        var createTableSql = showCreateReader.GetString(1);

        if (serverVersion.IsMariaDb
            && serverVersion.Version < new Version(11, 8))
        {
            Assert.DoesNotContain(GuidDefaultHex, createTableSql, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(GuidDefaultHex, createTableSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task AssertAddedDefaultsAsync(
        MySqlConnection connection,
        string tableName,
        int id,
        string expectedTime
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATE_FORMAT(`AddedDate`, '%Y-%m-%d'), "
            + $"TIME_FORMAT(`AddedTime`, '%H:%i:%s.%f') FROM `{tableName}` WHERE `Id` = @id;";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.True(await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("2027-01-02", reader.GetString(0));
        Assert.Equal(expectedTime, reader.GetString(1));
    }

    private static async Task AssertNullableTemporalRepairAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection relationalConnection,
        MySqlConnection connection,
        string tableName
    )
    {
        var timestampRepair = CreateRequiredColumnRepair<DateTime>(
            tableName,
            "RepairTimestamp",
            "timestamp(6)");
        timestampRepair.DefaultValueSql = "CURRENT_TIMESTAMP(6)";

        MigrationOperation[] operations =
        [
            CreateRequiredColumnRepair<DateOnly>(
                tableName,
                "RepairDate",
                "date",
                new DateOnly(2026, 8, 21)),
            CreateRequiredColumnRepair<DateTime>(
                tableName,
                "RepairDateTime",
                "datetime(6)",
                new DateTime(2026, 8, 21, 12, 34, 56)),
            timestampRepair,
        ];

        await ExecuteAsync(generator, model, relationalConnection, operations)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{tableName}` "
            + "WHERE `RepairDate` = DATE '2026-08-21' "
            + "AND `RepairDateTime` = TIMESTAMP '2026-08-21 12:34:56' "
            + "AND `RepairTimestamp` IS NOT NULL;";

        Assert.Equal(
            2L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));

        var invalidRepair = CreateRequiredColumnRepair<DateTime>(
            tableName,
            "InvalidRepairTimestamp",
            "timestamp(6)");

        invalidRepair.DefaultValueSql = "TIMESTAMP '0001-01-01 00:00:00'";

        var exception = await Assert.ThrowsAsync<MySqlException>(() =>
            ExecuteAsync(generator, model, relationalConnection, [invalidRepair]));

        Assert.Equal(1292, exception.Number);

        command.CommandText = "SELECT IS_NULLABLE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + $"AND TABLE_NAME = '{tableName}' "
            + "AND COLUMN_NAME = 'InvalidRepairTimestamp';";

        Assert.Equal(
            "YES",
            Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private static async Task AssertConstrainedNullableRepairAsync(
        IMigrationsSqlGenerator generator,
        IModel model,
        IRelationalConnection relationalConnection,
        MySqlConnection connection,
        string tableName
    )
    {
        AlterColumnOperation[] implicitRepairs =
        [
            CreateRequiredColumnRepair<string>(tableName, "RepairJson", "json"),
            CreateRequiredColumnRepair<byte[]>(tableName, "RepairSpatial", "point"),
            CreateRequiredColumnRepair<string>(tableName, "RepairEnum", "enum('alpha','beta')"),
            CreateRequiredColumnRepair<int>(tableName, "RepairChecked", "int"),
        ];

        foreach (var implicitRepair in implicitRepairs)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                generator.Generate([implicitRepair], model));

            Assert.Contains("application contract", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(implicitRepair.Name, exception.Message, StringComparison.Ordinal);
        }

        var jsonRepair = CreateRequiredColumnRepair<string>(
            tableName,
            "RepairJson",
            "json",
            "{}");
        var spatialRepair = CreateRequiredColumnRepair<byte[]>(
            tableName,
            "RepairSpatial",
            "point");
        spatialRepair.DefaultValueSql = "ST_GeomFromText('POINT(0 0)')";
        var enumRepair = CreateRequiredColumnRepair<string>(
            tableName,
            "RepairEnum",
            "enum('alpha','beta')",
            "alpha");
        var checkedRepair = CreateRequiredColumnRepair<int>(
            tableName,
            "RepairChecked",
            "int",
            1);

        await ExecuteAsync(
                generator,
                model,
                relationalConnection,
                [jsonRepair, spatialRepair, enumRepair, checkedRepair])
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{tableName}` "
            + "WHERE JSON_LENGTH(`RepairJson`) = 0 "
            + "AND ST_X(`RepairSpatial`) = 0 "
            + "AND ST_Y(`RepairSpatial`) = 0 "
            + "AND `RepairEnum` = 'alpha' "
            + "AND `RepairChecked` = 1;";

        Assert.Equal(
            2L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));
    }

    private static AlterColumnOperation CreateRequiredColumnRepair<TValue>(
        string tableName,
        string columnName,
        string columnType,
        object? defaultValue = null
    )
    {
        return new AlterColumnOperation
        {
            Table = tableName,
            Name = columnName,
            ClrType = typeof(TValue),
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
            OldColumn =
            {
                ClrType = typeof(TValue),
                ColumnType = columnType,
                IsNullable = true,
            },
        };
    }

    private static async Task AssertMySqlRejectsUnparenthesizedTemporalDefaultsAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        (string StoreType, string Literal)[] invalidDefaults =
        [
            ("date", "DATE '2026-08-17'"),
            ("time(6)", "TIME '12:34:56.123456'"),
        ];

        foreach (var (storeType, literal) in invalidDefaults)
        {
            var tableName = $"InvalidTemporalDefault_{Guid.NewGuid():N}";

            try
            {
                command.CommandText = $"CREATE TABLE `{tableName}` (`Value` {storeType} DEFAULT {literal});";

                var exception = await Assert.ThrowsAsync<MySqlException>(async () =>
                    await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));

                Assert.Equal(1067, exception.Number);
            }
            finally
            {
                command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
                _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private sealed class ColumnDefaultContext : DbContext
    {
        public ColumnDefaultContext(
            DbContextOptions<ColumnDefaultContext> options
        ) : base(options) { }
    }
}

internal static class TimestampRepairMigrationContract
{
    public const string HistoryTable = "__DokaTimestampRepairHistory";
    public const string InitialMigration = "20260821000000_TimestampRepairInitial";
    public const string InvalidMigration = "20260821000100_TimestampRepairInvalid";
    public const string Table = "DokaTimestampRepairProbe";
}

internal sealed class TimestampRepairMigrationContext : DbContext
{
    public TimestampRepairMigrationContext(
        DbContextOptions<TimestampRepairMigrationContext> options
    ) : base(options) { }
}

[DbContext(typeof(TimestampRepairMigrationContext))]
[Migration(TimestampRepairMigrationContract.InitialMigration)]
internal sealed class TimestampRepairInitialMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    )
    {
        migrationBuilder.CreateTable(
            name: TimestampRepairMigrationContract.Table,
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp(6)", nullable: true),
            },
            constraints: table =>
                table.PrimaryKey("PK_DokaTimestampRepairProbe", column => column.Id));

        migrationBuilder.InsertData(
            table: TimestampRepairMigrationContract.Table,
            columns: ["Id", "OccurredAt"],
            columnTypes: ["int", "timestamp(6)"],
            values: [1, null]);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.DropTable(TimestampRepairMigrationContract.Table);
}

[DbContext(typeof(TimestampRepairMigrationContext))]
[Migration(TimestampRepairMigrationContract.InvalidMigration)]
internal sealed class TimestampRepairInvalidMigration : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.AlterColumn<DateTime>(
        name: "OccurredAt",
        table: TimestampRepairMigrationContract.Table,
        type: "timestamp(6)",
        nullable: false,
        oldClrType: typeof(DateTime),
        oldType: "timestamp(6)",
        oldNullable: true);

    protected override void Down(
        MigrationBuilder migrationBuilder
    ) => migrationBuilder.AlterColumn<DateTime>(
        name: "OccurredAt",
        table: TimestampRepairMigrationContract.Table,
        type: "timestamp(6)",
        nullable: true,
        oldClrType: typeof(DateTime),
        oldType: "timestamp(6)");
}
