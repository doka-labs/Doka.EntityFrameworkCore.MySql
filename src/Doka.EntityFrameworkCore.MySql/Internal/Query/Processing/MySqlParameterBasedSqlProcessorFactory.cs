namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates provider-specific parameter-based SQL processors.
/// </summary>
internal sealed class MySqlParameterBasedSqlProcessorFactory : IRelationalParameterBasedSqlProcessorFactory
{
    private readonly RelationalParameterBasedSqlProcessorDependencies _dependencies;

    public MySqlParameterBasedSqlProcessorFactory(
        RelationalParameterBasedSqlProcessorDependencies dependencies
    )
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public RelationalParameterBasedSqlProcessor Create(
        RelationalParameterBasedSqlProcessorParameters parameters
    ) => new MySqlParameterBasedSqlProcessor(_dependencies, parameters);
}
