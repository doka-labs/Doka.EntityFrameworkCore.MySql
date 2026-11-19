namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Describes a SQL window inside an EF query; its members have no client implementation.</summary>
/// <remarks>Window ordering is independent of result ordering. Use an explicit tie-breaker when stable positions matter.</remarks>
public abstract class MySqlWindowDefinition
{
    // WHY: The fluent receiver is a translation-only contract. A private constructor prevents
    // callers from supplying a runtime instance or a custom implementation.
    private MySqlWindowDefinition() { }

    /// <summary>Appends a partition key, preserving native SQL equality and collation.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar key.</param>
    /// <returns>The expression-only window definition.</returns>
    public abstract MySqlWindowDefinition PartitionBy<T>(
        T value
    );

    /// <summary>Starts an ascending window ordering.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar key.</param>
    /// <returns>The expression-only window definition.</returns>
    public abstract MySqlWindowDefinition OrderBy<T>(
        T value
    );

    /// <summary>Starts a descending window ordering.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar key.</param>
    /// <returns>The expression-only window definition.</returns>
    public abstract MySqlWindowDefinition OrderByDescending<T>(
        T value
    );

    /// <summary>Appends an ascending window ordering; requires an earlier ordering.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar key.</param>
    /// <returns>The expression-only window definition.</returns>
    public abstract MySqlWindowDefinition ThenBy<T>(
        T value
    );

    /// <summary>Appends a descending window ordering; requires an earlier ordering.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar key.</param>
    /// <returns>The expression-only window definition.</returns>
    public abstract MySqlWindowDefinition ThenByDescending<T>(
        T value
    );

    /// <summary>Uses physical row offsets for this aggregate or value window.</summary>
    /// <param name="startBound">The inclusive frame start.</param>
    /// <param name="endBound">The inclusive frame end.</param>
    /// <returns>The expression-only window definition.</returns>
    /// <remarks>Ranking and LAG/LEAD reject explicit frames instead of pretending that an ignored frame has an effect.</remarks>
    public abstract MySqlWindowDefinition RowsBetween(
        MySqlWindowFrameBound startBound,
        MySqlWindowFrameBound endBound
    );

    /// <summary>Uses peer-based numeric offsets for this aggregate or value window.</summary>
    /// <param name="startBound">The inclusive frame start.</param>
    /// <param name="endBound">The inclusive frame end.</param>
    /// <returns>The expression-only window definition.</returns>
    /// <remarks>Ranking and LAG/LEAD reject explicit frames instead of pretending that an ignored frame has an effect.</remarks>
    public abstract MySqlWindowDefinition RangeBetween(
        MySqlWindowFrameBound startBound,
        MySqlWindowFrameBound endBound
    );

    /// <summary>Counts frame rows; overflow is an error.</summary>
    /// <returns>The checked 32-bit row count.</returns>
    public abstract int Count();

    /// <summary>Counts frame rows in a signed 64-bit result.</summary>
    /// <returns>The checked 64-bit row count.</returns>
    public abstract long LongCount();

    /// <summary>Numbers each ordered partition starting at one.</summary>
    /// <returns>The one-based row number; tied ordering does not guarantee stable positions.</returns>
    public abstract long RowNumber();

    /// <summary>Ranks ordered peers with gaps after ties.</summary>
    /// <returns>The one-based native rank.</returns>
    public abstract long Rank();

    /// <summary>Ranks ordered peers without gaps after ties.</summary>
    /// <returns>The one-based dense rank.</returns>
    public abstract long DenseRank();

    /// <summary>Calculates the ordered partition relative rank, including ties.</summary>
    /// <returns>The native relative rank between zero and one.</returns>
    public abstract double PercentRank();

    /// <summary>Calculates the ordered partition cumulative peer distribution.</summary>
    /// <returns>The native cumulative distribution.</returns>
    public abstract double CumeDist();

