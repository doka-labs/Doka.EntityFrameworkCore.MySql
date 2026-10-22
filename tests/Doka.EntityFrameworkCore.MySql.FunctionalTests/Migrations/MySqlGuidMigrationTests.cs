namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies migration contracts for provider-native Guid storage transitions.
/// </summary>
public sealed partial class MySqlGuidMigrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";
    private const string BinaryOverrideDefault = "5100da31-7b88-45e7-9606-08edfe8f7dab";
}
