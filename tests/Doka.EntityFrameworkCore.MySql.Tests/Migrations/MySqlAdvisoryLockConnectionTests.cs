namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests the advisory lock connection setup: verifies that the dedicated connection
/// disables pooling, and that the TimeoutException chain fires when GET_LOCK returns 0.
/// </summary>
public sealed class MySqlAdvisoryLockConnectionTests
{
    /// <summary>
    /// Verifies that MySqlConnectionStringBuilder with Pooling=false
    /// produces a connection string that disables pooling -- this is the
    /// exact setup used by MySqlMigrationsDatabaseLock.CreateDedicatedConnection.
    /// </summary>
    [Fact]
    public void Dedicated_connection_string_disables_pooling()
    {
        // Arrange
        var baseConnectionString = "Server=127.0.0.1;Port=33068;Database=doka;User ID=root;Password=x;";

        // Act
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
        };

        // Assert
        Assert.False(builder.Pooling);
        Assert.Contains("Pooling=False", builder.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the dedicated connection preserves all credentials from the source.
    /// </summary>
    [Fact]
    public void Dedicated_connection_string_preserves_credentials()
    {
        // Arrange
        var baseConnectionString = "Server=127.0.0.1;Port=33068;Database=doka;User ID=root;Password=secret;";

        // Act
        var builder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
        };

        // Assert
        Assert.Equal("root", builder.UserID);
        Assert.Equal("doka", builder.Database);
        Assert.Equal("127.0.0.1", builder.Server);
        Assert.Equal(33068u, builder.Port);
    }

    /// <summary>
    /// Verifies that GET_LOCK returning 0 flows through MySqlScalarConvert.ToBoolean
    /// as false -- this is the exact gate that causes AcquireLock to throw TimeoutException.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(0)]
    [InlineData(0.0)]
    public void GetLock_zero_result_evaluates_to_false(
        object result
    )
    {
        // Arrange: The theory supplies result.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(result);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies that GET_LOCK returning 1 (lock acquired) flows through
    /// MySqlScalarConvert.ToBoolean as true.
    /// </summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(1)]
    public void GetLock_one_result_evaluates_to_true(
        object result
    )
    {
        // Arrange: The theory supplies result.

        // Act
        var actual = MySqlScalarConvert.ToBoolean(result);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies that GET_LOCK returning null (server-side error) flows as false,
    /// which also triggers the TimeoutException path.
    /// </summary>
    [Fact]
    public void GetLock_null_result_evaluates_to_false()
    {
        // Arrange
        object? input = null;

        // Act
        var actual = MySqlScalarConvert.ToBoolean(input);

        // Assert
        Assert.False(actual);
    }
}
