namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Keeps provider functions in the query tree until provider translation runs.
/// </summary>
internal sealed class MySqlEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    /// <inheritdoc />
    public bool IsEvaluatableExpression(
        Expression expression
    ) => expression is not MethodCallExpression methodCall
        || methodCall.Method.DeclaringType != typeof(MySqlDbFunctionsExtensions);
}
