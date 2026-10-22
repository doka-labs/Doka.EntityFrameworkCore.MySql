namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Shares the numeric responsibility without changing the test fixture.</summary>
public sealed partial class MySqlQueryTranslationCoverageTests
{
    // -- Math Function Translations --

    [Fact]
    public void Math_Log10_translates_to_log10()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log10(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LOG10");
    }

    [Fact]
    public void Math_Exp_translates_to_exp()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Exp(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "EXP");
    }

    // Math.Sign already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Sin_translates_to_sin()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Sin(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "SIN");
    }

    [Fact]
    public void Math_Cos_translates_to_cos()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Cos(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "COS");
    }

    [Fact]
    public void Math_Tan_translates_to_tan()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Tan(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "TAN");
    }

    [Fact]
    public void Math_Atan2_translates_to_atan2()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Atan2(e.Score, 1.0))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "ATAN2");
    }

    // Math.Pow, Math.Sqrt, Math.Log (1-arg) already tested in MySqlQueryTranslationExtendedTests.

    [Fact]
    public void Math_Log_two_args_translates_to_log()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Log(e.Score, 2.0))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "LOG");
    }

    [Fact]
    public void Math_Round_one_arg_translates_to_round()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => Math.Round(e.Score))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(sql, "ROUND");
    }

    /// <summary>
    /// Verifies that signed and unsigned shifts apply CLR width masks and that
    /// complement operations do not leak the engines' unsigned 64-bit results.
    /// </summary>
    [Fact]
    public void Signed_integral_bitwise_operations_preserve_clr_semantics()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sql = context
            .Set<CoverageEntity>()
            .Select(e => new
            {
                SignedIntLeft = e.SignedValue << e.ShiftCount,
                SignedIntRight = e.SignedValue >> e.ShiftCount,
                SignedLongLeft = e.SignedLongValue << e.ShiftCount,
                SignedLongRight = e.SignedLongValue >> e.ShiftCount,
                UnsignedIntLeft = e.UnsignedValue << e.ShiftCount,
                UnsignedIntRight = e.UnsignedValue >> e.ShiftCount,
                UnsignedLongLeft = e.UnsignedLongValue << e.ShiftCount,
                UnsignedLongRight = e.UnsignedLongValue >> e.ShiftCount,
                Complement = ~e.SignedValue,
                And = e.SignedValue & e.AltId,
                Or = e.SignedValue | e.AltId,
                Xor = e.SignedValue ^ e.AltId,
            })
            .ToQueryString();

        // Assert
        Assert.Contains("CASE WHEN `c`.`SignedValue` < 0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SIGNED) <<", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS SIGNED) END", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" & 31)", sql, StringComparison.Ordinal);
        Assert.Contains(" & 63)", sql, StringComparison.Ordinal);
        Assert.Contains("4294967295", sql, StringComparison.Ordinal);
        Assert.Contains("2147483648", sql, StringComparison.Ordinal);
        Assert.Contains(" & ", sql, StringComparison.Ordinal);
        Assert.Contains(" | ", sql, StringComparison.Ordinal);
        Assert.Contains(" ^ ", sql, StringComparison.Ordinal);
    }
}
