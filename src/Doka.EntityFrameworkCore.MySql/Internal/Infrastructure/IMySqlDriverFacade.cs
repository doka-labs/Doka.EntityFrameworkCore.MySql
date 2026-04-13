namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlDriverFacade
{
    DbConnection CreateConnection(
        string connectionString
    );

    string DriverName { get; }
}
