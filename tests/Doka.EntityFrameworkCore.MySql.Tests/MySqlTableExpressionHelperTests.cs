namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies shared relational table-expression operations.
/// </summary>
public sealed class MySqlTableExpressionHelperTests
{
    /// <summary>
    /// Verifies that direct and nested join sources expose distinct underlying
    /// aliases without relying on recursive traversal.
    /// </summary>
    [Fact]
    public void Collect_returns_distinct_aliases_from_direct_and_nested_join_sources()
    {
        var arguments = Expression.Constant(Array.Empty<object>());
        var direct = new FromSqlExpression("direct", "SELECT 1", arguments);
        var nested = new CrossJoinExpression(
            new CrossJoinExpression(new FromSqlExpression("joined", "SELECT 1", arguments)));

        var aliases = MySqlTableExpressionHelper.CollectAliases(
        [
            direct,
            nested,
            direct,
        ]);

        Assert.Equal(
            [
                "direct",
                "joined",
            ],
            aliases.Order(StringComparer.Ordinal));
    }
}
