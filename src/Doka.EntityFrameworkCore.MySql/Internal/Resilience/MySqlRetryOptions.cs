namespace Doka.EntityFrameworkCore.MySql;

internal sealed record MySqlRetryOptions(
    int MaxRetryCount,
    TimeSpan MaxRetryDelay
)
{
    public const int DefaultMaxRetryCount = 6;

    public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(30);

    public static MySqlRetryOptions Create(
        int maxRetryCount,
        TimeSpan? maxRetryDelay
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetryCount);

        var effectiveMaxRetryDelay = maxRetryDelay ?? DefaultMaxRetryDelay;

        if (effectiveMaxRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetryDelay),
                "The maximum retry delay must be greater than zero.");
        }

        return new MySqlRetryOptions(maxRetryCount, effectiveMaxRetryDelay);
    }
}
