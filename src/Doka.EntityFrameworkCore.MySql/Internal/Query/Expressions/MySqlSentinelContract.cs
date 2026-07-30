namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Identifies provider-internal SQL shapes that cannot be represented by a regular
/// <see cref="SqlFunctionExpression"/>.
/// </summary>
internal enum MySqlSentinelKind
{
    JsonSet,
    RegularExpression,
    Match,
    MatchBoolean,
    GroupConcat,
    OrderAscending,
    OrderDescending,
    GuidToString,
    DateTimeOffsetNow,
    DateTimeOffsetUtcNow,
    DateTimeOffsetSubtractTimeSpan,
    DateTimeDifferenceTicks,
    TimeDifferenceTicks,
    TimeOfDayTicks,
    LeftShift,
    RightShift,
    OnesComplement,
    DateAdd,
    TimeAdd,
}

/// <summary>
/// Closed set of interval units accepted by provider-internal date/time sentinels.
/// </summary>
internal enum MySqlIntervalUnit
{
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
}

/// <summary>
/// Parsed sentinel identity, including the interval unit required by date/time
/// addition sentinels.
/// </summary>
internal readonly record struct MySqlSentinel(
    MySqlSentinelKind Kind,
    MySqlIntervalUnit? IntervalUnit = null
);

/// <summary>
/// Owns every provider-internal sentinel name and validates the name, interval
/// unit, and arity before the SQL generator dispatches to a custom emitter.
/// </summary>
internal static class MySqlSentinelContract
{
    private const string Prefix = "__mysql_";
    private const string DateAddPrefix = Prefix + "date_add_";
    private const string TimeAddPrefix = Prefix + "time_add_";
    private const string JsonSetName = Prefix + "json_set";
    private const string RegularExpressionName = Prefix + "regexp";
    private const string MatchName = Prefix + "match";
    private const string MatchBooleanName = Prefix + "match_boolean";
    private const string GroupConcatName = Prefix + "group_concat";
    private const string OrderAscendingName = Prefix + "order_ascending";
    private const string OrderDescendingName = Prefix + "order_descending";
    private const string GuidToStringName = Prefix + "guid_to_string";
    private const string DateTimeOffsetNowName = Prefix + "datetimeoffset_now";
    private const string DateTimeOffsetUtcNowName = Prefix + "datetimeoffset_utc_now";
    private const string DateTimeOffsetSubtractTimeSpanName = Prefix + "datetimeoffset_subtract_timespan";
    private const string DateTimeDifferenceTicksName = Prefix + "datetime_diff_ticks";
    private const string TimeDifferenceTicksName = Prefix + "time_diff_ticks";
    private const string TimeOfDayTicksName = Prefix + "time_of_day_ticks";
    private const string LeftShiftName = Prefix + "left_shift";
    private const string RightShiftName = Prefix + "right_shift";
    private const string OnesComplementName = Prefix + "ones_complement";

