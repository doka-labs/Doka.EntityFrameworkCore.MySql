namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Makes the approved repo-local compatibility matrix explicit and version-sensitive in live tests.
/// </summary>
public sealed class RepoLocalCompatibilityMatrixTests
{
    /// <summary>
    /// Verifies the explicit matrix contract for MySQL 8.0.
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
        Assert.Equal(usesJsonAlias, detectedServerVersion.Capabilities.UsesJsonAliasForJsonColumns);
        Assert.Equal(supportsNativeJsonType, detectedServerVersion.Capabilities.SupportsNativeJsonType);
    }
}
