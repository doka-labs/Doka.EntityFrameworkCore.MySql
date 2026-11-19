namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>Quoted recursive leaves retain parameters and identity without embedding their own definition.</summary>
    [Fact]
    [Experimental("EF9100")]
    public void Recursive_frontier_quote_is_finite_and_retains_its_typed_depth()
    {
        // Arrange
        using var context = CreateContext();
        var depth = new SqlConstantExpression(4, IntTypeMapping.Default);
        var frontier = new MySqlRecursiveCteReferenceExpression("frontier", 73, depth);
        var binding = new TypeMappingSourceBindingExpressionVisitor(context.GetService<IRelationalTypeMappingSource>());

        // Act
        var reconstructed = Expression
            .Lambda<Func<MySqlRecursiveCteReferenceExpression>>(binding.Visit(frontier.Quote()))
            .Compile()();

        // Assert
        Assert.Equal("frontier", reconstructed.Alias);
        Assert.Equal(73, reconstructed.BindingId);
        Assert.Equal(4,
            Assert.IsType<SqlConstantExpression>(reconstructed.MaximumDepth)
                .Value);

        Assert.Equal(typeof(int), reconstructed.MaximumDepth.Type);
    }

    /// <summary>Aliasing changes only the native reference alias and preserves its exact depth expression.</summary>
    [Fact]
    public void Recursive_frontier_aliasing_retains_the_depth_identity()
    {
        // Arrange
        var depth = new SqlConstantExpression(4, IntTypeMapping.Default);
        var frontier = new MySqlRecursiveCteReferenceExpression("first", 73, depth);

        // Act
        var aliased = Assert.IsType<MySqlRecursiveCteReferenceExpression>(frontier.WithAlias("second"));

        // Assert
        Assert.Equal("second", aliased.Alias);
        Assert.Equal("first", frontier.Alias);
        Assert.Equal(73, aliased.BindingId);
        Assert.Same(depth, aliased.MaximumDepth);
    }

    /// <summary>Invalid internal depth types cannot reach native recursion SQL.</summary>
    [Fact]
    public void Recursive_frontier_rejects_a_nonintegral_depth()
    {
        // Arrange
        var depth = new SqlConstantExpression("4", MySqlStringTypeMapping.Default);

        void act() => _ = new MySqlRecursiveCteReferenceExpression("frontier", 73, depth);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Contains("integer",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Negative constants are rejected even when internal construction bypasses the public method.</summary>
    [Fact]
    public void Recursive_frontier_rejects_a_negative_constant_depth()
    {
        // Arrange
        var depth = new SqlConstantExpression(-1, IntTypeMapping.Default);

        void act() => _ = new MySqlRecursiveCteReferenceExpression("frontier", 73, depth);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal("maximumDepth",
            Assert.IsType<ArgumentOutOfRangeException>(exception)
                .ParamName);
    }
}
