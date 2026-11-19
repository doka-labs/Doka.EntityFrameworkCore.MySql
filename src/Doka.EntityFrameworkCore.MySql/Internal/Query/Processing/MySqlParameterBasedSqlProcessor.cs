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
    protected override Expression ProcessSqlNullability(
        Expression queryExpression,
        ParametersCacheDecorator parametersDecorator
    ) => new MySqlWindowSqlNullabilityProcessor(Dependencies, Parameters).Process(queryExpression, parametersDecorator);

    /// <inheritdoc />
    public override Expression Process(
        Expression queryExpression,
        ParametersCacheDecorator parametersDecorator
    )
    {
        if (queryExpression is MySqlInsertSelectExpression insert)
        {
            return insert.Update((SelectExpression)Process(insert.Source, parametersDecorator));
        }

        var lockingMode = queryExpression is SelectExpression input
            ? MySqlLockingReadMetadata.GetMode(input)
            : MySqlLockingReadMode.None;

        var processed = base.Process(queryExpression, parametersDecorator);
        processed = new MySqlLimitExpressionEvaluatingExpressionVisitor(_sqlExpressionFactory, parametersDecorator)
            .Visit(
                processed);

        if (lockingMode == MySqlLockingReadMode.None)
        {
            return processed;
        }

        // WHY: Parameter pruning may rebuild the SELECT. Preserve a qualified direct block
        // or reject the rewritten shape; never silently execute an unguarded locking command.
        return processed is SelectExpression select
            ? MySqlLockingReadQueryValidator.ValidateProcessedSelect(select, lockingMode)
            : throw new InvalidOperationException(
                "Parameter processing removed the locking read's direct query scope.");
    }
}
