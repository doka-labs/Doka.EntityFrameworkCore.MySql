namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that one shared server supports independent test-class databases.
/// </summary>
[IntegrationDatabaseTest]
public sealed class IntegrationDatabaseIsolationTests
{
    /// <summary>Verifies different owners can use the same table name without sharing rows.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public async Task Different_owners_can_use_the_same_table_name_without_sharing_rows()
    {
        // Arrange
        var firstConnectionString = IntegrationTestEnvironment.GetConnectionString(
            IntegrationDatabaseTarget.MySql84,
            "/tests/first/IsolationTests.cs");

        var secondConnectionString = IntegrationTestEnvironment.GetConnectionString(
            IntegrationDatabaseTarget.MySql84,
            "/tests/second/IsolationTests.cs");

        await using var firstConnection = new MySqlConnection(firstConnectionString);
        await using var secondConnection = new MySqlConnection(secondConnectionString);
        await firstConnection.OpenAsync(CancellationToken.None);
        await secondConnection.OpenAsync(CancellationToken.None);
        await ExecuteAsync(
            firstConnection,
            "CREATE TABLE `ParallelIsolation` (`Value` int NOT NULL);",
            CancellationToken.None);

        await ExecuteAsync(
            secondConnection,
            "CREATE TABLE `ParallelIsolation` (`Value` int NOT NULL);",
            CancellationToken.None);

        // Act
        await ExecuteAsync(
            firstConnection,
            "INSERT INTO `ParallelIsolation` (`Value`) VALUES (1);",
            CancellationToken.None);

        await ExecuteAsync(
            secondConnection,
            "INSERT INTO `ParallelIsolation` (`Value`) VALUES (2);",
            CancellationToken.None);

        var firstValue = await ReadValueAsync(firstConnection, CancellationToken.None);
        var secondValue = await ReadValueAsync(secondConnection, CancellationToken.None);

        // Assert
        Assert.NotEqual(
            new MySqlConnectionStringBuilder(firstConnectionString).Database,
            new MySqlConnectionStringBuilder(secondConnectionString).Database);

        Assert.Equal(1, firstValue);
        Assert.Equal(2, secondValue);
    }

    /// <summary>Verifies one owner reuses the same database.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public void One_owner_reuses_the_same_database()
    {
        // Arrange
        const string ownerFilePath = "/tests/one/IsolationTests.cs";

        // Act
        var firstConnectionString = IntegrationTestEnvironment.GetConnectionString(
            IntegrationDatabaseTarget.MySql84,
            ownerFilePath);

        var secondConnectionString = IntegrationTestEnvironment.GetConnectionString(
            IntegrationDatabaseTarget.MySql84,
            ownerFilePath);

        // Assert
        Assert.Equal(firstConnectionString, secondConnectionString);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadValueAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand("SELECT `Value` FROM `ParallelIsolation`;", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return Assert.IsType<int>(result);
    }
}
