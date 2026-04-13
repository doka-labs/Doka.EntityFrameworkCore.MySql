namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Validates the contract for externally supplied integration-test connection strings.
/// </summary>
public sealed class IntegrationConnectionStringTests
{
    /// <summary>
    /// Verifies that the resolved MySQL 8.0 integration-test connection string is parseable.
    /// </summary>
    [Fact]
    public void Resolved_mysql80_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql80);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MySQL 8.4 integration-test connection string is parseable.
    /// </summary>
    [Fact]
    public void Resolved_mysql84_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MySql84);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 11.8 integration-test connection string is parseable.
    /// </summary>
    [Fact]
    public void Resolved_mariadb118_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb118);

        AssertConnectionString(connectionString);
    }

    /// <summary>
    /// Verifies that the resolved MariaDB 11.4 integration-test connection string is parseable.
    /// </summary>
    [Fact]
    public void Resolved_mariadb114_connection_string_is_parseable()
    {
        var connectionString = IntegrationTestEnvironment.GetConnectionString(IntegrationDatabaseTarget.MariaDb114);

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
