namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Keeps provider functions in the query tree until provider translation runs.
/// </summary>
internal sealed class MySqlEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    /// <inheritdoc />
    public bool IsEvaluatableExpression(
        Expression expression
    ) => expression switch
    {
        MethodCallExpression methodCall => methodCall.Method.DeclaringType != typeof(MySqlDbFunctionsExtensions)
            && methodCall.Method.DeclaringType != typeof(MySqlQueryableExtensions)
            && methodCall.Method.DeclaringType != typeof(MySqlWindowDefinition)
            && methodCall.Method.DeclaringType != typeof(MySqlWindowFrameBound)
            && methodCall.Method.DeclaringType != typeof(MySqlInsertProjectionMarker)
            && methodCall.Method.DeclaringType != typeof(MySqlIndexedSelectLowering),

        // WHY: Frame constants are expression markers too. Evaluating a static bound property
        // early would lose its structural identity before the exact fluent-chain parser sees it.
        MemberExpression member => member.Member.DeclaringType != typeof(MySqlWindowFrameBound),
        _ => true,
    };
}
