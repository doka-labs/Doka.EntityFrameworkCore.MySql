namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Applies MySQL-family rewrites that require the concrete query-parameter values.
/// </summary>
internal sealed class MySqlParameterBasedSqlProcessor : RelationalParameterBasedSqlProcessor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlParameterBasedSqlProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters
    ) : base(dependencies, parameters)
    {
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
    }

    /// <inheritdoc />
    public override Expression Process(
        Expression queryExpression,
        ParametersCacheDecorator parametersDecorator
    )
    {
        var processed = base.Process(queryExpression, parametersDecorator);

        return new MySqlLimitExpressionEvaluatingExpressionVisitor(_sqlExpressionFactory, parametersDecorator).Visit(
            processed);
    }
}
