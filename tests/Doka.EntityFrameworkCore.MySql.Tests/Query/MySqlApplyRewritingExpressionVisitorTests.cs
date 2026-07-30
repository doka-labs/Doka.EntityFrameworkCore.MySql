namespace Doka.EntityFrameworkCore.MySql.Tests.Query;

/// <summary>
/// Verifies the predicate boundary used when APPLY is flattened into an ordinary join.
/// </summary>
public class MySqlApplyRewritingExpressionVisitorTests
{
    /// <summary>
    /// Verifies every binary predicate shape accepted by EF Core's join nullability
    /// processor and rejects binary forms which that processor cannot consume.
    /// </summary>
    [Theory]
    [InlineData(ExpressionType.Equal, true)]
    [InlineData(ExpressionType.AndAlso, true)]
    [InlineData(ExpressionType.NotEqual, true)]
    [InlineData(ExpressionType.GreaterThan, true)]
    [InlineData(ExpressionType.GreaterThanOrEqual, true)]
    [InlineData(ExpressionType.LessThan, true)]
    [InlineData(ExpressionType.LessThanOrEqual, true)]
    [InlineData(ExpressionType.OrElse, false)]
    [InlineData(ExpressionType.Add, false)]
    public void Can_move_only_supported_binary_predicates_to_join(
        ExpressionType operatorType,
        bool expected
    )
    {
        var operand = new SqlConstantExpression(true, typeMapping: null);
        var predicate = new SqlBinaryExpression(operatorType, operand, operand, typeof(bool), typeMapping: null);

        Assert.Equal(expected, MySqlApplyRewritingExpressionVisitor.CanMovePredicateToJoin(predicate));
    }

    /// <summary>
    /// Verifies the predicate-free and constant forms normalized by the rewriter while
    /// keeping unsupported expression kinds outside the join boundary.
    /// </summary>
    [Fact]
    public void Can_move_only_normalizable_non_binary_predicates_to_join()
    {
        var constant = new SqlConstantExpression(true, typeMapping: null);
        var unary = new SqlUnaryExpression(ExpressionType.Not, constant, typeof(bool), typeMapping: null);

        Assert.True(MySqlApplyRewritingExpressionVisitor.CanMovePredicateToJoin(predicate: null));
        Assert.True(MySqlApplyRewritingExpressionVisitor.CanMovePredicateToJoin(constant));
        Assert.False(MySqlApplyRewritingExpressionVisitor.CanMovePredicateToJoin(unary));
    }
}
