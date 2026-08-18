using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies provider-native Guid store-type transitions against live database engines.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlGuidMigrationIntegrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";
    private static readonly Guid s_documentId = Guid.Parse("b70d9279-dc5e-4cd5-9cd3-a983613aaed7");

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
            + "DROP TABLE IF EXISTS `TextGuidDocuments`;";
        _ = await command.ExecuteNonQueryAsync();
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => IntegrationTestDbContextOptions
        .Create<TContext>()
        .UseMySql(connectionString, serverVersion)
        .Options;

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
