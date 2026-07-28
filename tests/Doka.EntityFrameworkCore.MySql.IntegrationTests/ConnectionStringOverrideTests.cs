namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies that EF Core runtime connection-string overrides remain authoritative
/// for server-level provider operations such as database creation.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class ConnectionStringOverrideTests
{
    /// <summary>
    /// Verifies the EF tooling connection override contract on MySQL 8.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public Task Database_creator_honors_connection_override_on_mysql84() =>
        VerifyConnectionOverrideAsync(IntegrationDatabaseTarget.MySql84);

    /// <summary>
    /// Verifies the EF tooling connection override contract on MariaDB 11.4.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public Task Database_creator_honors_connection_override_on_mariadb114() =>
        VerifyConnectionOverrideAsync(IntegrationDatabaseTarget.MariaDb114);

    /// <summary>
    /// Verifies the EF tooling connection override contract on MariaDB 11.8.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public Task Database_creator_honors_connection_override_on_mariadb118() =>
        VerifyConnectionOverrideAsync(IntegrationDatabaseTarget.MariaDb118);

    private static async Task VerifyConnectionOverrideAsync(
        IntegrationDatabaseTarget target
    )
    {
        var baseConnectionString = IntegrationTestEnvironment.GetConnectionString(target);
        var databaseName = $"doka_connection_override_{Guid.NewGuid():N}"[..48];
        var overriddenConnectionString = IntegrationDatabaseUtilities.BuildConnectionString(
            baseConnectionString,
            databaseName);
        var serverVersion = MySqlServerVersion.AutoDetect(
            IntegrationTestEnvironment.CreateRequest(target)
                .ServerVersionToken);

        try
        {
            await using var context = new ConnectionOverrideContext(
                new DbContextOptionsBuilder<ConnectionOverrideContext>().UseMySql(baseConnectionString, serverVersion)
                    .Options);
            context.Database.SetConnectionString(overriddenConnectionString);

            var databaseCreator = context.GetService<IRelationalDatabaseCreator>();

            Assert.False(
                await databaseCreator
                    .ExistsAsync()
                    .ConfigureAwait(false));

            await databaseCreator
                .CreateAsync()
                .ConfigureAwait(false);

            Assert.True(
                await databaseCreator
                    .ExistsAsync()
                    .ConfigureAwait(false));

            await context
                .Database.OpenConnectionAsync()
                .ConfigureAwait(false);

            try
            {
                await using var command = context
                    .Database.GetDbConnection()
                    .CreateCommand();
                command.CommandText = "SELECT DATABASE();";

                Assert.Equal(
                    databaseName,
                    Convert.ToString(
                        await command
                            .ExecuteScalarAsync()
                            .ConfigureAwait(false),
                        CultureInfo.InvariantCulture));
            }
            finally
            {
                await context
                    .Database.CloseConnectionAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await IntegrationDatabaseUtilities
                .DropDatabaseAsync(overriddenConnectionString)
                .ConfigureAwait(false);
        }
    }

    private sealed class ConnectionOverrideContext : DbContext
    {
        public ConnectionOverrideContext(
            DbContextOptions<ConnectionOverrideContext> options
        ) : base(options) { }
    }
}
