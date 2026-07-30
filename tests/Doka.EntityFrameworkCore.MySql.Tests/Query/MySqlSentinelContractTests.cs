namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the exhaustive provider-internal SQL sentinel contract.
/// </summary>
public sealed class MySqlSentinelContractTests
{
    /// <summary>
    /// Ensures every non-interval sentinel has one canonical, round-trippable name.
    /// </summary>
    [Fact]
    public void Every_non_interval_sentinel_round_trips()
    {
        var kinds = Enum
            .GetValues<MySqlSentinelKind>()
            .Where(kind => kind is not (MySqlSentinelKind.DateAdd or MySqlSentinelKind.TimeAdd));

        foreach (var kind in kinds)
        {
            var name = MySqlSentinelContract.GetName(kind);
            var resolved = MySqlSentinelContract.Resolve(name, ValidArgumentCount(kind));

            Assert.Equal(new MySqlSentinel(kind), resolved);
        }
    }

    /// <summary>
    /// Ensures every declared interval unit round-trips through the date-add contract.
    /// </summary>
    [Fact]
    public void Every_date_add_interval_unit_round_trips()
    {
        foreach (var intervalUnit in Enum.GetValues<MySqlIntervalUnit>())
        {
            var name = MySqlSentinelContract.GetName(MySqlSentinelKind.DateAdd, intervalUnit);
            var resolved = MySqlSentinelContract.Resolve(name, argumentCount: 2);

            Assert.Equal(new MySqlSentinel(MySqlSentinelKind.DateAdd, intervalUnit), resolved);
        }
    }

    /// <summary>
    /// Ensures the time-add contract accepts only the units translated by TimeOnly.
    /// </summary>
    [Fact]
    public void Time_add_accepts_only_supported_interval_units()
    {
        foreach (var intervalUnit in new[]
                 {
                     MySqlIntervalUnit.Hour,
                     MySqlIntervalUnit.Minute,
                 })
        {
            var name = MySqlSentinelContract.GetName(MySqlSentinelKind.TimeAdd, intervalUnit);
            var resolved = MySqlSentinelContract.Resolve(name, argumentCount: 2);

            Assert.Equal(new MySqlSentinel(MySqlSentinelKind.TimeAdd, intervalUnit), resolved);
        }

        Assert.Throws<ArgumentException>(() => MySqlSentinelContract.GetName(
            MySqlSentinelKind.TimeAdd,
            MySqlIntervalUnit.Second));
    }

    /// <summary>
    /// Ensures every sentinel rejects an arity that its SQL emitter cannot consume.
    /// </summary>
    [Fact]
    public void Every_sentinel_rejects_invalid_argument_count()
    {
        foreach (var kind in Enum.GetValues<MySqlSentinelKind>())
        {
            var name = kind switch
            {
                MySqlSentinelKind.DateAdd => MySqlSentinelContract.GetName(kind, MySqlIntervalUnit.Day),
                MySqlSentinelKind.TimeAdd => MySqlSentinelContract.GetName(kind, MySqlIntervalUnit.Hour),
                _ => MySqlSentinelContract.GetName(kind),
            };

            var invalidCount = kind == MySqlSentinelKind.GroupConcat ? 1 : ValidArgumentCount(kind) + 1;

            Assert.Throws<InvalidOperationException>(() => MySqlSentinelContract.Resolve(name, invalidCount));
        }
    }

    /// <summary>
    /// Ensures unknown names in the reserved sentinel namespace fail before SQL emission.
    /// </summary>
    [Fact]
    public void Unknown_reserved_sentinel_fails_closed()
    {
        var unknownName = MySqlSentinelContract
            .GetName(MySqlSentinelKind.JsonSet)
            .Replace("json_set", "unknown", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => MySqlSentinelContract.Resolve(unknownName, argumentCount: 1));
    }

    /// <summary>
    /// Ensures callers cannot attach interval data to a non-interval sentinel.
    /// </summary>
    [Fact]
    public void Non_interval_sentinel_rejects_interval_unit()
    {
        Assert.Throws<ArgumentException>(() => MySqlSentinelContract.GetName(
            MySqlSentinelKind.Match,
            MySqlIntervalUnit.Day));
    }

    private static int ValidArgumentCount(
        MySqlSentinelKind kind
    ) => kind switch
    {
        MySqlSentinelKind.DateTimeOffsetNow or MySqlSentinelKind.DateTimeOffsetUtcNow => 0,
        MySqlSentinelKind.OrderAscending
            or MySqlSentinelKind.OrderDescending
            or MySqlSentinelKind.GuidToString
            or MySqlSentinelKind.TimeOfDayTicks
            or MySqlSentinelKind.OnesComplement => 1,
        MySqlSentinelKind.JsonSet => 3,
        MySqlSentinelKind.GroupConcat => 2,
        _ => 2,
    };
}
