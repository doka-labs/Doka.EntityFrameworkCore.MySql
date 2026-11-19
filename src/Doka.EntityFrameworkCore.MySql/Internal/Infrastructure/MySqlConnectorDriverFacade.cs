namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlConnectorDriverFacade : IMySqlDriverFacade
{
    public DbConnection CreateConnection(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new MySqlConnection(connectionString);
    }
}
