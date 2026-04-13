namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalConnection : RelationalConnection
{
    private readonly IMySqlDriverFacade _driverFacade;
    private readonly MySqlOptionsExtension _optionsExtension;

    public MySqlRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IMySqlDriverFacade driverFacade
    ) : base(dependencies)
    {
        _driverFacade = driverFacade ?? throw new ArgumentNullException(nameof(driverFacade));
        _optionsExtension = dependencies.ContextOptions.FindExtension<MySqlOptionsExtension>()
            ?? throw new InvalidOperationException("The Doka MySQL options extension is not configured.");
    }

    protected override bool SupportsAmbientTransactions => true;

    protected override DbConnection CreateDbConnection()
    {
        if (_optionsExtension.DataSource is not null)
        {
            return _optionsExtension.DataSource.CreateConnection();
        }

        if (_optionsExtension.Connection is not null)
        {
            return _optionsExtension.Connection;
        }

        if (string.IsNullOrWhiteSpace(_optionsExtension.ConnectionString))
        {
            throw new InvalidOperationException(
                "A MySQL connection string, DbConnection, or MySqlDataSource must be configured.");
        }

        var connectionStringBuilder = new MySqlConnectionStringBuilder(_optionsExtension.ConnectionString)
        {
            GuidFormat = _optionsExtension.DefaultGuidFormat switch
            {
                MySqlGuidFormat.Char36 => MySqlConnector.MySqlGuidFormat.Char36,
                _ => MySqlConnector.MySqlGuidFormat.Binary16,
            },
        };

        return _driverFacade.CreateConnection(connectionStringBuilder.ConnectionString);
    }
}
