using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies the shared semantic SQL-assertion contract used by translation tests.
/// </summary>
public sealed class MySqlSqlAssertTests
{
    /// <summary>
    /// Verifies both equivalent local current-timestamp spellings.
    /// </summary>
    [Theory]
    [InlineData("SELECT NOW(6)")]
    [InlineData("SELECT CURRENT_TIMESTAMP(6)")]
    public void Local_current_timestamp_accepts_equivalent_spellings(
        string sql
    )
    {
        // Arrange
        const int precision = 6;

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsCurrentTimestamp(sql, utc: false, precision));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that omitting the required fractional precision is rejected.
    /// </summary>
    [Fact]
    public void Current_timestamp_requires_the_requested_precision()
    {
        // Arrange
        const string sql = "SELECT NOW()";

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsCurrentTimestamp(sql, utc: false, precision: 6));

        // Assert
        Assert.IsAssignableFrom<XunitException>(exception);
    }

    /// <summary>
    /// Verifies both equivalent natural-logarithm spellings.
    /// </summary>
    [Theory]
    [InlineData("SELECT LN(`Value`)")]
    [InlineData("SELECT LOG(`Value`)")]
    public void Natural_logarithm_accepts_equivalent_spellings(
        string sql
    )
    {
        // Arrange: The theory supplies the complete SQL input.

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsNaturalLogarithm(sql));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that a different logarithm function cannot satisfy the contract.
    /// </summary>
    [Fact]
    public void Natural_logarithm_rejects_base_specific_logarithms()
    {
        // Arrange
        const string sql = "SELECT LOG10(`Value`)";

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsNaturalLogarithm(sql));

        // Assert
        Assert.IsAssignableFrom<XunitException>(exception);
    }

    /// <summary>
    /// Verifies that interval text without a <c>DATE_ADD</c> call is rejected.
    /// </summary>
    [Fact]
    public void Date_add_requires_function_interval_and_unit()
    {
        // Arrange
        const string sql = "SELECT INTERVAL 1 DAY";

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsDateAdd(sql, "DAY"));

        // Assert
        Assert.IsAssignableFrom<XunitException>(exception);
    }

    /// <summary>
    /// Verifies each engine's regular-expression spelling independently.
    /// </summary>
    [Theory]
    [InlineData("SELECT REGEXP_LIKE(`Value`, '^a')", false)]
    [InlineData("SELECT `Value` REGEXP '^a'", true)]
    public void Regular_expression_accepts_the_matching_engine_dialect(
        string sql,
        bool mariaDb
    )
    {
        // Arrange: The theory supplies the SQL and its owning engine family.

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsRegularExpression(sql, mariaDb));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// Rejects MariaDB-only syntax when checking a MySQL translation.
    /// </summary>
    [Fact]
    public void Regular_expression_rejects_the_other_engine_dialect()
    {
        // Arrange
        const string sql = "SELECT `Value` REGEXP '^a'";

        // Act
        var exception = Record.Exception(() => MySqlSqlAssert.ContainsRegularExpression(sql, mariaDb: false));

        // Assert
        Assert.IsAssignableFrom<XunitException>(exception);
    }
}
