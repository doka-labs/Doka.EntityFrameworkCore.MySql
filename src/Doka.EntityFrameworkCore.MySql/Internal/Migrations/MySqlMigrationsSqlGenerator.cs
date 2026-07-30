namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator : MigrationsSqlGenerator
{
    private readonly MySqlSingletonOptions _mySqlSingletonOptions;

    public MySqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> singletonOptions
    ) : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _mySqlSingletonOptions = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    private string DelimitMigrationIdentifier(
        string identifier
    ) => Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier);

    private string DelimitMigrationIdentifier(
        string identifier,
        string? schema
    ) => Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier, schema);
}
