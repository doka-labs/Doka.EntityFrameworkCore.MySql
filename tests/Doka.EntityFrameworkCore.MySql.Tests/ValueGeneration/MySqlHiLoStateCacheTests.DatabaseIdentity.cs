namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlHiLoStateCacheTests
{
    /// <summary>
    /// Verifies physical database identity retains routing fields while excluding credentials.
    /// </summary>
    [Fact]
    public void Database_identity_excludes_password()
    {
        // Arrange
        var first = MySqlDatabaseIdentity.FromConnectionString(
            "Server=db.example;Port=3307;Database=orders;User ID=app;Password=top-secret;");

        // Act
        var second = MySqlDatabaseIdentity.FromConnectionString(
            "Server=db.example;Port=3307;Database=orders;User ID=app;Password=another-secret;");

        // Assert
        Assert.Equal(first, second);
        Assert.Equal("db.example", first.Server);
        Assert.Equal((uint)3307, first.Port);
        Assert.Equal("orders", first.Database);
        Assert.Equal("app", first.UserId);
        Assert.DoesNotContain("top-secret", first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", second.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Distinct named pipes are distinct physical endpoints even when every
    /// logical database field is identical.
    /// </summary>
    [Fact]
    public void Database_identity_distinguishes_named_pipe_endpoints()
    {
        // Arrange
        var first = MySqlDatabaseIdentity.FromConnectionString(
            "Server=localhost;Database=orders;User ID=app;Connection Protocol=Pipe;Pipe Name=orders-a;");

        // Act
        var second = MySqlDatabaseIdentity.FromConnectionString(
            "Server=localhost;Database=orders;User ID=app;Connection Protocol=Pipe;Pipe Name=orders-b;");

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.PipeName, second.PipeName);
    }

    /// <summary>
    /// Distinct Unix socket paths remain isolated through the normalized server
    /// endpoint while retaining the connector protocol in the identity.
    /// </summary>
    [Fact]
    public void Database_identity_distinguishes_unix_socket_endpoints()
    {
        // Arrange
        var first = MySqlDatabaseIdentity.FromConnectionString(
            "Server=/var/run/mysql-a.sock;Database=orders;User ID=app;Connection Protocol=Unix;");

        // Act
        var second = MySqlDatabaseIdentity.FromConnectionString(
            "Server=/var/run/mysql-b.sock;Database=orders;User ID=app;Connection Protocol=Unix;");

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Server, second.Server);
        Assert.Equal(first.ConnectionProtocol, second.ConnectionProtocol);
    }

    /// <summary>
    /// Equivalent connection-string spellings still share one normalized physical
    /// endpoint identity and therefore one HiLo state.
    /// </summary>
    [Fact]
    public void Database_identity_normalizes_equivalent_named_pipe_settings()
    {
        // Arrange
        var first = MySqlDatabaseIdentity.FromConnectionString(
            "Server=localhost;Database=orders;User ID=app;Connection Protocol=Pipe;Pipe Name=orders;");

        // Act
        var second = MySqlDatabaseIdentity.FromConnectionString(
            "Data Source=localhost;Initial Catalog=orders;Uid=app;Protocol=Pipe;Pipe=orders;");

        // Assert
        Assert.Equal(first, second);
    }
}
