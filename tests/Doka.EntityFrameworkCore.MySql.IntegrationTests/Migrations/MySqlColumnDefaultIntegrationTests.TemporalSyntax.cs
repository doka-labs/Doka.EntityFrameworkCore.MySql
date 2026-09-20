namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    /// <summary>MySQL rejects unparenthesized typed temporal defaults before creating a table.</summary>
    [RequiresDatabaseTargetsTheory(new[] { IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97 })]
    [MemberData(nameof(InvalidTemporalDefaultCases))]
    public async Task MySql_rejects_unparenthesized_temporal_default(
        string targetName,
        string storeType,
        string literal
    )
    {
        // Arrange
        await using var connection = CreateConnection(Enum.Parse<IntegrationDatabaseTarget>(targetName));
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var tableName = $"InvalidTemporalDefault_{Guid.NewGuid():N}";
        command.CommandText = $"CREATE TABLE `{tableName}` (`Value` {storeType} DEFAULT {literal});";

        try
        {
            // Act
            var exception = await Assert.ThrowsAsync<MySqlException>(async () =>
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));

            // Assert
            Assert.Equal(1067, exception.Number);
        }
        finally
        {
            command.CommandText = $"DROP TABLE IF EXISTS `{tableName}`;";
            _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
