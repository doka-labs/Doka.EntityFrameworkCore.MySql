namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests <see cref="MySqlRetryOptions.Create"/> validation guards.
/// </summary>
public sealed class MySqlRetryOptionsTests
{
    /// <summary>Valid inputs produce a valid options instance.</summary>
    [Fact]
    public void Create_with_valid_inputs_succeeds()
    {
        var options = MySqlRetryOptions.Create(3, TimeSpan.FromSeconds(10));

        Assert.Equal(3, options.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(10), options.MaxRetryDelay);
    }

    /// <summary>Null delay falls back to the default 30s.</summary>
    [Fact]
    public void Create_with_null_delay_uses_default()
    {
        var options = MySqlRetryOptions.Create(5, null);

        Assert.Equal(MySqlRetryOptions.DefaultMaxRetryDelay, options.MaxRetryDelay);
    }

    /// <summary>Zero retry count throws ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Create_with_zero_retry_count_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlRetryOptions.Create(0, TimeSpan.FromSeconds(5)));
    }

    /// <summary>Negative retry count throws ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Create_with_negative_retry_count_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlRetryOptions.Create(-1, TimeSpan.FromSeconds(5)));
    }

    /// <summary>Zero delay throws ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Create_with_zero_delay_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlRetryOptions.Create(3, TimeSpan.Zero));
    }

    /// <summary>Negative delay throws ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Create_with_negative_delay_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MySqlRetryOptions.Create(3, TimeSpan.FromSeconds(-1)));
    }
}
