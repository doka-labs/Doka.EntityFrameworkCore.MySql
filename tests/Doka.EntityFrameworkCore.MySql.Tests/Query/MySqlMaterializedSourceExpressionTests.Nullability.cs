namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>Only the actual frontier alias receives conservative later-iteration NULL facts.</summary>
    [Theory]
    [InlineData("frontier", true)]
    [InlineData("ordinary", false)]
    public void Recursive_frontier_nullability_preserves_scope_type_and_mapping(
        string alias,
        bool expectedNullable
    )
    {
        // Arrange
        var original = new ColumnExpression("Value", alias, typeof(int), IntTypeMapping.Default, nullable: false);
        var visitor = new MySqlRecursiveFrontierNullabilityExpressionVisitor("frontier");

        // Act
        var rewritten = Assert.IsType<ColumnExpression>(visitor.Visit(original));

        // Assert
        Assert.Equal(expectedNullable, rewritten.IsNullable);
        Assert.False(original.IsNullable);
        Assert.Equal(original.Type, rewritten.Type);
        Assert.Same(original.TypeMapping, rewritten.TypeMapping);
        Assert.Equal(original.Name, rewritten.Name);
        Assert.Equal(alias, rewritten.TableAlias);
        Assert.Equal(expectedNullable, !ReferenceEquals(original, rewritten));
    }

    /// <summary>An already nullable frontier column is reused without another node or broader rewrite.</summary>
    [Fact]
    public void Already_nullable_frontier_column_is_not_reallocated()
    {
        // Arrange
        var original = new ColumnExpression("Value", "frontier", typeof(int), IntTypeMapping.Default, nullable: true);
        var visitor = new MySqlRecursiveFrontierNullabilityExpressionVisitor("frontier");

        // Act
        var rewritten = visitor.Visit(original);

        // Assert
        Assert.Same(original, rewritten);
    }
}
