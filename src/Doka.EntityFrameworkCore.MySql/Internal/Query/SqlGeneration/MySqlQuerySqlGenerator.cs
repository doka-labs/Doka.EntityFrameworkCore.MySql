namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator : QuerySqlGenerator
{
    private const string OffsetWithoutLimitSentinel = "18446744073709551615";

    private readonly MySqlSingletonOptions _singletonOptions;
    private TableExpression? _mutationTargetTable;
    private string? _unqualifiedTableAlias;

    public MySqlQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        MySqlSingletonOptions singletonOptions
    ) : base(dependencies)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

}
