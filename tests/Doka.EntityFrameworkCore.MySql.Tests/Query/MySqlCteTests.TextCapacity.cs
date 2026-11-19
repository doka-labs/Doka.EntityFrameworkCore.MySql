namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlCteTests
{
    /// <summary>Gets actual Unicode scalar variants whose native character counts differ from UTF-16 length.</summary>
    public static TheoryData<string, int> LiteralGrowthCases => new()
    {
        { "", 0 },
        { "ab", 2 },
        { "\U0001F600", 1 },
        { "e\u0301", 2 },
    };

    /// <summary>The actual growth classifier uses native character units rather than UTF-16 code units.</summary>
    [Theory]
    [MemberData(nameof(LiteralGrowthCases))]
    public void Literal_recursive_growth_counts_unicode_scalar_characters(
        string suffix,
        int expectedGrowth
    )
    {
        // Arrange
        var frontier = new ColumnExpression("Value",
            "frontier",
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: false);

        var literal = new SqlConstantExpression(suffix, MySqlStringTypeMapping.Default);
        var expression = new SqlBinaryExpression(ExpressionType.Add,
            frontier,
            literal,
            typeof(string),
            MySqlStringTypeMapping.Default);

        // Act
        var supported = MySqlRecursiveTextCapacity.TryLinearGrowth(expression,
            "frontier",
            "Value",
            null,
            out var actualGrowth);

        // Assert
        Assert.True(supported);
        Assert.Equal(expectedGrowth, actualGrowth);
    }

    /// <summary>A literal converter can expand the provider value, invalidating a bound based on its model text.</summary>
    [Fact]
    public void Converted_recursive_literal_is_not_treated_as_unconverted_character_capacity()
    {
        // Arrange
        var converter = new ValueConverter<string, string>(
            value => "expanded:" + value,
            value => value.Substring(9));

        var converted = (RelationalTypeMapping)MySqlStringTypeMapping.Default.WithComposedConverter(converter);
        var providerLiteral = converted.GenerateSqlLiteral("x");
        var frontier = new ColumnExpression("Value",
            "frontier",
            typeof(string),
            MySqlStringTypeMapping.Default,
            nullable: false);

        var literal = new SqlConstantExpression("x", converted);
        var expression = new SqlBinaryExpression(ExpressionType.Add,
            frontier,
            literal,
            typeof(string),
            MySqlStringTypeMapping.Default);

        // Act
        var supported = MySqlRecursiveTextCapacity.TryLinearGrowth(expression, "frontier", "Value", null, out _);

        // Assert
        Assert.Contains("expanded:x", providerLiteral, StringComparison.Ordinal);
        Assert.False(supported);
    }
}
