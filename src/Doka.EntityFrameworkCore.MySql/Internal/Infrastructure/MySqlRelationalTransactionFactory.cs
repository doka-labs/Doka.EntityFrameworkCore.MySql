namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalTransactionFactory : RelationalTransactionFactory
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlRelationalTransactionFactory(
        RelationalTransactionFactoryDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        _singletonOptions = (singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions)))
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public override RelationalTransaction Create(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(logger);

        return new MySqlRelationalTransaction(
            connection,
            transaction,
            transactionId,
            logger,
            transactionOwned,
            Dependencies.SqlGenerationHelper,
            _singletonOptions);
    }
}