    /// <summary>Distributes an ordered partition into a positive number of buckets.</summary>
    /// <param name="buckets">The positive, constant or parameterized bucket count.</param>
    /// <returns>The one-based bucket number.</returns>
    public abstract long Ntile(
        long buckets
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract int Sum(
        int value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        int value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract int? Sum(
        int? value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        int? value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract long Sum(
        long value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        long value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract long? Sum(
        long? value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        long? value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract float Sum(
        float value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract float? Average(
        float value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract float? Sum(
        float? value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract float? Average(
        float? value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract double Sum(
        double value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        double value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract double? Sum(
        double? value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract double? Average(
        double? value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract decimal Sum(
        decimal value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract decimal? Average(
        decimal value
    );

    /// <summary>Sums the frame using EF zero-on-empty semantics.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed sum, or typed zero for an empty or all-NULL frame.</returns>
    /// <remarks>Numeric overflow is an error; no silent narrowing or client aggregation is performed.</remarks>
    public abstract decimal? Sum(
        decimal? value
    );

    /// <summary>Averages the frame and preserves NULL for an empty or all-NULL frame.</summary>
    /// <param name="value">The mapped numeric value.</param>
    /// <returns>The typed average, or NULL when the frame has no non-NULL value.</returns>
    public abstract decimal? Average(
        decimal? value
    );

    /// <summary>Returns the least non-NULL frame value.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The least non-NULL value, or NULL for an empty or all-NULL frame.</returns>
    public abstract T? Min<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the least non-NULL frame value.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The least non-NULL value, or NULL for an empty or all-NULL frame.</returns>
    public abstract T? Min<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the greatest non-NULL frame value.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The greatest non-NULL value, or NULL for an empty or all-NULL frame.</returns>
    public abstract T? Max<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the greatest non-NULL frame value.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The greatest non-NULL value, or NULL for an empty or all-NULL frame.</returns>
    public abstract T? Max<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the first value in the selected frame, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? FirstValue<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the first value in the selected frame, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? FirstValue<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the last value in the selected frame, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? LastValue<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the last value in the selected frame, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? LastValue<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the requested one-based frame value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="position">The positive, constant or parameterized one-based position.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? NthValue<T>(
        T? value,
        long position
    )
        where T : struct;

    /// <summary>Returns the requested one-based frame value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="position">The positive, constant or parameterized one-based position.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? NthValue<T>(
        T? value,
        long position
    )
        where T : class;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? Lag<T>(
        T? value,
        long offset
    )
        where T : struct;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The immediately adjacent value, or NULL for a missing or NULL-valued neighbor.</returns>
    public abstract T? Lag<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <param name="defaultValue">The current-row value used only when the neighbor does not exist.</param>
    /// <returns>The neighbor value, preserving an existing NULL; the default applies only to a missing neighbor.</returns>
    public abstract T? Lag<T>(
        T? value,
        long offset,
        T? defaultValue
    )
        where T : struct;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? Lag<T>(
        T? value,
        long offset
    )
        where T : class;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The immediately adjacent value, or NULL for a missing or NULL-valued neighbor.</returns>
    public abstract T? Lag<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the preceding partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <param name="defaultValue">The current-row value used only when the neighbor does not exist.</param>
    /// <returns>The neighbor value, preserving an existing NULL; the default applies only to a missing neighbor.</returns>
    public abstract T? Lag<T>(
        T? value,
        long offset,
        T? defaultValue
    )
        where T : class;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? Lead<T>(
        T? value,
        long offset
    )
        where T : struct;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The immediately adjacent value, or NULL for a missing or NULL-valued neighbor.</returns>
    public abstract T? Lead<T>(
        T? value
    )
        where T : struct;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <param name="defaultValue">The current-row value used only when the neighbor does not exist.</param>
    /// <returns>The neighbor value, preserving an existing NULL; the default applies only to a missing neighbor.</returns>
    public abstract T? Lead<T>(
        T? value,
        long offset,
        T? defaultValue
    )
        where T : struct;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <returns>The mapped value, or NULL for a missing position or NULL-valued row.</returns>
    public abstract T? Lead<T>(
        T? value,
        long offset
    )
        where T : class;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <returns>The immediately adjacent value, or NULL for a missing or NULL-valued neighbor.</returns>
    public abstract T? Lead<T>(
        T? value
    )
        where T : class;

    /// <summary>Returns the following partition value, respecting NULLs.</summary>
    /// <typeparam name="T">The mapped scalar type.</typeparam>
    /// <param name="value">The mapped scalar value.</param>
    /// <param name="offset">The nonnegative, constant or parameterized offset.</param>
    /// <param name="defaultValue">The current-row value used only when the neighbor does not exist.</param>
    /// <returns>The neighbor value, preserving an existing NULL; the default applies only to a missing neighbor.</returns>
    public abstract T? Lead<T>(
        T? value,
        long offset,
        T? defaultValue
    )
        where T : class;

    /// <summary>Rejects accidental execution rather than silently changing SQL window semantics.</summary>
    internal static InvalidOperationException ClientEvaluation() => new(
        "SQL window markers can only be used inside a translatable Doka LINQ query.");
}
