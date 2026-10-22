namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies provider-native Guid store-type transitions against live database engines.
/// </summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlGuidMigrationIntegrationTests
{
    private const string ForeignKeyName = "FK_TextGuidDocumentRevisions_TextGuidDocuments_DocumentId";

    private const string StorageTransitionTable = "GuidStorageTransitionRecords";

    private static readonly Guid s_documentId = Guid.Parse("b70d9279-dc5e-4cd5-9cd3-a983613aaed7");

    private static readonly Guid s_secondDocumentId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    // WHY: Topic partials share the original class-owned schema selected by CallerFilePath.
    private static string GetConnectionString(
        IntegrationDatabaseTarget target
    ) => IntegrationTestEnvironment.GetConnectionString(target);

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
}
