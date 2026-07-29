namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies generated SQL through MySQL semantic contracts instead of duplicating
/// raw function spellings across translation tests.
/// </summary>
internal static class MySqlSqlAssert
{
    private static readonly string[] s_naturalLogarithmCalls =
    [
        "LN(",
        "LOG(",
    ];

    /// <summary>
    /// Verifies that SQL contains a call to the named function.
    /// </summary>
    public static void ContainsFunction(
        string sql,
        string functionName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        Assert.Contains(functionName + "(", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a local or UTC current-timestamp call at the required fractional
    /// precision while accepting equivalent MySQL local-time spellings.
    /// </summary>
    public static void ContainsCurrentTimestamp(
        string sql,
        bool utc,
        int precision
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentOutOfRangeException.ThrowIfNegative(precision);

        if (utc)
        {
            Assert.Contains($"UTC_TIMESTAMP({precision})", sql, StringComparison.OrdinalIgnoreCase);
            return;
        }

        var containsLocalTimestamp = sql.Contains($"NOW({precision})", StringComparison.OrdinalIgnoreCase)
            || sql.Contains($"CURRENT_TIMESTAMP({precision})", StringComparison.OrdinalIgnoreCase);

        Assert.True(
            containsLocalTimestamp,
            $"Expected SQL to contain NOW({precision}) or CURRENT_TIMESTAMP({precision}).");
    }

    /// <summary>
    /// Verifies the complete MySQL date-add grammar for the requested interval unit.
    /// </summary>
    public static void ContainsDateAdd(
        string sql,
        string intervalUnit
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intervalUnit);

        ContainsFunction(sql, "DATE_ADD");
        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(intervalUnit, sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a natural-logarithm call while accepting MySQL's equivalent
    /// <c>LN(value)</c> and one-argument <c>LOG(value)</c> spellings.
    /// </summary>
    public static void ContainsNaturalLogarithm(
        string sql
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        Assert.Contains(
            s_naturalLogarithmCalls,
            expectedCall => sql.Contains(expectedCall, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies the engine-specific regular-expression spelling without duplicating
    /// that dialect branch at each call site.
    /// </summary>
    public static void ContainsRegularExpression(
        string sql,
        bool mariaDb
    )
    {
        if (mariaDb)
        {
            Assert.Contains(" REGEXP ", sql, StringComparison.OrdinalIgnoreCase);
            return;
        }

        ContainsFunction(sql, "REGEXP_LIKE");
    }
}
