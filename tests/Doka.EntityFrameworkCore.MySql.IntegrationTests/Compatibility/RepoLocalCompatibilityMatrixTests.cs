namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Makes the approved repo-local compatibility matrix explicit and version-sensitive in live tests.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class RepoLocalCompatibilityMatrixTests
{
    /// <summary>
    /// Verifies the external-only legacy contract for MySQL 8.0.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql80)]
    public Task MySql80_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MySql80,
            expectedVersion: new Version(8, 0, 0),
            isMariaDb: false,
            usesJsonAlias: false,
            supportsNativeJsonType: true);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task MySql84_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MySql84,
            expectedVersion: new Version(8, 4, 0),
            isMariaDb: false,
            usesJsonAlias: false,
            supportsNativeJsonType: true);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MySQL 9.7.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public Task MySql97_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MySql97,
            expectedVersion: new Version(9, 7, 0),
            isMariaDb: false,
            usesJsonAlias: false,
            supportsNativeJsonType: true);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MariaDB 10.11.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public Task MariaDb1011_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MariaDb1011,
            expectedVersion: new Version(10, 11, 0),
            isMariaDb: true,
            usesJsonAlias: true,
            supportsNativeJsonType: false);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task MariaDb114_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MariaDb114,
            expectedVersion: new Version(11, 4, 0),
            isMariaDb: true,
            usesJsonAlias: true,
            supportsNativeJsonType: false);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task MariaDb118_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MariaDb118,
            expectedVersion: new Version(11, 8, 0),
            isMariaDb: true,
            usesJsonAlias: true,
            supportsNativeJsonType: false);
    }

    /// <summary>
    /// Verifies the explicit matrix contract for MariaDB 12.3.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public Task MariaDb123_matrix_contract_is_visible()
    {
        return VerifyMatrixContractAsync(
            IntegrationDatabaseTarget.MariaDb123,
            expectedVersion: new Version(12, 3, 0),
            isMariaDb: true,
            usesJsonAlias: true,
            supportsNativeJsonType: false);
    }

    private static async Task VerifyMatrixContractAsync(
        IntegrationDatabaseTarget target,
        Version expectedVersion,
        bool isMariaDb,
        bool usesJsonAlias,
        bool supportsNativeJsonType
    )
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(target);

        await using var connection = new MySqlConnection(connectionString);
        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        var detectedServerVersion = MySqlServerVersion.AutoDetect(connection);

        Assert.Equal(isMariaDb, detectedServerVersion.IsMariaDb);
        Assert.Equal(expectedVersion.Major, detectedServerVersion.Version.Major);
        Assert.Equal(expectedVersion.Minor, detectedServerVersion.Version.Minor);
        Assert.Equal(
            usesJsonAlias,
            detectedServerVersion.Profile.GetSupport(ProviderCapability.JsonColumns)
            == ProviderSupportStatus.Emulated);
        Assert.Equal(
            supportsNativeJsonType,
            detectedServerVersion.Profile.GetSupport(ProviderCapability.JsonColumns)
            == ProviderSupportStatus.Native);
    }
}
