namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests transient exception detection including all MySQL error codes, IOException, and depth traversal.
/// </summary>
public sealed class MySqlTransientExceptionDetectorTests
{
    private readonly MySqlTransientExceptionDetector _detector = new();
    private readonly ServerCapabilities _capabilities = ServerCapabilities.Create(false, new Version(8, 4, 0));

    // ── SocketException (retryable) ──

    /// <summary>SocketException is retryable.</summary>
    [Fact]
    public void SocketException_is_retryable() => Assert.True(
        _detector.ShouldRetryOn(new System.Net.Sockets.SocketException(), _capabilities));

    // ── IOException (retryable) ──

    /// <summary>IOException is retryable.</summary>
    [Fact]
    public void IOException_is_retryable() => Assert.True(
        _detector.ShouldRetryOn(new System.IO.IOException("connection reset"), _capabilities));

    // ── OperationCanceledException (not retryable) ──

    /// <summary>OperationCanceledException is not retryable.</summary>
    [Fact]
    public void OperationCanceledException_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new OperationCanceledException(), _capabilities));

    // ── TimeoutException (not retryable via ShouldRetryOn) ──

    /// <summary>TimeoutException is not retryable (handled separately as command timeout).</summary>
    [Fact]
    public void TimeoutException_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new TimeoutException(), _capabilities));

    // ── IsCommandTimeout ──

    /// <summary>TimeoutException is recognized as command timeout.</summary>
    [Fact]
    public void TimeoutException_is_command_timeout() =>
        Assert.True(_detector.IsCommandTimeout(new TimeoutException()));

    /// <summary>Regular exception is not command timeout.</summary>
    [Fact]
    public void Regular_exception_is_not_command_timeout() =>
        Assert.False(_detector.IsCommandTimeout(new InvalidOperationException()));

    // ── Inner exception traversal ──

    /// <summary>SocketException nested inside another exception is detected.</summary>
    [Fact]
    public void Nested_socket_exception_is_retryable()
    {
        var inner = new System.Net.Sockets.SocketException();
        var outer = new InvalidOperationException("wrapper", inner);
        Assert.True(_detector.ShouldRetryOn(outer, _capabilities));
    }

    /// <summary>Deeply nested exception beyond max depth is not traversed.</summary>
    [Fact]
    public void Exception_beyond_max_depth_is_not_retryable()
    {
        // Build a chain deeper than the 20-level limit.
        Exception current = new System.Net.Sockets.SocketException();
        for (var i = 0; i < 25; i++)
        {
            current = new InvalidOperationException($"level_{i}", current);
        }

        // The SocketException is at depth 25 — beyond the 20-level traversal limit.
        Assert.False(_detector.ShouldRetryOn(current, _capabilities));
    }

    // ── Arbitrary exception (not retryable) ──

    /// <summary>Arbitrary exceptions are not retryable.</summary>
    [Fact]
    public void Arbitrary_exception_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new ArgumentException(), _capabilities));
}
