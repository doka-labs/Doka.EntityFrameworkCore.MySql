namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlCteTests
{
    /// <summary>Missing recursive API arguments fail before any foreign provider can be invoked.</summary>
    [Theory]
    [InlineData("anchor")]
    [InlineData("recursiveStep")]
    [InlineData("compose")]
    public void Null_recursive_inputs_are_rejected(
        string parameter
    )
    {
        // Arrange
        var source = s_values.AsQueryable();
        Action act = parameter switch
        {
            "anchor" => () => MySqlQueryableExtensions.WithRecursiveCte<int, int>(null!, rows => rows, rows => rows, 1),
            "recursiveStep" => () => source.WithRecursiveCte(null!, rows => rows, 1),
            _ => () => source.WithRecursiveCte<int, int>(rows => rows, null!, 1),
        };

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal(parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>A negative semantic depth cannot issue a command or be hidden by another provider failure.</summary>
    [Fact]
    public void Negative_recursive_depth_is_rejected_before_provider_validation()
    {
        // Arrange
        var source = s_values.AsQueryable();

        void act() => source.WithRecursiveCte(rows => rows, rows => rows, -1);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Equal("maximumDepth",
            Assert.IsType<ArgumentOutOfRangeException>(exception)
                .ParamName);
    }

    /// <summary>A local sequence cannot silently evaluate the recursive callback in application memory.</summary>
    [Fact]
    public void In_memory_recursive_source_is_rejected()
    {
        // Arrange
        var source = s_values.AsQueryable();

        void act() => source.WithRecursiveCte(rows => rows.Select(value => value + 1), rows => rows, 2);

        // Act
        var exception = Record.Exception(act);

        // Assert
        Assert.Contains("provider",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The recursive marker has stable closed-method identity and never invokes a data command.</summary>
    [Fact]
    public void Recursive_construction_retains_the_deferred_expression_shape()
    {
        // Arrange
        using var context = new CteGuardContext();
        var source = context
            .Set<CteGuardRow>()
            .Select(row => row.Id);

        var previous = source.WithRecursiveCte(rows => rows.Select(value => value + 1), rows => rows, 3);

        // Act
        var actual = source.WithRecursiveCte(rows => rows.Select(value => value + 1), rows => rows, 3);

        // Assert
        Assert.Same(source.Provider, actual.Provider);
        Assert.True(ExpressionEqualityComparer.Instance.Equals(previous.Expression, actual.Expression));
        Assert.Equal(typeof(IQueryable<int>), actual.Expression.Type);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
