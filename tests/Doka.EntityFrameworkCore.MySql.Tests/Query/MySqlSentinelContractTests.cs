namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the exhaustive provider-internal SQL sentinel contract.
/// </summary>
public sealed class MySqlSentinelContractTests
{
    /// <summary>
    /// Ensures every non-interval sentinel has one canonical, round-trippable name.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonIntervalSentinelCases))]
    public void Every_non_interval_sentinel_round_trips(
        int kindValue
    )
    {
        // Arrange
        var kind = (MySqlSentinelKind)kindValue;
        var name = MySqlSentinelContract.GetName(kind);

        // Act
        var resolved = MySqlSentinelContract.Resolve(name, ValidArgumentCount(kind));

        // Assert
        Assert.Equal(new MySqlSentinel(kind), resolved);
    }

    /// <summary>
    /// Ensures every declared interval unit round-trips through the date-add contract.
    /// </summary>
    [Theory]
    [MemberData(nameof(IntervalUnitCases))]
    public void Every_date_add_interval_unit_round_trips(
        int intervalUnitValue
    )
    {
        // Arrange
        var intervalUnit = (MySqlIntervalUnit)intervalUnitValue;
        var name = MySqlSentinelContract.GetName(MySqlSentinelKind.DateAdd, intervalUnit);

        // Act
        var resolved = MySqlSentinelContract.Resolve(name, argumentCount: 2);

        // Assert
        Assert.Equal(new MySqlSentinel(MySqlSentinelKind.DateAdd, intervalUnit), resolved);
    }

    /// <summary>
    /// Ensures the time-add contract accepts the units translated by TimeOnly.
    /// </summary>
    [Theory]
    [InlineData((int)MySqlIntervalUnit.Hour)]
    [InlineData((int)MySqlIntervalUnit.Minute)]
    public void Time_add_accepts_supported_interval_unit(int intervalUnitValue)
    {
        // Arrange
        var intervalUnit = (MySqlIntervalUnit)intervalUnitValue;
        var name = MySqlSentinelContract.GetName(MySqlSentinelKind.TimeAdd, intervalUnit);

        // Act
        var resolved = MySqlSentinelContract.Resolve(name, argumentCount: 2);

        // Assert
        Assert.Equal(new MySqlSentinel(MySqlSentinelKind.TimeAdd, intervalUnit), resolved);
    }

    /// <summary>
    /// Ensures the time-add contract rejects a unit not translated by TimeOnly.
    /// </summary>
    [Fact]
    public void Time_add_rejects_unsupported_interval_unit()
    {
        // Arrange
        const MySqlIntervalUnit unsupportedUnit = MySqlIntervalUnit.Second;

        // Act
        var exception = Record.Exception(() => MySqlSentinelContract.GetName(
            MySqlSentinelKind.TimeAdd,
            unsupportedUnit));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    /// Ensures every sentinel rejects an arity that its SQL emitter cannot consume.
    /// </summary>
    [Theory]
    [MemberData(nameof(SentinelCases))]
    public void Every_sentinel_rejects_invalid_argument_count(
        int kindValue
    )
    {
        // Arrange
        var kind = (MySqlSentinelKind)kindValue;
        var name = kind switch
        {
            MySqlSentinelKind.DateAdd => MySqlSentinelContract.GetName(kind, MySqlIntervalUnit.Day),
            MySqlSentinelKind.TimeAdd => MySqlSentinelContract.GetName(kind, MySqlIntervalUnit.Hour),
            _ => MySqlSentinelContract.GetName(kind),
        };

        var invalidCount = kind == MySqlSentinelKind.GroupConcat ? 1 : ValidArgumentCount(kind) + 1;

        // Act
        var exception = Record.Exception(() => MySqlSentinelContract.Resolve(name, invalidCount));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>Supplies every non-interval SQL sentinel as an independently selectable case.</summary>
    public static IEnumerable<object[]> NonIntervalSentinelCases() => Enum
        .GetValues<MySqlSentinelKind>()
        .Where(kind => kind is not (MySqlSentinelKind.DateAdd or MySqlSentinelKind.TimeAdd))
        .Select(kind => new object[] { (int)kind });

    /// <summary>Supplies every SQL sentinel for argument-count rejection.</summary>
    public static IEnumerable<object[]> SentinelCases() => Enum
        .GetValues<MySqlSentinelKind>()
        .Select(kind => new object[] { (int)kind });

    /// <summary>Supplies every translated date-add interval unit.</summary>
    public static IEnumerable<object[]> IntervalUnitCases() => Enum
        .GetValues<MySqlIntervalUnit>()
        .Select(unit => new object[] { (int)unit });

    /// <summary>
    /// Ensures unknown names in the reserved sentinel namespace fail before SQL emission.
    /// </summary>
    [Fact]
    public void Unknown_reserved_sentinel_fails_closed()
    {
        // Arrange
        var unknownName = MySqlSentinelContract
            .GetName(MySqlSentinelKind.JsonSet)
            .Replace("json_set", "unknown", StringComparison.Ordinal);

        Action act = () => MySqlSentinelContract.Resolve(unknownName, argumentCount: 1);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Ensures callers cannot attach interval data to a non-interval sentinel.
    /// </summary>
    [Fact]
    public void Non_interval_sentinel_rejects_interval_unit()
    {
        // Arrange
        Action act = () => MySqlSentinelContract.GetName(
            MySqlSentinelKind.Match,
            MySqlIntervalUnit.Day);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    private static int ValidArgumentCount(
        MySqlSentinelKind kind
    ) => kind switch
    {
        MySqlSentinelKind.DateTimeOffsetNow or MySqlSentinelKind.DateTimeOffsetUtcNow => 0,
        MySqlSentinelKind.OrderAscending
            or MySqlSentinelKind.OrderDescending
            or MySqlSentinelKind.GuidToString
            or MySqlSentinelKind.StringJsonDecode
            or MySqlSentinelKind.TimeSpanJsonDecode
            or MySqlSentinelKind.TimeOfDayTicks
            or MySqlSentinelKind.OnesComplement => 1,
        MySqlSentinelKind.JsonSet => 3,
        MySqlSentinelKind.GroupConcat => 2,
        _ => 2,
    };
}
