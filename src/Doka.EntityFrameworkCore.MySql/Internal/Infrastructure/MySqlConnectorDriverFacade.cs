namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlConnectorDriverFacade : IMySqlDriverFacade
{
    public string DriverName => "MySqlConnector";

    public DbConnection CreateConnection(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new MySqlConnector.MySqlConnection(connectionString);
    }
}
