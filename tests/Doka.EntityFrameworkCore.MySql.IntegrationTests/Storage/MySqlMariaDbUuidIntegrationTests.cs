namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Exercises MariaDB's native UUID type through Doka's fixed Binary16 connector configuration.
/// </summary>
[IntegrationDatabaseTest]
[Trait("Category", "MigrationContract")]
[Trait("VerificationLane", "FullIntegration")]
public sealed partial class MySqlMariaDbUuidIntegrationTests
    : IClassFixture<MySqlMariaDbUuidIntegrationTests.NativeUuidFixture>
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

    // WHY: Derived case data stays below its source GUIDs in the same partial;
    // static field initialization order across separate partial files is unspecified.
    private static readonly Guid[] s_seedMembership = [s_seededId, Guid.Empty];
    private static readonly Guid[] s_v7Membership = [s_v7Last, Guid.Empty];
    private static readonly VersionedOrderCase[] s_versionedOrderCases =
    [
        new("NativeV1", "NativeId", 1, 2, [2, 1], s_v1TextLast, 1),
        new("BinaryV1", "BinaryId", 1, 2, [1, 2], s_v1TextLast, null),
        new("CharV1", "CharId", 1, 2, [1, 2], s_v1TextLast, null),
        new("NativeV7", "NativeId", 3, 4, [3, 4], s_v7First, 4),
    ];

    private readonly NativeUuidFixture _fixture;
    private static readonly IntegrationDatabaseTarget[] s_mariaDbTargets =
    [
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123,
    ];

    /// <summary>Uses the class-owned native and versioned UUID datasets.</summary>
    public MySqlMariaDbUuidIntegrationTests(
        NativeUuidFixture fixture
    ) => _fixture = fixture;

    /// <summary>Identifies selected MariaDB engines that support native UUID storage.</summary>
    public static IEnumerable<object[]> NativeTargetCases =>
        s_mariaDbTargets.Where(IntegrationTestEnvironment.IsTargetSelected)
            .Select(target => new object[] { target.ToString() });

    /// <summary>
    /// The connector must expose a native UUID value as text or Guid, never as opaque bytes.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_native_uuid_wire_value_is_materializable()
    {
        // Arrange
        var connectionString = new MySqlConnectionStringBuilder(
            GetConnectionString(IntegrationDatabaseTarget.MariaDb118, "Probes"))
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

            // Act
            var value = await select.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

            // Assert
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
        // Arrange
        var connectionString = GetConnectionString(IntegrationDatabaseTarget.MariaDb118, "Probes");
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
            // Act
            var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
                .Create(connectionString, options);
            var scaffolded = services.GetRequiredService<IReverseEngineerScaffolder>()
                .ScaffoldModel(
                    connectionString,
                    options,
                    new ModelReverseEngineerOptions(),
                    ScaffoldingTestServices.CreateCodeGenerationOptions(connectionString));

            // Assert
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
        // Arrange
        var connectionString = new MySqlConnectionStringBuilder(
            GetConnectionString(IntegrationDatabaseTarget.MariaDb118, "Probes"))
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

            // Act
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);
            // WHY: Explicit seed keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
            context.Add(item);
            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var actual = await context.Set<NativeUuidOptInEntity>()
                .AsNoTracking()
                .SingleAsync(value => value.NativeId == s_seededId, CancellationToken.None)
                .ConfigureAwait(false);
            var columnTypes = await ReadOptInColumnTypesAsync(context).ConfigureAwait(false);

            // Assert
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
        // Arrange
        var connectionString = new MySqlConnectionStringBuilder(
            GetConnectionString(IntegrationDatabaseTarget.MariaDb118, "Probes"))
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
            // Act
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None).ConfigureAwait(false);

            // WHY: Explicit seed keys need no asynchronous value generation.
            // ReSharper disable once MethodHasAsyncOverload
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

            // Assert
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
}
