using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies provider-native Guid store-type transitions against live database engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlGuidMigrationIntegrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";
    private const string StorageTransitionTable = "GuidStorageTransitionRecords";
    private static readonly Guid s_documentId = Guid.Parse("b70d9279-dc5e-4cd5-9cd3-a983613aaed7");
    private static readonly Guid s_secondDocumentId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>
    /// Application-owned Char36/Binary16 staging preserves valid values and
    /// identifies malformed values before the destructive schema step.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MySql84);

    /// <inheritdoc cref="MySql84_staged_guid_storage_transition_roundtrips_data" />
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MySql97);

    /// <inheritdoc cref="MySql84_staged_guid_storage_transition_roundtrips_data" />
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MariaDb1011);

    /// <inheritdoc cref="MySql84_staged_guid_storage_transition_roundtrips_data" />
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <inheritdoc cref="MySql84_staged_guid_storage_transition_roundtrips_data" />
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <inheritdoc cref="MySql84_staged_guid_storage_transition_roundtrips_data" />
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_staged_guid_storage_transition_roundtrips_data() =>
        AssertStagedGuidStorageTransitionAsync(IntegrationDatabaseTarget.MariaDb123);

    /// <summary>
    /// A populated indexed relationship survives both migration directions while
    /// retaining its canonical Guid text, constraint name, and cascade behavior.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_char36_transition_preserves_populated_relationship_in_both_directions() =>
        await AssertChar36TransitionPreservesPopulatedRelationshipAsync(IntegrationDatabaseTarget.MariaDb118);

    /// <summary>
    /// The same populated relationship contract applies to the MySQL engine family.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task MySql84_char36_transition_preserves_populated_relationship_in_both_directions() =>
        await AssertChar36TransitionPreservesPopulatedRelationshipAsync(IntegrationDatabaseTarget.MySql84);

    private static async Task AssertStagedGuidStorageTransitionAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await CleanupAsync(connection).ConfigureAwait(false);

        try
        {
            await ExecuteRawAsync(
                    connection,
                    $"CREATE TABLE `{StorageTransitionTable}` ("
                    + "`Id` int NOT NULL AUTO_INCREMENT, "
                    + "`RequiredValue` char(36) NOT NULL, "
                    + "`OptionalValue` char(36) NULL, "
                    + $"CONSTRAINT `PK_{StorageTransitionTable}` PRIMARY KEY (`Id`));")
                .ConfigureAwait(false);

            await using (var charContext = new Char36StorageContext(
                             CreateOptions<Char36StorageContext>(
                                 connectionString,
                                 serverVersion,
                                 MySqlGuidFormat.Char36)))
            {
                charContext.Records.AddRange(
                    new GuidStorageRecord
                    {
                        RequiredValue = s_documentId,
                        OptionalValue = Guid.Empty,
                    },
                    new GuidStorageRecord
                    {
                        RequiredValue = s_secondDocumentId,
                    });

                await charContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await ExecuteRawAsync(
                    connection,
                    $"INSERT INTO `{StorageTransitionTable}` (`RequiredValue`, `OptionalValue`) "
                    + "VALUES ('not-a-guid', NULL);")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "ADD COLUMN `RequiredValueBinary` binary(16) NULL, "
                    + "ADD COLUMN `OptionalValueBinary` binary(16) NULL;")
                .ConfigureAwait(false);

            Assert.Equal(1L, await CountInvalidChar36SourceValuesAsync(connection).ConfigureAwait(false));

            await ExecuteRawAsync(
                    connection,
                    $"DELETE FROM `{StorageTransitionTable}` WHERE `RequiredValue` = 'not-a-guid';")
                .ConfigureAwait(false);

            Assert.Equal(0L, await CountInvalidChar36SourceValuesAsync(connection).ConfigureAwait(false));

            await ExecuteRawAsync(connection, CreateChar36ToBinaryBackfillSql()).ConfigureAwait(false);

            Assert.Equal(0L, await CountInvalidChar36BackfillsAsync(connection).ConfigureAwait(false));

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "MODIFY COLUMN `RequiredValueBinary` binary(16) NOT NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "DROP COLUMN `RequiredValue`, DROP COLUMN `OptionalValue`, "
                    + "CHANGE COLUMN `RequiredValueBinary` `RequiredValue` binary(16) NOT NULL, "
                    + "CHANGE COLUMN `OptionalValueBinary` `OptionalValue` binary(16) NULL;")
                .ConfigureAwait(false);

            await AssertBinaryStorageAsync(connection).ConfigureAwait(false);

            await using (var binaryContext = new Binary16StorageContext(
                             CreateOptions<Binary16StorageContext>(
                                 connectionString,
                                 serverVersion,
                                 MySqlGuidFormat.Binary16)))
            {
                await AssertStorageRowsAsync(binaryContext).ConfigureAwait(false);
            }

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "ADD COLUMN `RequiredValueText` char(36) NULL, "
                    + "ADD COLUMN `OptionalValueText` char(36) NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(connection, CreateBinaryToChar36BackfillSql()).ConfigureAwait(false);

            Assert.Equal(0L, await CountInvalidBinaryBackfillsAsync(connection).ConfigureAwait(false));

            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "MODIFY COLUMN `RequiredValueText` char(36) NOT NULL;")
                .ConfigureAwait(false);
            await ExecuteRawAsync(
                    connection,
                    $"ALTER TABLE `{StorageTransitionTable}` "
                    + "DROP COLUMN `RequiredValue`, DROP COLUMN `OptionalValue`, "
                    + "CHANGE COLUMN `RequiredValueText` `RequiredValue` char(36) NOT NULL, "
                    + "CHANGE COLUMN `OptionalValueText` `OptionalValue` char(36) NULL;")
                .ConfigureAwait(false);

            await AssertChar36StorageAsync(connection).ConfigureAwait(false);

            await using var finalContext = new Char36StorageContext(
                CreateOptions<Char36StorageContext>(
                    connectionString,
                    serverVersion,
                    MySqlGuidFormat.Char36));

            await AssertStorageRowsAsync(finalContext).ConfigureAwait(false);
        }
        finally
        {
            await CleanupAsync(connection).ConfigureAwait(false);
        }
    }

    private static string CreateChar36ToBinaryBackfillSql() =>
        $"UPDATE `{StorageTransitionTable}` SET "
        + "`RequiredValueBinary` = UNHEX(REPLACE(TRIM(`RequiredValue`), '-', '')), "
        + "`OptionalValueBinary` = CASE WHEN `OptionalValue` IS NULL THEN NULL "
        + "ELSE UNHEX(REPLACE(TRIM(`OptionalValue`), '-', '')) END;";

    private static string CreateBinaryToChar36BackfillSql() =>
        $"UPDATE `{StorageTransitionTable}` SET "
        + $"`RequiredValueText` = {FormatBinaryGuid("`RequiredValue`")}, "
        + "`OptionalValueText` = CASE WHEN `OptionalValue` IS NULL THEN NULL "
        + $"ELSE {FormatBinaryGuid("`OptionalValue`")} END;";

    private static async Task<long> CountInvalidChar36SourceValuesAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + $"NOT ({IsCanonicalChar36("`RequiredValue`")}) OR "
            + $"(`OptionalValue` IS NOT NULL AND NOT ({IsCanonicalChar36("`OptionalValue`")}));")
        .ConfigureAwait(false);

    private static async Task<long> CountInvalidChar36BackfillsAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + $"(`RequiredValueBinary` IS NULL OR LOWER(TRIM(`RequiredValue`)) <> {FormatBinaryGuid("`RequiredValueBinary`")}) "
            + "OR (`OptionalValue` IS NOT NULL AND (`OptionalValueBinary` IS NULL "
            + $"OR LOWER(TRIM(`OptionalValue`)) <> {FormatBinaryGuid("`OptionalValueBinary`")}));")
        .ConfigureAwait(false);

    private static async Task<long> CountInvalidBinaryBackfillsAsync(
        MySqlConnection connection
    ) => await ExecuteLongScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM `{StorageTransitionTable}` WHERE "
            + "(`RequiredValueText` IS NULL OR `RequiredValue` <> UNHEX(REPLACE(`RequiredValueText`, '-', '')) "
            + $"OR `RequiredValueText` <> {FormatBinaryGuid("`RequiredValue`")}) "
            + "OR (`OptionalValue` IS NOT NULL AND (`OptionalValueText` IS NULL "
            + "OR `OptionalValue` <> UNHEX(REPLACE(`OptionalValueText`, '-', '')) "
            + $"OR `OptionalValueText` <> {FormatBinaryGuid("`OptionalValue`")}));")
        .ConfigureAwait(false);

    private static string FormatBinaryGuid(
        string expression
    ) => $"LOWER(CONCAT(SUBSTRING(HEX({expression}), 1, 8), '-', "
        + $"SUBSTRING(HEX({expression}), 9, 4), '-', SUBSTRING(HEX({expression}), 13, 4), '-', "
        + $"SUBSTRING(HEX({expression}), 17, 4), '-', SUBSTRING(HEX({expression}), 21, 12)))";

    private static string IsCanonicalChar36(
        string expression
    ) => $"LOWER(TRIM({expression})) REGEXP "
        + "'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'";

    private static async Task AssertBinaryStorageAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT HEX(`RequiredValue`) FROM `{StorageTransitionTable}` ORDER BY `Id` LIMIT 1;";

        Assert.Equal(
            Convert.ToHexString(s_documentId.ToByteArray(bigEndian: true)),
            Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));

        await AssertColumnTypesAsync(connection, "binary(16)").ConfigureAwait(false);
    }

    private static async Task AssertChar36StorageAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `RequiredValue` FROM `{StorageTransitionTable}` ORDER BY `Id` LIMIT 1;";

        Assert.Equal(
            s_documentId.ToString("D", CultureInfo.InvariantCulture),
            Convert.ToString(
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
                CultureInfo.InvariantCulture));

        await AssertColumnTypesAsync(connection, "char(36)").ConfigureAwait(false);
    }

    private static async Task AssertColumnTypesAsync(
        MySqlConnection connection,
        string expectedColumnType
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `COLUMN_TYPE` FROM information_schema.COLUMNS "
            + "WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = @tableName "
            + "AND `COLUMN_NAME` IN ('RequiredValue', 'OptionalValue') ORDER BY `COLUMN_NAME`;";
        command.Parameters.AddWithValue("@tableName", StorageTransitionTable);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var columnTypes = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            columnTypes.Add(reader.GetString(0));
        }

        Assert.Equal([expectedColumnType, expectedColumnType], columnTypes);
    }

    private static async Task AssertStorageRowsAsync(
        GuidStorageContext context
    )
    {
        var rows = await context
            .Records.AsNoTracking()
            .OrderBy(record => record.Id)
            .ToArrayAsync(CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Collection(
            rows,
            first =>
            {
                Assert.Equal(s_documentId, first.RequiredValue);
                Assert.Equal(Guid.Empty, first.OptionalValue);
            },
            second =>
            {
                Assert.Equal(s_secondDocumentId, second.RequiredValue);
                Assert.Null(second.OptionalValue);
            });
    }

    private static async Task<long> ExecuteLongScalarAsync(
        MySqlConnection connection,
        string sql
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
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

    private static async Task AssertChar36TransitionPreservesPopulatedRelationshipAsync(
        IntegrationDatabaseTarget target
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await CleanupAsync(connection);

        try
        {
            await using var empty = new EmptyGuidContext(
                CreateOptions<EmptyGuidContext>(connectionString, serverVersion));

            await using var converted = new ConvertedGuidContext(
                CreateOptions<ConvertedGuidContext>(connectionString, serverVersion));

            await using var native = new NativeChar36GuidContext(
                CreateOptions<NativeChar36GuidContext>(connectionString, serverVersion));

            await ExecuteOperationsAsync(converted, connection, GetDifferences(empty, converted));
            converted.Documents.Add(
                new TextGuidDocument
                {
                    Id = s_documentId,
                    Revisions =
                    {
                        new TextGuidDocumentRevision(),
                    },
                });
            await converted.SaveChangesAsync();

            var upOperations = GetDifferences(converted, native);
            AssertForeignKeyLifecycle(upOperations, "varchar(36)", "char(36)");
            await ExecuteOperationsAsync(native, connection, upOperations);
            await AssertDatabaseContractAsync(native, connection);

            var downOperations = GetDifferences(native, converted);
            AssertForeignKeyLifecycle(downOperations, "char(36)", "varchar(36)");
            await ExecuteOperationsAsync(converted, connection, downOperations);
            await AssertDatabaseContractAsync(converted, connection);
        }
        finally
        {
            await CleanupAsync(connection);
        }
    }

    private static void AssertForeignKeyLifecycle(
        IReadOnlyList<MigrationOperation> operations,
        string oldStoreType,
        string newStoreType
    )
    {
        var operationList = operations.ToList();

        var drop = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alters = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var add = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(ForeignKeyName, drop.Name);
        Assert.Equal(ForeignKeyName, add.Name);
        Assert.Equal(ReferentialAction.Cascade, add.OnDelete);
        Assert.Equal(2, alters.Length);
        Assert.All(alters, alter => Assert.Equal(newStoreType, alter.ColumnType));
        Assert.All(alters, alter => Assert.Equal(oldStoreType, alter.OldColumn.ColumnType));
        Assert.True(operationList.IndexOf(drop) < operationList.IndexOf(alters[0]));
        Assert.True(operationList.IndexOf(drop) < operationList.IndexOf(alters[1]));
        Assert.True(operationList.IndexOf(add) > operationList.IndexOf(alters[0]));
        Assert.True(operationList.IndexOf(add) > operationList.IndexOf(alters[1]));
    }

    private static async Task AssertDatabaseContractAsync(
        GuidMigrationContext context,
        MySqlConnection connection
    )
    {
        context.ChangeTracker.Clear();
        var document = await context
            .Documents
            .Include(item => item.Revisions)
            .SingleAsync();

        Assert.Equal(s_documentId, document.Id);
        Assert.Equal(
            s_documentId,
            Assert.Single(document.Revisions)
                .DocumentId);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(d.`Id` AS CHAR(36)), CAST(r.`DocumentId` AS CHAR(36)) "
            + "FROM `TextGuidDocuments` AS d "
            + "INNER JOIN `TextGuidDocumentRevisions` AS r ON r.`DocumentId` = d.`Id`;";

        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(s_documentId.ToString("D", CultureInfo.InvariantCulture), reader.GetString(0));
            Assert.Equal(s_documentId.ToString("D", CultureInfo.InvariantCulture), reader.GetString(1));
            Assert.False(await reader.ReadAsync());
        }

        command.CommandText = "SELECT rc.`DELETE_RULE` "
            + "FROM information_schema.REFERENTIAL_CONSTRAINTS AS rc "
            + "WHERE rc.`CONSTRAINT_SCHEMA` = DATABASE() "
            + "AND rc.`CONSTRAINT_NAME` = @constraintName;";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@constraintName", ForeignKeyName);

        Assert.Equal("CASCADE", Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel(),
            target
                .GetService<IDesignTimeModel>()
                .Model
                .GetRelationalModel());

    private static async Task ExecuteOperationsAsync(
        DbContext context,
        MySqlConnection connection,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        var commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(
                operations,
                context.GetService<IDesignTimeModel>()
                    .Model);

        foreach (var migrationCommand in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migrationCommand.CommandText;
            _ = await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task CleanupAsync(
        MySqlConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS `TextGuidDocumentRevisions`; "
            + "DROP TABLE IF EXISTS `TextGuidDocuments`; "
            + $"DROP TABLE IF EXISTS `{StorageTransitionTable}`;";
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion,
        MySqlGuidFormat defaultGuidFormat = MySqlGuidFormat.Binary16
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .UseMySql(
            connectionString,
            serverVersion,
            options => options.DefaultGuidFormat(defaultGuidFormat))
        .Options;

    private abstract class GuidStorageContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<GuidStorageRecord> Records => Set<GuidStorageRecord>();

        protected abstract MySqlGuidFormat GuidFormat { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<GuidStorageRecord>(entity =>
            {
                entity.ToTable(StorageTransitionTable);
                entity.HasKey(record => record.Id);
                entity.Property(record => record.Id).UseMySqlAutoIncrementColumn();
                entity.Property(record => record.RequiredValue).HasMySqlGuidFormat(GuidFormat);
                entity.Property(record => record.OptionalValue).HasMySqlGuidFormat(GuidFormat);
            });
        }
    }

    private sealed class Char36StorageContext(DbContextOptions<Char36StorageContext> options)
        : GuidStorageContext(options)
    {
        protected override MySqlGuidFormat GuidFormat => MySqlGuidFormat.Char36;
    }

    private sealed class Binary16StorageContext(DbContextOptions<Binary16StorageContext> options)
        : GuidStorageContext(options)
    {
        protected override MySqlGuidFormat GuidFormat => MySqlGuidFormat.Binary16;
    }

    private sealed class GuidStorageRecord
    {
        public int Id { get; set; }

        public Guid RequiredValue { get; set; }

        public Guid? OptionalValue { get; set; }
    }

    private sealed class EmptyGuidContext(DbContextOptions<EmptyGuidContext> options) : DbContext(options);

    private abstract class GuidMigrationContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<TextGuidDocument> Documents => Set<TextGuidDocument>();

        protected abstract bool UseNativeChar36 { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TextGuidDocument>(entity =>
            {
                entity.ToTable("TextGuidDocuments");
                entity.HasKey(document => document.Id);
                ConfigureGuid(entity.Property(document => document.Id));
            });

            modelBuilder.Entity<TextGuidDocumentRevision>(entity =>
            {
                entity.ToTable("TextGuidDocumentRevisions");
                entity.HasKey(revision => revision.Id);
                ConfigureGuid(entity.Property(revision => revision.DocumentId));
                entity.HasIndex(revision => revision.DocumentId);
                entity
                    .HasOne(revision => revision.Document)
                    .WithMany(document => document.Revisions)
                    .HasForeignKey(revision => revision.DocumentId)
                    .HasConstraintName(ForeignKeyName)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private void ConfigureGuid(
            PropertyBuilder<Guid> property
        )
        {
            if (UseNativeChar36)
            {
                property.HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                return;
            }

            property
                .HasConversion<string>()
                .HasColumnType("varchar(36)")
                .HasMaxLength(36)
                .IsUnicode(false);
        }
    }

    private sealed class ConvertedGuidContext(DbContextOptions<ConvertedGuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override bool UseNativeChar36 => false;
    }

    private sealed class NativeChar36GuidContext(DbContextOptions<NativeChar36GuidContext> options)
        : GuidMigrationContext(options)
    {
        protected override bool UseNativeChar36 => true;
    }

    private sealed class TextGuidDocument
    {
        public Guid Id { get; set; }

        public ICollection<TextGuidDocumentRevision> Revisions { get; set; } = [];
    }

    private sealed class TextGuidDocumentRevision
    {
        public int Id { get; set; }

        public Guid DocumentId { get; set; }

        public TextGuidDocument Document { get; set; } = null!;
    }
}
