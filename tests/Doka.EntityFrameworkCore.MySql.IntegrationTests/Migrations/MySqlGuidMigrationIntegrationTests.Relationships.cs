namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidMigrationIntegrationTests
{
    /// <summary>
    /// Verifies char36 transition preserves populated relationship for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="target">The database target used by the test.</param>
    private static async Task AssertChar36TransitionPreservesPopulatedRelationshipAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        var connectionString = GetConnectionString(target);
        var serverVersion = IntegrationTestEnvironment.GetServerVersion(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await CleanupAsync(connection, cancellationToken);

        try
        {
            await using var empty = new EmptyGuidContext(
                CreateOptions<EmptyGuidContext>(connectionString, serverVersion));

            await using var converted = new ConvertedGuidContext(
                CreateOptions<ConvertedGuidContext>(connectionString, serverVersion));

            await using var native = new NativeChar36GuidContext(
                CreateOptions<NativeChar36GuidContext>(connectionString, serverVersion));

            await ExecuteOperationsAsync(converted, connection, GetDifferences(empty, converted), cancellationToken);
            converted.Documents.Add(
                new TextGuidDocument
                {
                    Id = s_documentId,
                    Revisions =
                    {
                        new TextGuidDocumentRevision(),
                    },
                });

            await converted.SaveChangesAsync(cancellationToken: cancellationToken);

            // WHY: The Down transition must consume the populated relationship produced by Up.
            // Act
            var upOperations = GetDifferences(converted, native);
            await ExecuteOperationsAsync(native, connection, upOperations, cancellationToken);
            var afterUp = await ReadDatabaseContractAsync(native, connection, cancellationToken);

            var downOperations = GetDifferences(native, converted);
            await ExecuteOperationsAsync(converted, connection, downOperations, cancellationToken);
            var afterDown = await ReadDatabaseContractAsync(converted, connection, cancellationToken);

            // Assert
            AssertForeignKeyLifecycle(upOperations, "varchar(36)", "char(36)");
            AssertForeignKeyLifecycle(downOperations, "char(36)", "varchar(36)");
            AssertDatabaseContract(afterUp);
            AssertDatabaseContract(afterDown);
        }
        finally
        {
            await CleanupAsync(connection, CancellationToken.None);
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

    /// <summary>
    /// Reads database contract for the integration contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel setup and execution.</param>
    /// <param name="context">The context used for the database operation.</param>
    /// <param name="connection">The open database connection.</param>
    private static async Task<(TextGuidDocument Document, bool HasFirstRow, string? Id, string? DocumentId,
        bool HasSecondRow, string? DeleteRule)> ReadDatabaseContractAsync(
        GuidMigrationContext context,
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        context.ChangeTracker.Clear();

        var document = await context
            .Documents
            .Include(item => item.Revisions)
            .SingleAsync(cancellationToken: cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(d.`Id` AS CHAR(36)), CAST(r.`DocumentId` AS CHAR(36)) "
            + "FROM `TextGuidDocuments` AS d "
            + "INNER JOIN `TextGuidDocumentRevisions` AS r ON r.`DocumentId` = d.`Id`;";

        bool hasFirstRow;
        bool hasSecondRow;
        string? id;
        string? documentId;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken: cancellationToken))
        {
            hasFirstRow = await reader.ReadAsync(cancellationToken);
            id = hasFirstRow
                ? reader.GetString(0)
                : null;

            documentId = hasFirstRow
                ? reader.GetString(1)
                : null;

            hasSecondRow = await reader.ReadAsync(cancellationToken);
        }

        command.CommandText = "SELECT rc.`DELETE_RULE` "
            + "FROM information_schema.REFERENTIAL_CONSTRAINTS AS rc "
            + "WHERE rc.`CONSTRAINT_SCHEMA` = DATABASE() "
            + "AND rc.`CONSTRAINT_NAME` = @constraintName;";

        command.Parameters.Clear();
        command.Parameters.AddWithValue("@constraintName", ForeignKeyName);

        var deleteRule = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        return (document, hasFirstRow, id, documentId, hasSecondRow, deleteRule);
    }

    private static void AssertDatabaseContract(
        (TextGuidDocument Document, bool HasFirstRow, string? Id, string? DocumentId,
            bool HasSecondRow, string? DeleteRule) actual
    )
    {
        Assert.Equal(s_documentId, actual.Document.Id);
        Assert.Equal(
            s_documentId,
            Assert.Single(actual.Document.Revisions)
                .DocumentId);

        Assert.True(actual.HasFirstRow);
        Assert.Equal(s_documentId.ToString("D", CultureInfo.InvariantCulture), actual.Id);
        Assert.Equal(s_documentId.ToString("D", CultureInfo.InvariantCulture), actual.DocumentId);
        Assert.False(actual.HasSecondRow);
        Assert.Equal("CASCADE", actual.DeleteRule);
    }
}
