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
    ) => MySqlSqlAssert.ContainsCurrentTimestamp(sql, utc: false, precision: 6);

    /// <summary>
    /// Verifies that omitting the required fractional precision is rejected.
    /// </summary>
    [Fact]
    public void Current_timestamp_requires_the_requested_precision() => Assert.ThrowsAny<XunitException>(() =>
        MySqlSqlAssert.ContainsCurrentTimestamp("SELECT NOW()", utc: false, precision: 6));

    /// <summary>
    /// Verifies both equivalent natural-logarithm spellings.
    /// </summary>
    [Theory]
    [InlineData("SELECT LN(`Value`)")]
    [InlineData("SELECT LOG(`Value`)")]
    public void Natural_logarithm_accepts_equivalent_spellings(
        string sql
    ) => MySqlSqlAssert.ContainsNaturalLogarithm(sql);

    /// <summary>
    /// Verifies that a different logarithm function cannot satisfy the contract.
    /// </summary>
    [Fact]
    public void Natural_logarithm_rejects_base_specific_logarithms() =>
        Assert.ThrowsAny<XunitException>(() => MySqlSqlAssert.ContainsNaturalLogarithm("SELECT LOG10(`Value`)"));

    /// <summary>
    /// Verifies that interval text without a <c>DATE_ADD</c> call is rejected.
    /// </summary>
    [Fact]
    public void Date_add_requires_function_interval_and_unit() =>
        Assert.ThrowsAny<XunitException>(() => MySqlSqlAssert.ContainsDateAdd("SELECT INTERVAL 1 DAY", "DAY"));

    /// <summary>
    /// Verifies the distinct MySQL and MariaDB regular-expression spellings.
    /// </summary>
    [Fact]
    public void Regular_expression_keeps_engine_dialects_distinct()
    {
        MySqlSqlAssert.ContainsRegularExpression("SELECT REGEXP_LIKE(`Value`, '^a')", mariaDb: false);
        MySqlSqlAssert.ContainsRegularExpression("SELECT `Value` REGEXP '^a'", mariaDb: true);

        Assert.ThrowsAny<XunitException>(() =>
            MySqlSqlAssert.ContainsRegularExpression("SELECT `Value` REGEXP '^a'", mariaDb: false));
    }
}
