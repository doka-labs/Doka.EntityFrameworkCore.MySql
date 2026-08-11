namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Validates the contract for externally supplied integration-test connection strings.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class IntegrationConnectionStringTests
{
    /// <summary>
    /// Verifies that the resolved MySQL 8.0 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql80)]
    public void Resolved_mysql80_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql80);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MySQL 8.4 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql84)]
    public void Resolved_mysql84_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MySQL 9.7 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MySql97)]
    public void Resolved_mysql97_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql97);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 10.11 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb1011)]
    public void Resolved_mariadb1011_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb1011);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 11.8 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public void Resolved_mariadb118_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 11.4 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb114)]
    public void Resolved_mariadb114_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 12.3 integration-test connection string is parseable.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb123)]
    public void Resolved_mariadb123_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb123);

        AssertConnectionString(connectionString);
    }

    private static void AssertConnectionString(
        string? connectionString
    )
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new MySqlConnectionStringBuilder(connectionString);

        Assert.False(string.IsNullOrWhiteSpace(builder.Server));
        Assert.False(string.IsNullOrWhiteSpace(builder.Database));
        Assert.False(string.IsNullOrWhiteSpace(builder.UserID));
    }
}
