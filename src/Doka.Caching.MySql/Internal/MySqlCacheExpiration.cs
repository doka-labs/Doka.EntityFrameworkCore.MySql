namespace Doka.Caching.MySql;

internal readonly record struct MySqlCacheExpiration(
    DateTime? AbsoluteExpirationUtc,
    long? AbsoluteExpirationRelativeMicroseconds,
    long? SlidingExpirationMicroseconds
)
{
    private static readonly DateTime s_minimumDateTime = new(1000, 1, 1);

    public static MySqlCacheExpiration Resolve(
        DistributedCacheEntryOptions options,
        long defaultSlidingExpirationMicroseconds
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTime? absoluteExpirationUtc = null;
        if (options.AbsoluteExpirationRelativeToNow is null
            && options.AbsoluteExpiration is { } absoluteExpiration)
        {
            var utcDateTime = absoluteExpiration.UtcDateTime;
            if (utcDateTime < s_minimumDateTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "AbsoluteExpiration must be representable by MySQL datetime.");
            }

            absoluteExpirationUtc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Unspecified);
        }

        long? absoluteExpirationRelativeMicroseconds = options.AbsoluteExpirationRelativeToNow is { } relative
            ? ToMicroseconds(relative, nameof(options.AbsoluteExpirationRelativeToNow))
            : null;

        long? slidingExpirationMicroseconds = options.SlidingExpiration is { } sliding
            ? ToMicroseconds(sliding, nameof(options.SlidingExpiration))
            : null;

        if (absoluteExpirationUtc is null
            && absoluteExpirationRelativeMicroseconds is null
            && slidingExpirationMicroseconds is null)
        {
            slidingExpirationMicroseconds = defaultSlidingExpirationMicroseconds;
        }

        return new MySqlCacheExpiration(
            absoluteExpirationUtc,
            absoluteExpirationRelativeMicroseconds,
            slidingExpirationMicroseconds);
    }

    public static long ToMicroseconds(
        TimeSpan value,
        string parameterName
    )
    {
        if (value < TimeSpan.FromMicroseconds(1))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Expiration values must be at least one microsecond.");
        }

        return value.Ticks / TimeSpan.TicksPerMicrosecond;
    }
}
