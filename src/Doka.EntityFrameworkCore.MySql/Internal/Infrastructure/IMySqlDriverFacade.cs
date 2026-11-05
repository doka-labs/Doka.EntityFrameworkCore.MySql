namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlDriverFacade
{
    string DriverName { get; }

    DbConnection CreateConnection(
        string connectionString
    );
}
