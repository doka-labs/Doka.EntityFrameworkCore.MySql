namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _singletonOptions = (singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions)))
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    public QuerySqlGenerator Create() => new MySqlQuerySqlGenerator(_dependencies, _singletonOptions);
}