    /// <summary>
    /// Returns the canonical function name for a non-interval sentinel.
    /// </summary>
    public static string GetName(
        MySqlSentinelKind kind
    ) => kind switch
    {
        MySqlSentinelKind.JsonSet => JsonSetName,
        MySqlSentinelKind.RegularExpression => RegularExpressionName,
        MySqlSentinelKind.Match => MatchName,
        MySqlSentinelKind.MatchBoolean => MatchBooleanName,
        MySqlSentinelKind.GroupConcat => GroupConcatName,
        MySqlSentinelKind.OrderAscending => OrderAscendingName,
        MySqlSentinelKind.OrderDescending => OrderDescendingName,
        MySqlSentinelKind.GuidToString => GuidToStringName,
        MySqlSentinelKind.DateTimeOffsetNow => DateTimeOffsetNowName,
        MySqlSentinelKind.DateTimeOffsetUtcNow => DateTimeOffsetUtcNowName,
        MySqlSentinelKind.DateTimeOffsetSubtractTimeSpan => DateTimeOffsetSubtractTimeSpanName,
        MySqlSentinelKind.DateTimeDifferenceTicks => DateTimeDifferenceTicksName,
        MySqlSentinelKind.TimeDifferenceTicks => TimeDifferenceTicksName,
        MySqlSentinelKind.TimeOfDayTicks => TimeOfDayTicksName,
        MySqlSentinelKind.LeftShift => LeftShiftName,
        MySqlSentinelKind.RightShift => RightShiftName,
        MySqlSentinelKind.OnesComplement => OnesComplementName,
        MySqlSentinelKind.DateAdd or MySqlSentinelKind.TimeAdd => throw new ArgumentException(
            $"Sentinel kind {kind} requires an interval unit.",
            nameof(kind)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Returns the canonical function name for a date/time addition sentinel.
    /// </summary>
    public static string GetName(
        MySqlSentinelKind kind,
        MySqlIntervalUnit intervalUnit
    )
    {
        if (kind is not (MySqlSentinelKind.DateAdd or MySqlSentinelKind.TimeAdd))
        {
            throw new ArgumentException($"Sentinel kind {kind} does not accept an interval unit.", nameof(kind));
        }

        if (kind == MySqlSentinelKind.TimeAdd
            && intervalUnit is not (MySqlIntervalUnit.Hour or MySqlIntervalUnit.Minute))
        {
            throw new ArgumentException(
                $"Time addition does not support the {intervalUnit} interval unit.",
                nameof(intervalUnit));
        }

        var prefix = kind == MySqlSentinelKind.DateAdd ? DateAddPrefix : TimeAddPrefix;
        return prefix + GetIntervalSql(intervalUnit);
    }

    /// <summary>
    /// Returns whether a SQL function name belongs to the provider sentinel namespace.
    /// </summary>
    public static bool IsSentinelName(
        string name
    ) => name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Resolves and validates a sentinel function before custom SQL generation.
    /// </summary>
    public static MySqlSentinel Resolve(
        string name,
        int argumentCount
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);

        var sentinel = name switch
        {
            JsonSetName => new MySqlSentinel(MySqlSentinelKind.JsonSet),
            RegularExpressionName => new MySqlSentinel(MySqlSentinelKind.RegularExpression),
            MatchName => new MySqlSentinel(MySqlSentinelKind.Match),
            MatchBooleanName => new MySqlSentinel(MySqlSentinelKind.MatchBoolean),
            GroupConcatName => new MySqlSentinel(MySqlSentinelKind.GroupConcat),
            OrderAscendingName => new MySqlSentinel(MySqlSentinelKind.OrderAscending),
            OrderDescendingName => new MySqlSentinel(MySqlSentinelKind.OrderDescending),
            GuidToStringName => new MySqlSentinel(MySqlSentinelKind.GuidToString),
            DateTimeOffsetNowName => new MySqlSentinel(MySqlSentinelKind.DateTimeOffsetNow),
            DateTimeOffsetUtcNowName => new MySqlSentinel(MySqlSentinelKind.DateTimeOffsetUtcNow),
            DateTimeOffsetSubtractTimeSpanName => new MySqlSentinel(MySqlSentinelKind.DateTimeOffsetSubtractTimeSpan),
            DateTimeDifferenceTicksName => new MySqlSentinel(MySqlSentinelKind.DateTimeDifferenceTicks),
            TimeDifferenceTicksName => new MySqlSentinel(MySqlSentinelKind.TimeDifferenceTicks),
            TimeOfDayTicksName => new MySqlSentinel(MySqlSentinelKind.TimeOfDayTicks),
            LeftShiftName => new MySqlSentinel(MySqlSentinelKind.LeftShift),
            RightShiftName => new MySqlSentinel(MySqlSentinelKind.RightShift),
            OnesComplementName => new MySqlSentinel(MySqlSentinelKind.OnesComplement),
            _ when name.StartsWith(DateAddPrefix, StringComparison.Ordinal) =>
                ResolveInterval(name, DateAddPrefix, MySqlSentinelKind.DateAdd),
            _ when name.StartsWith(TimeAddPrefix, StringComparison.Ordinal) => ResolveInterval(
                name,
                TimeAddPrefix,
                MySqlSentinelKind.TimeAdd),
            _ => throw new InvalidOperationException($"Unknown provider SQL sentinel '{name}'."),
        };

        if (!HasValidArgumentCount(sentinel.Kind, argumentCount))
        {
            throw new InvalidOperationException(
                $"Provider SQL sentinel '{name}' received an invalid argument count of {argumentCount}.");
        }

        return sentinel;
    }

    /// <summary>
    /// Returns the SQL token for a validated interval unit.
    /// </summary>
    public static string GetIntervalSql(
        MySqlIntervalUnit intervalUnit
    ) => intervalUnit switch
    {
        MySqlIntervalUnit.Year => "YEAR",
        MySqlIntervalUnit.Month => "MONTH",
        MySqlIntervalUnit.Day => "DAY",
        MySqlIntervalUnit.Hour => "HOUR",
        MySqlIntervalUnit.Minute => "MINUTE",
        MySqlIntervalUnit.Second => "SECOND",
        _ => throw new ArgumentOutOfRangeException(nameof(intervalUnit)),
    };

    private static MySqlSentinel ResolveInterval(
        string name,
        string prefix,
        MySqlSentinelKind kind
    )
    {
        var unit = name[prefix.Length..] switch
        {
            "YEAR" => MySqlIntervalUnit.Year,
            "MONTH" => MySqlIntervalUnit.Month,
            "DAY" => MySqlIntervalUnit.Day,
            "HOUR" => MySqlIntervalUnit.Hour,
            "MINUTE" => MySqlIntervalUnit.Minute,
            "SECOND" => MySqlIntervalUnit.Second,
            _ => throw new InvalidOperationException(
                $"Provider SQL sentinel '{name}' contains an unsupported interval unit."),
        };

        if (kind == MySqlSentinelKind.TimeAdd
            && unit is not (MySqlIntervalUnit.Hour or MySqlIntervalUnit.Minute))
        {
            throw new InvalidOperationException(
                $"Provider SQL sentinel '{name}' contains an unsupported time interval unit.");
        }

        return new MySqlSentinel(kind, unit);
    }

    private static bool HasValidArgumentCount(
        MySqlSentinelKind kind,
        int argumentCount
    ) => kind switch
    {
        MySqlSentinelKind.DateTimeOffsetNow or MySqlSentinelKind.DateTimeOffsetUtcNow => argumentCount == 0,
        MySqlSentinelKind.OrderAscending
            or MySqlSentinelKind.OrderDescending
            or MySqlSentinelKind.GuidToString
            or MySqlSentinelKind.TimeOfDayTicks
            or MySqlSentinelKind.OnesComplement => argumentCount == 1,
        MySqlSentinelKind.JsonSet => argumentCount == 3,
        MySqlSentinelKind.GroupConcat => argumentCount >= 2,
        MySqlSentinelKind.RegularExpression
            or MySqlSentinelKind.Match
            or MySqlSentinelKind.MatchBoolean
            or MySqlSentinelKind.DateTimeOffsetSubtractTimeSpan
            or MySqlSentinelKind.DateTimeDifferenceTicks
            or MySqlSentinelKind.TimeDifferenceTicks
            or MySqlSentinelKind.LeftShift
            or MySqlSentinelKind.RightShift
            or MySqlSentinelKind.DateAdd
            or MySqlSentinelKind.TimeAdd => argumentCount == 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
