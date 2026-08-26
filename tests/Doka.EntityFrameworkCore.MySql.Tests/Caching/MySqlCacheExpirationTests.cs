using Doka.Caching.MySql;
using Microsoft.Extensions.Caching.Distributed;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers cache expiration resolution without introducing a client clock dependency.
/// </summary>
public sealed class MySqlCacheExpirationTests
{
    /// <summary>
    /// Verifies unspecified entry expiration uses only the configured sliding default.
    /// </summary>
    [Fact]
    public void Unspecified_expiration_uses_the_configured_sliding_default()
    {
        var expiration = MySqlCacheExpiration.Resolve(new DistributedCacheEntryOptions(), 12_345L);

        Assert.Null(expiration.AbsoluteExpirationUtc);
        Assert.Null(expiration.AbsoluteExpirationRelativeMicroseconds);
        Assert.Equal(12_345L, expiration.SlidingExpirationMicroseconds);
    }

    /// <summary>
    /// Verifies absolute timestamps are normalized to UTC for MySQL DATETIME values.
    /// </summary>
    [Fact]
    public void Absolute_expiration_is_normalized_to_utc_without_adding_default_sliding()
    {
        var absolute = new DateTimeOffset(2030, 4, 5, 12, 30, 0, TimeSpan.FromHours(3));
        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions { AbsoluteExpiration = absolute }, 12_345L);

        Assert.Equal(new DateTime(2030, 4, 5, 9, 30, 0), expiration.AbsoluteExpirationUtc);
        Assert.Equal(DateTimeKind.Unspecified, expiration.AbsoluteExpirationUtc!.Value.Kind);
        Assert.Null(expiration.AbsoluteExpirationRelativeMicroseconds);
        Assert.Null(expiration.SlidingExpirationMicroseconds);
    }

    /// <summary>
    /// Verifies relative expiration remains a duration for database-clock evaluation.
    /// </summary>
    [Fact]
    public void Relative_expiration_remains_a_duration_without_reading_the_application_clock()
    {
        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2) }, 12_345L);

        Assert.Null(expiration.AbsoluteExpirationUtc);
        Assert.Equal(2_000_000L, expiration.AbsoluteExpirationRelativeMicroseconds);
        Assert.Null(expiration.SlidingExpirationMicroseconds);
    }

    /// <summary>
    /// Verifies relative expiration takes precedence when both absolute forms are supplied.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2030)]
    public void Relative_expiration_takes_precedence_over_absolute_expiration(
        int absoluteYear
    )
    {
        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = new DateTimeOffset(absoluteYear, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2),
                SlidingExpiration = TimeSpan.FromSeconds(1)
            },
            12_345L);

        Assert.Null(expiration.AbsoluteExpirationUtc);
        Assert.Equal(2_000_000L, expiration.AbsoluteExpirationRelativeMicroseconds);
        Assert.Equal(1_000_000L, expiration.SlidingExpirationMicroseconds);
    }

    /// <summary>
    /// Verifies absolute and sliding constraints are preserved for database-side minimum selection.
    /// </summary>
    [Fact]
    public void Absolute_and_sliding_expiration_are_both_preserved()
    {
        var absolute = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = absolute,
                SlidingExpiration = TimeSpan.FromSeconds(3)
            },
            12_345L);

        Assert.Equal(absolute.UtcDateTime, expiration.AbsoluteExpirationUtc);
        Assert.Equal(3_000_000L, expiration.SlidingExpirationMicroseconds);
        Assert.Null(expiration.AbsoluteExpirationRelativeMicroseconds);
    }

    /// <summary>
    /// Verifies explicit sliding expiration replaces the default.
    /// </summary>
    [Fact]
    public void Explicit_sliding_expiration_replaces_the_default()
    {
        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(3) }, 12_345L);

        Assert.Equal(3_000_000L, expiration.SlidingExpirationMicroseconds);
        Assert.Null(expiration.AbsoluteExpirationUtc);
        Assert.Null(expiration.AbsoluteExpirationRelativeMicroseconds);
    }

    /// <summary>
    /// Verifies past timestamps are preserved for immediate database-clock expiration.
    /// </summary>
    [Fact]
    public void Past_absolute_expiration_does_not_depend_on_the_application_clock()
    {
        var absolute = new DateTimeOffset(1000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var expiration = MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions { AbsoluteExpiration = absolute }, 12_345L);

        Assert.Equal(absolute.UtcDateTime, expiration.AbsoluteExpirationUtc);
    }

    /// <summary>
    /// Verifies timestamps outside the MySQL DATETIME range are rejected.
    /// </summary>
    [Fact]
    public void Absolute_expiration_before_mysql_datetime_range_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlCacheExpiration.Resolve(
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = new DateTimeOffset(999, 12, 31, 23, 59, 59, TimeSpan.Zero),
            },
            12_345L));
    }

    /// <summary>
    /// Verifies expiration cannot collapse a positive duration to zero database units.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submicrosecond_expiration_is_rejected(
        bool sliding
    )
    {
        var options = new DistributedCacheEntryOptions();
        if (sliding)
        {
            options.SlidingExpiration = TimeSpan.FromTicks(9);
        }
        else
        {
            options.AbsoluteExpirationRelativeToNow = TimeSpan.FromTicks(9);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlCacheExpiration.Resolve(options, 12_345L));
    }

    /// <summary>
    /// Verifies conversion uses integer ticks at the database precision boundary.
    /// </summary>
    [Theory]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(123_456_789, 12_345_678)]
    public void Durations_convert_to_microseconds_without_floating_point_rounding(
        long ticks,
        long expectedMicroseconds
    ) => Assert.Equal(expectedMicroseconds, MySqlCacheExpiration.ToMicroseconds(TimeSpan.FromTicks(ticks), "value"));

    /// <summary>
    /// Verifies missing per-entry options are rejected before an operation can run.
    /// </summary>
    [Fact]
    public void Null_expiration_options_are_rejected()=> Assert.Throws<ArgumentNullException>(
        "options",
        () => MySqlCacheExpiration.Resolve(null!, 12_345L));
}
