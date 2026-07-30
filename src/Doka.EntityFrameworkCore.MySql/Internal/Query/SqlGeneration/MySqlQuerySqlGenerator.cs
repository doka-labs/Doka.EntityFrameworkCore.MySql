namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator : QuerySqlGenerator
{
    private const string OffsetWithoutLimitSentinel = "18446744073709551615";

    private readonly MySqlSingletonOptions _singletonOptions;
    private TableExpression? _mutationTargetTable;
    private string? _unqualifiedTableAlias;

    private ProviderProfile Profile => _singletonOptions.Profile
        ?? throw new InvalidOperationException(
            "The provider profile must be initialized before SQL generation.");

    public MySqlQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        MySqlSingletonOptions singletonOptions
    ) : base(dependencies)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

}
