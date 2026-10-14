namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Exercises MariaDB's native UUID type through Doka's fixed Binary16 connector configuration.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlMariaDbUuidIntegrationTests
{
    private const string ProbeTable = "DokaNativeUuidProbe";
    private const string PrincipalTable = "DokaNativeUuidPrincipals";
    private const string DependentTable = "DokaNativeUuidDependents";
    private const string OptInTable = "DokaNativeUuidOptIn";
    private const string VersionedTable = "DokaVersionedUuidProbe";
    private const string Char36BytesTable = "DokaChar36BytesProbe";

    private static readonly Guid s_probeGuid = Guid.Parse("00112233-4455-4677-8899-aabbccddeeff");
    private static readonly Guid s_seededId = Guid.Parse("bf1da273-beed-4197-ab57-4cf8395244d4");
    private static readonly Guid s_runtimeId = Guid.Parse("a5e91c65-450d-47fa-9683-b6471d3df651");
    private static readonly Guid s_optionalId = Guid.Parse("1e15bed0-86cb-408e-b1cf-08952340a095");
    private static readonly Guid s_convertedSeedId = Guid.Parse("5c855603-622d-4bee-b232-f74f63026caf");
    private static readonly Guid s_convertedId = Guid.Parse("5100da31-7b88-45e7-9606-08edfe8f7dab");
    private static readonly Guid s_v1TextFirst = Guid.Parse("00000001-0000-1000-8000-ffffffffffff");
    private static readonly Guid s_v1TextLast = Guid.Parse("ffffffff-0000-1000-8000-000000000001");
    private static readonly Guid s_v7First = Guid.Parse("0189f81c-f940-7161-9c48-9677e11bc40c");
    private static readonly Guid s_v7Last = Guid.Parse("0189f81d-498e-72f8-b0fa-246d7be1adb1");

    /// <summary>
    /// Verifies native UUID storage, seed data, relationships, and queries on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_round_trips_native_uuid_model() =>
        AssertNativeUuidModelAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies native UUID storage, seed data, relationships, and queries on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_round_trips_native_uuid_model() =>
        AssertNativeUuidModelAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies native UUID storage, seed data, relationships, and queries on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_round_trips_native_uuid_model() =>
        AssertNativeUuidModelAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies native UUID storage, seed data, relationships, and queries on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_round_trips_native_uuid_model() =>
        AssertNativeUuidModelAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// Verifies v1 storage order and v7 comparison semantics on MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_preserves_versioned_uuid_comparisons() =>
        AssertVersionedUuidComparisonsAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <summary>
    /// Verifies v1 storage order and v7 comparison semantics on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_preserves_versioned_uuid_comparisons() =>
        AssertVersionedUuidComparisonsAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies v1 storage order and v7 comparison semantics on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_preserves_versioned_uuid_comparisons() =>
        AssertVersionedUuidComparisonsAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// Verifies v1 storage order and v7 comparison semantics on MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_preserves_versioned_uuid_comparisons() =>
        AssertVersionedUuidComparisonsAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// The connector must expose a native UUID value as text or Guid, never as opaque bytes.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_uuid_wire_value_is_materializable()
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await DropProbeTableAsync(connection).ConfigureAwait(false);

        try
        {
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE TABLE `{ProbeTable}` (`Id` uuid NOT NULL PRIMARY KEY);";
                _ = await create.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO `{ProbeTable}` (`Id`) VALUES (@id);";
                insert.Parameters.AddWithValue("@id", s_probeGuid.ToString("D", CultureInfo.InvariantCulture));
                _ = await insert.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT `Id` FROM `{ProbeTable}`;";
            var value = await select.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.True(value is string or Guid, $"Unexpected connector UUID value: {value?.GetType().FullName}");
            Assert.Equal(
                s_probeGuid,
                value is Guid guid ? guid : Guid.Parse((string)value!));
        }
        finally
        {
            await DropProbeTableAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The live catalog type must survive database discovery and generated model code.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_live_scaffolding_preserves_native_uuid_guid()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await DropProbeTableAsync(connection).ConfigureAwait(false);

        try
        {
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE TABLE `{ProbeTable}` (`Id` uuid NOT NULL PRIMARY KEY);";
                _ = await create.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await using var provider = ScaffoldingTestServices.CreateDesignTimeServiceProvider();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var options = new DatabaseModelFactoryOptions([ProbeTable], Array.Empty<string>());
            var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, options);
            var scaffolded = services.GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    options,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));
            var column = Assert.Single(Assert.Single(databaseModel.Tables).Columns);

            Assert.Equal("uuid", column.StoreType);
            Assert.Contains("public Guid Id { get; set; }", scaffolded.AdditionalFiles.Single().Code,
                StringComparison.Ordinal);
            Assert.Contains("HasMySqlGuidFormat(MySqlGuidFormat.Uuid)", scaffolded.ContextFile.Code,
                StringComparison.Ordinal);
        }
        finally
        {
            await DropProbeTableAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A property-level native UUID choice must not change the Binary16 default for other Guid columns.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_property_opt_in_preserves_binary16_default()
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;
        var builder = IntegrationTestDbContextOptions.Create<NativeUuidOptInContext>();
        builder.UseMySql(
            connectionString,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb118),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Binary16));

        await using var context = new NativeUuidOptInContext(builder.Options);
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{OptInTable}`;",
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var item = new NativeUuidOptInEntity
            {
                Id = 1,
                NativeId = s_seededId,
                BinaryId = s_runtimeId,
            };
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);
            context.Add(item);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var actual = await context.Set<NativeUuidOptInEntity>()
                .AsNoTracking()
                .SingleAsync(value => value.NativeId == s_seededId, CancellationToken.None)
                .ConfigureAwait(false);
            var columnTypes = await ReadOptInColumnTypesAsync(context).ConfigureAwait(false);

            Assert.Equal(s_seededId, actual.NativeId);
            Assert.Equal(s_runtimeId, actual.BinaryId);
            Assert.Equal("uuid", columnTypes["NativeId"]);
            Assert.Equal("binary", columnTypes["BinaryId"]);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{OptInTable}`;",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A Char36 model default must not make an application byte converter read as a connector Guid.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_char36_default_round_trips_application_guid_bytes()
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        var builder = IntegrationTestDbContextOptions.Create<Char36BytesContext>();
        builder.UseMySql(
            connectionString,
            IntegrationTestEnvironment.GetServerVersion(IntegrationDatabaseTarget.MariaDb118),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36));

        await using var context = new Char36BytesContext(builder.Options);
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{Char36BytesTable}`;",
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);
            context.Add(new Char36BytesEntity { Id = 1, ConvertedId = s_convertedId });
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var actual = await context.Set<Char36BytesEntity>()
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == 1, CancellationToken.None)
                .ConfigureAwait(false);

            var connection = (MySqlConnection)context.Database.GetDbConnection();
            await using var column = connection.CreateCommand();
            column.CommandText = "SELECT COLUMN_TYPE FROM information_schema.COLUMNS "
                + "WHERE TABLE_SCHEMA = DATABASE() "
                + $"AND TABLE_NAME = '{Char36BytesTable}' AND COLUMN_NAME = 'ConvertedId';";
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            var columnType = await column.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Equal(s_convertedId, actual.ConvertedId);
            Assert.Equal("varbinary(17)", columnType);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS `{Char36BytesTable}`;",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task AssertVersionedUuidComparisonsAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;

        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await DropVersionedTableAsync(connection).ConfigureAwait(false);

        try
        {
            await using var context = new VersionedUuidContext(
                IntegrationTestDbContextOptions
                    .Create<VersionedUuidContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options.DefaultGuidFormat(MySqlGuidFormat.Uuid))
                    .Options);

            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);
            context.AddRange(
                new VersionedUuidEntity
                {
                    Id = 1, NativeId = s_v1TextFirst, BinaryId = s_v1TextFirst, CharId = s_v1TextFirst,
                },
                new VersionedUuidEntity
                {
                    Id = 2, NativeId = s_v1TextLast, BinaryId = s_v1TextLast, CharId = s_v1TextLast,
                },
                new VersionedUuidEntity
                {
                    Id = 3, NativeId = s_v7First, BinaryId = s_v7First, CharId = s_v7First,
                },
                new VersionedUuidEntity
                {
                    Id = 4, NativeId = s_v7Last, BinaryId = s_v7Last, CharId = s_v7Last,
                });
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var v7Values = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .Where(entity => entity.Id >= 3)
                .OrderBy(entity => entity.Id)
                .Select(entity => entity.NativeId)
                .ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

            var exactV7 = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .SingleAsync(entity => entity.NativeId == s_v7Last, CancellationToken.None)
                .ConfigureAwait(false);

            var membership = new[] { s_v7Last, Guid.Empty };
            var containedV7 = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .SingleAsync(entity => EF.Parameter(membership).Contains(entity.NativeId), CancellationToken.None)
                .ConfigureAwait(false);

            var missing = await context.Set<VersionedUuidEntity>()
                .AnyAsync(entity => entity.NativeId == Guid.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            var nativeV1Order = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .Where(entity => entity.Id <= 2)
                .OrderBy(entity => entity.NativeId)
                .Select(entity => entity.Id)
                .ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

            var binaryV1Order = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .Where(entity => entity.Id <= 2)
                .OrderBy(entity => entity.BinaryId)
                .Select(entity => entity.Id)
                .ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

            var charV1Order = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .Where(entity => entity.Id <= 2)
                .OrderBy(entity => entity.CharId)
                .Select(entity => entity.Id)
                .ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

            var nativeV7Order = await context.Set<VersionedUuidEntity>()
                .AsNoTracking()
                .Where(entity => entity.Id >= 3)
                .OrderBy(entity => entity.NativeId)
                .Select(entity => entity.Id)
                .ToArrayAsync(CancellationToken.None).ConfigureAwait(false);

            var nativeV1Next = await ReadFirstIdAfterAsync(
                connection, "NativeId", 1, 2, s_v1TextLast).ConfigureAwait(false);

            var binaryV1Next = await ReadFirstIdAfterAsync(
                connection, "BinaryId", 1, 2, s_v1TextLast).ConfigureAwait(false);

            var charV1Next = await ReadFirstIdAfterAsync(
                connection, "CharId", 1, 2, s_v1TextLast).ConfigureAwait(false);

            var nativeV7Next = await ReadFirstIdAfterAsync(
                connection, "NativeId", 3, 4, s_v7First).ConfigureAwait(false);

            Assert.Equal([s_v7First, s_v7Last], v7Values);
            Assert.Equal(4, exactV7.Id);
            Assert.Equal(4, containedV7.Id);
            Assert.False(missing);
            Assert.Equal([2, 1], nativeV1Order);
            Assert.Equal([1, 2], binaryV1Order);
            Assert.Equal([1, 2], charV1Order);
            Assert.Equal([3, 4], nativeV7Order);
            Assert.Equal(1, nativeV1Next);
            Assert.Null(binaryV1Next);
            Assert.Null(charV1Next);
            Assert.Equal(4, nativeV7Next);
        }
        finally
        {
            await DropVersionedTableAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task<int?> ReadFirstIdAfterAsync(
        MySqlConnection connection,
        string columnName,
        int firstId,
        int lastId,
        Guid pivot
    )
    {
        await using var command = connection.CreateCommand();
        // The server owns UUID ordering. Raw SQL isolates its range semantics
        // from Guid.CompareTo translation, which is not part of this feature.
        command.CommandText = $"SELECT `Id` FROM `{VersionedTable}` "
            + $"WHERE `Id` BETWEEN @firstId AND @lastId AND `{columnName}` > @pivot "
            + $"ORDER BY `{columnName}` LIMIT 1;";
        command.Parameters.AddWithValue("@firstId", firstId);
        command.Parameters.AddWithValue("@lastId", lastId);
        // The connector must bind Binary16 as Guid, but native UUID as text.
        // A text pivot against Binary16 would test coercion instead of key order.
        if (columnName == "BinaryId")
        {
            command.Parameters.AddWithValue("@pivot", pivot);
        }
        else
        {
            command.Parameters.AddWithValue("@pivot", pivot.ToString("D", CultureInfo.InvariantCulture));
        }

        var value = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task AssertNativeUuidModelAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
            Pooling = false,
        }.ConnectionString;
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await DropModelTablesAsync(connection).ConfigureAwait(false);

        try
        {
            await using var context = new NativeUuidContext(
                IntegrationTestDbContextOptions
                    .Create<NativeUuidContext>()
                    .UseMySql(
                        connection,
                        serverVersion,
                        options => options.DefaultGuidFormat(MySqlGuidFormat.Uuid))
                    .Options);
            var model = context.GetService<IDesignTimeModel>().Model;
            var operations = context
                .GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel());
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var relationalConnection = context.GetService<IRelationalConnection>();

            foreach (var command in generator.Generate(operations, model))
            {
                _ = await command
                    .ExecuteNonQueryAsync(relationalConnection, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var runtime = new NativeUuidPrincipal
            {
                Id = s_runtimeId,
                OptionalNativeId = null,
                BinaryId = Guid.Parse("79cf7ff0-1bb0-4007-8ef4-b345386a6f41"),
                CharId = Guid.Parse("1458f03f-9acb-4902-bca8-280b613a7c92"),
                ConvertedId = s_convertedId,
                Name = "runtime",
            };
            context.Add(runtime);
            context.Add(new NativeUuidDependent
            {
                Id = 2,
                PrincipalId = s_runtimeId,
                Name = "runtime-related",
            });
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            runtime.OptionalNativeId = s_optionalId;
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var ids = new[] { s_seededId, Guid.Empty };
            var seeded = await context.Principals
                .AsNoTracking()
                .Include(principal => principal.Dependents)
                .SingleAsync(principal => EF.Parameter(ids).Contains(principal.Id), CancellationToken.None)
                .ConfigureAwait(false);
            var actualRuntime = await context.Principals
                .AsNoTracking()
                .Include(principal => principal.Dependents)
                .SingleAsync(principal => principal.Id == s_runtimeId, CancellationToken.None)
                .ConfigureAwait(false);
            var missing = await context.Principals
                .AnyAsync(principal => principal.Id == Guid.Empty, CancellationToken.None)
                .ConfigureAwait(false);
            var likeMatch = await context.Principals
                .AnyAsync(
                    principal => EF.Functions.Like(principal.Id, "bf1da273-%"),
                    CancellationToken.None)
                .ConfigureAwait(false);
            var formatted = await context.Principals
                .Where(principal => principal.Id == s_seededId)
                .Select(principal => principal.Id.ToString())
                .SingleAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var generated = await context.Principals
                .Select(_ => Guid.NewGuid())
                .FirstAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var columnTypes = await ReadColumnTypesAsync(connection).ConfigureAwait(false);
            await using var rejectedInsert = connection.CreateCommand();
            rejectedInsert.CommandText = $"INSERT INTO `{DependentTable}` "
                + "(`Id`, `PrincipalId`, `Name`) VALUES (3, @id, 'orphan');";
            rejectedInsert.Parameters.AddWithValue(
                "@id",
                Guid.Empty.ToString("D", CultureInfo.InvariantCulture));
            var foreignKeyFailure = await Record.ExceptionAsync(async () =>
                await rejectedInsert.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));

            Assert.Equal(s_seededId, seeded.Id);
            Assert.Equal(s_optionalId, seeded.OptionalNativeId);
            Assert.Equal(s_convertedSeedId, seeded.ConvertedId);
            Assert.Single(seeded.Dependents);
            Assert.Equal(s_seededId, seeded.Dependents.Single().PrincipalId);
            Assert.Equal("runtime", actualRuntime.Name);
            Assert.Equal(s_optionalId, actualRuntime.OptionalNativeId);
            Assert.Equal(s_convertedId, actualRuntime.ConvertedId);
            Assert.Single(actualRuntime.Dependents);
            Assert.Equal(s_runtimeId, actualRuntime.Dependents.Single().PrincipalId);
            Assert.False(missing);
            Assert.True(likeMatch);
            Assert.Equal(s_seededId.ToString("D", CultureInfo.InvariantCulture), formatted);
            Assert.NotEqual(Guid.Empty, generated);
            Assert.Equal(1452, Assert.IsType<MySqlException>(foreignKeyFailure).Number);
            Assert.Equal("uuid", columnTypes["Id"]);
            Assert.Equal("uuid", columnTypes["OptionalNativeId"]);
            Assert.Equal("binary", columnTypes["BinaryId"]);
            Assert.Equal("char", columnTypes["CharId"]);
            Assert.Equal("varbinary", columnTypes["ConvertedId"]);
            Assert.Equal("uuid", columnTypes["PrincipalId"]);
        }
        finally
        {
            await DropModelTablesAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, string>> ReadOptInColumnTypesAsync(
        NativeUuidOptInContext context
    )
    {
        var connection = (MySqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() "
            + $"AND TABLE_NAME = '{OptInTable}' AND COLUMN_NAME IN ('NativeId', 'BinaryId');";
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> ReadColumnTypesAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND "
            + $"((TABLE_NAME = '{PrincipalTable}' AND COLUMN_NAME IN "
            + "('Id', 'OptionalNativeId', 'BinaryId', 'CharId', 'ConvertedId')) OR "
            + $"(TABLE_NAME = '{DependentTable}' AND COLUMN_NAME = 'PrincipalId'));";
        await using var reader = await command
            .ExecuteReaderAsync(CancellationToken.None)
            .ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task DropModelTablesAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{DependentTable}`; "
            + $"DROP TABLE IF EXISTS `{PrincipalTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task DropProbeTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{ProbeTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task DropVersionedTableAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS `{VersionedTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class NativeUuidContext(
        DbContextOptions<NativeUuidContext> options
    ) : DbContext(options)
    {
        public DbSet<NativeUuidPrincipal> Principals => Set<NativeUuidPrincipal>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<NativeUuidPrincipal>(entity =>
            {
                entity.ToTable(PrincipalTable);
                entity.HasKey(principal => principal.Id);
                entity.Property(principal => principal.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(principal => principal.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity.Property(principal => principal.ConvertedId)
                    .HasConversion<byte[]>();
                entity.HasData(new NativeUuidPrincipal
                {
                    Id = s_seededId,
                    OptionalNativeId = s_optionalId,
                    BinaryId = Guid.Parse("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    CharId = Guid.Parse("1e326748-be2c-4856-bf8d-cffc79c192cf"),
                    ConvertedId = s_convertedSeedId,
                    Name = "seeded",
                });
            });

            modelBuilder.Entity<NativeUuidDependent>(entity =>
            {
                entity.ToTable(DependentTable);
                entity.HasKey(dependent => dependent.Id);
                entity.HasOne(dependent => dependent.Principal)
                    .WithMany(principal => principal.Dependents)
                    .HasForeignKey(dependent => dependent.PrincipalId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasData(new NativeUuidDependent
                {
                    Id = 1,
                    PrincipalId = s_seededId,
                    Name = "seeded-related",
                });
            });
        }
    }

    private sealed class NativeUuidPrincipal
    {
        public Guid Id { get; set; }

        public Guid? OptionalNativeId { get; set; }

        public Guid BinaryId { get; set; }

        public Guid CharId { get; set; }

        public Guid ConvertedId { get; set; }

        public string Name { get; set; } = string.Empty;

        public ICollection<NativeUuidDependent> Dependents { get; } = [];
    }

    private sealed class NativeUuidDependent
    {
        public int Id { get; set; }

        public Guid PrincipalId { get; set; }

        public string Name { get; set; } = string.Empty;

        public NativeUuidPrincipal Principal { get; set; } = null!;
    }

    private sealed class NativeUuidOptInContext(
        DbContextOptions<NativeUuidOptInContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<NativeUuidOptInEntity>(entity =>
            {
                entity.ToTable(OptInTable);
                entity.HasKey(value => value.Id);
                entity.Property(value => value.NativeId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Uuid);
            });
        }
    }

    private sealed class NativeUuidOptInEntity
    {
        public int Id { get; set; }

        public Guid NativeId { get; set; }

        public Guid BinaryId { get; set; }
    }

    private sealed class VersionedUuidContext(
        DbContextOptions<VersionedUuidContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<VersionedUuidEntity>(entity =>
            {
                entity.ToTable(VersionedTable);
                entity.HasKey(value => value.NativeId);
                entity.Property(value => value.BinaryId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(value => value.CharId)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            });
        }
    }

    private sealed class VersionedUuidEntity
    {
        public int Id { get; set; }

        public Guid NativeId { get; set; }

        public Guid BinaryId { get; set; }

        public Guid CharId { get; set; }
    }

    private sealed class Char36BytesContext(
        DbContextOptions<Char36BytesContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<Char36BytesEntity>(entity =>
            {
                entity.ToTable(Char36BytesTable);
                entity.HasKey(value => value.Id);
                entity.Property(value => value.ConvertedId)
                    .HasConversion<byte[]>();
            });
        }
    }

    private sealed class Char36BytesEntity
    {
        public int Id { get; set; }

        public Guid ConvertedId { get; set; }
    }
}
