namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlQueryTranslationExtendedTests
{
    /// <summary>
    /// Math.Pow translates to POWER().
    /// </summary>
    [Fact]
    public void Math_pow_translates_to_power()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => Math.Pow(e.Value, 2))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "POWER");
    }

    /// <summary>
    /// Math.Sqrt translates to SQRT().
    /// </summary>
    [Fact]
    public void Math_sqrt_translates_to_sqrt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => Math.Sqrt(e.Value))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SQRT");
    }

    /// <summary>
    /// Math.Log translates to LN().
    /// </summary>
    [Fact]
    public void Math_log_translates_to_ln()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => Math.Log(e.Value))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsNaturalLogarithm(sql);
    }

    /// <summary>
    /// Math.Sign translates to SIGN().
    /// </summary>
    [Fact]
    public void Math_sign_translates_to_sign()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => Math.Sign(e.Value))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SIGN");
        Assert.Contains("CAST(SIGN(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Least translates its inline value list to native MySQL SQL.
    /// </summary>
    [Fact]
    public void Ef_functions_least_translates_to_native_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(entity => EF.Functions.Least(entity.Id, 100) == entity.Id)
            .ToQueryString();

        // Assert
        Assert.Contains("LEAST(`t`.`Id`, 100)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// EF.Functions.Greatest translates its nullable inline value list to native
    /// MySQL SQL without losing its result type.
    /// </summary>
    [Fact]
    public void Ef_functions_greatest_translates_nullable_values_to_native_function()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Where(
                entity => EF.Functions.Greatest(
                    (int?)entity.Id,
                    100) == entity.Id)
            .ToQueryString();

        // Assert
        Assert.Contains("GREATEST(`t`.`Id`, 100)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Numeric coalesce preserves the CLR promotion instead of inheriting the
    /// unsigned column's result mapping.
    /// </summary>
    [Fact]
    public void Numeric_coalesce_with_conversion_preserves_promoted_type()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => e.NullableUnsignedValue ?? 2.25)
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "COALESCE");

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS DOUBLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Floating-point to decimal query casts preserve the CLR decimal range and scale
    /// instead of inheriting the provider's schema-column default.
    /// </summary>
    [Fact]
    public void Double_to_decimal_cast_uses_lossless_query_precision()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Items.Select(e => (decimal)e.Value)
            .ToQueryString();

        // Assert
        Assert.Contains(" AS DECIMAL(65,30))", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Culture-sensitive Parse overloads remain client-only because MySQL CAST
    /// cannot reproduce arbitrary CLR format-provider semantics.
    /// </summary>
    [Fact]
    public void Numeric_parse_with_format_provider_remains_client_evaluated()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context.Items
            .Select(entity => int.Parse(entity.Name, CultureInfo.InvariantCulture))
            .ToQueryString();

        // Assert
        Assert.Contains("`t`.`Name`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(", sql, StringComparison.OrdinalIgnoreCase);
    }
}
