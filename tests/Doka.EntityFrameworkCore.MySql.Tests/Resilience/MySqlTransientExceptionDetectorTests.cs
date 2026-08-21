namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests transient exception detection including all MySQL error codes, IOException, and depth traversal.
/// </summary>
public sealed class MySqlTransientExceptionDetectorTests
{
    private readonly MySqlTransientExceptionDetector _detector = new();
    // -- SocketException (retryable) --

    /// <summary>SocketException is retryable.</summary>
    [Fact]
    public void SocketException_is_retryable() => Assert.True(
        _detector.ShouldRetryOn(new System.Net.Sockets.SocketException()));

    // -- IOException (retryable) --

    /// <summary>IOException is retryable.</summary>
    [Fact]
    public void IOException_is_retryable() => Assert.True(
        _detector.ShouldRetryOn(new System.IO.IOException("connection reset")));

    // -- OperationCanceledException (not retryable) --

    /// <summary>OperationCanceledException is not retryable.</summary>
    [Fact]
    public void OperationCanceledException_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new OperationCanceledException()));

    /// <summary>A retryable outer I/O failure cannot hide an inner cancellation.</summary>
    [Fact]
    public void IOException_wrapping_cancellation_is_not_retryable()
    {
        var exception = new IOException("transport wrapper", new OperationCanceledException("cancelled operation"));

        Assert.True(_detector.IsCancellation(exception));
        Assert.False(_detector.ShouldRetryOn(exception));
    }

    /// <summary>
    /// A session cleanup failure is an ambiguous DDL outcome and must not be
    /// retried even when its cause would normally be transient.
    /// </summary>
    [Fact]
    public void Migration_session_cleanup_failure_is_not_retryable()
    {
        var exception = new MySqlMigrationSessionCleanupException(
            new IOException("connection reset during cleanup"));

        Assert.False(_detector.ShouldRetryOn(exception));
    }

    /// <summary>A retryable connector error cannot hide an inner cancellation.</summary>
    [Fact]
    public void MySqlException_wrapping_cancellation_is_not_retryable()
    {
        var exception = CreateMySqlException(
            MySqlErrorCode.UnableToConnectToHost,
            new OperationCanceledException("cancelled operation"));

        Assert.True(_detector.IsCancellation(exception));
        Assert.False(_detector.ShouldRetryOn(exception));
    }

    // -- TimeoutException (not retryable via ShouldRetryOn) --

    /// <summary>TimeoutException is not retryable (handled separately as command timeout).</summary>
    [Fact]
    public void TimeoutException_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new TimeoutException()));

    // -- IsCommandTimeout --

    /// <summary>TimeoutException is recognized as command timeout.</summary>
    [Fact]
    public void TimeoutException_is_command_timeout() =>
        Assert.True(_detector.IsCommandTimeout(new TimeoutException()));

    /// <summary>Regular exception is not command timeout.</summary>
    [Fact]
    public void Regular_exception_is_not_command_timeout() =>
        Assert.False(_detector.IsCommandTimeout(new InvalidOperationException()));

    // -- Inner exception traversal --

    /// <summary>SocketException nested inside another exception is detected.</summary>
    [Fact]
    public void Nested_socket_exception_is_retryable()
    {
        var inner = new System.Net.Sockets.SocketException();
        var outer = new InvalidOperationException("wrapper", inner);

        Assert.True(_detector.ShouldRetryOn(outer));
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

        // The SocketException is at depth 25 -- beyond the 20-level traversal limit.
        Assert.False(_detector.ShouldRetryOn(current));
    }

    // -- Arbitrary exception (not retryable) --

    /// <summary>Arbitrary exceptions are not retryable.</summary>
    [Fact]
    public void Arbitrary_exception_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(new ArgumentException()));

    // -- MySqlException retryable error codes --

    /// <summary>Each known-retryable MySqlErrorCode triggers a retry verdict.</summary>
    [Theory]
    [InlineData(MySqlErrorCode.ConnectionCountError)]
    [InlineData(MySqlErrorCode.TooManyUserConnections)]
    [InlineData(MySqlErrorCode.UnableToConnectToHost)]
    [InlineData(MySqlErrorCode.ServerShutdown)]
    [InlineData(MySqlErrorCode.LockWaitTimeout)]
    [InlineData(MySqlErrorCode.LockDeadlock)]
    [InlineData(MySqlErrorCode.XARBDeadlock)]
    [InlineData(MySqlErrorCode.UserLockDeadlock)]
    public void MySqlException_with_known_retryable_error_code_is_retryable(MySqlErrorCode code) =>
        Assert.True(_detector.ShouldRetryOn(CreateMySqlException(code)));

    /// <summary>MySqlExceptions whose ErrorCode is not on the known-retryable list and not transient are not retryable.</summary>
    [Theory]
    [InlineData(MySqlErrorCode.AccessDenied)]
    [InlineData(MySqlErrorCode.NoSuchTable)]
    [InlineData(MySqlErrorCode.SyntaxError)]
    public void MySqlException_with_unknown_error_code_is_not_retryable(MySqlErrorCode code) =>
        Assert.False(_detector.ShouldRetryOn(CreateMySqlException(code)));

    /// <summary>MySqlException with CommandTimeoutExpired is recognized as command timeout.</summary>
    [Fact]
    public void MySqlException_with_command_timeout_error_code_is_command_timeout() =>
        Assert.True(_detector.IsCommandTimeout(CreateMySqlException(MySqlErrorCode.CommandTimeoutExpired)));

    /// <summary>MySqlException with CommandTimeoutExpired is not retryable (timeout short-circuits ShouldRetryOn).</summary>
    [Fact]
    public void MySqlException_with_command_timeout_error_code_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(CreateMySqlException(MySqlErrorCode.CommandTimeoutExpired)));

    /// <summary>Nested MySqlException with retryable code is detected via inner-exception traversal.</summary>
    [Fact]
    public void Nested_mysql_exception_with_retryable_code_is_retryable()
    {
        var inner = CreateMySqlException(MySqlErrorCode.LockDeadlock);
        var outer = new InvalidOperationException("wrapper", inner);

        Assert.True(_detector.ShouldRetryOn(outer));
    }

    /// <summary>
    /// A connector transport wrapper without a server error number must not
    /// hide the retryable I/O cause observed after a commit acknowledgement is
    /// lost.
    /// </summary>
    [Fact]
    public void Number_zero_mysql_exception_wrapping_io_failure_is_retryable()
    {
        var exception = CreateMySqlException((MySqlErrorCode)0, new IOException("unexpected EOF"));

        Assert.False(exception.IsTransient);
        Assert.Equal(0, exception.Number);
        Assert.True(_detector.ShouldRetryOn(exception));
    }

    /// <summary>
    /// A concrete non-retryable server error remains terminal even when an
    /// inner transport exception is attached.
    /// </summary>
    [Fact]
    public void Non_retryable_server_error_wrapping_io_failure_is_not_retryable()
    {
        var exception = CreateMySqlException(MySqlErrorCode.AccessDenied, new IOException("transport detail"));

        Assert.False(_detector.ShouldRetryOn(exception));
    }

    /// <summary>A number-zero connector wrapper without a retryable cause remains terminal.</summary>
    [Fact]
    public void Number_zero_mysql_exception_without_transport_cause_is_not_retryable() =>
        Assert.False(_detector.ShouldRetryOn(CreateMySqlException((MySqlErrorCode)0)));

    private static MySqlException CreateMySqlException(MySqlErrorCode code)
        => CreateMySqlException(code, innerException: null);

    private static MySqlException CreateMySqlException(
        MySqlErrorCode code,
        Exception? innerException
    )
    {
        // MySqlConnector keeps error-code construction internal. Reflection is
        // the only test-time path that can cover its complete classification
        // surface without manufacturing a real server failure.
        var ctor = typeof(MySqlException).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            types: [typeof(MySqlErrorCode), typeof(string), typeof(Exception)],
            modifiers: null);

        Assert.NotNull(ctor);
        return (MySqlException)ctor.Invoke([code, $"test:{code}", innerException]);
    }
}
