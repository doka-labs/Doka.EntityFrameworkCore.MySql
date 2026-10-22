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
    public void SocketException_is_retryable()
    {
        // Arrange
        var input = new System.Net.Sockets.SocketException();

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.True(
        actual);
    }

    // -- IOException (retryable) --

    /// <summary>IOException is retryable.</summary>
    [Fact]
    public void IOException_is_retryable()
    {
        // Arrange
        var input = new System.IO.IOException("connection reset");

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.True(
        actual);
    }

    // -- OperationCanceledException (not retryable) --

    /// <summary>OperationCanceledException is not retryable.</summary>
    [Fact]
    public void OperationCanceledException_is_not_retryable()
    {
        // Arrange
        var input = new OperationCanceledException();

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.False(actual);
    }

    /// <summary>A retryable outer I/O failure cannot hide an inner cancellation.</summary>
    [Fact]
    public void IOException_wrapping_cancellation_is_not_retryable()
    {
        // Arrange
        var exception = new IOException("transport wrapper", new OperationCanceledException("cancelled operation"));

        // Act
        var actual = _detector.IsCancellation(exception);
        var shouldRetry = _detector.ShouldRetryOn(exception);

        // Assert
        Assert.True(actual);
        Assert.False(shouldRetry);
    }

    /// <summary>
    /// A session cleanup failure is an ambiguous DDL outcome and must not be
    /// retried even when its cause would normally be transient.
    /// </summary>
    [Fact]
    public void Migration_session_cleanup_failure_is_not_retryable()
    {
        // Arrange
        var exception = new MySqlMigrationSessionCleanupException(
            new IOException("connection reset during cleanup"));

        // Act
        var actual = _detector.ShouldRetryOn(exception);

        // Assert
        Assert.False(actual);
    }

    /// <summary>A retryable connector error cannot hide an inner cancellation.</summary>
    [Fact]
    public void MySqlException_wrapping_cancellation_is_not_retryable()
    {
        // Arrange
        var exception = CreateMySqlException(
            MySqlErrorCode.UnableToConnectToHost,
            new OperationCanceledException("cancelled operation"));

        // Act
        var actual = _detector.IsCancellation(exception);
        var shouldRetry = _detector.ShouldRetryOn(exception);

        // Assert
        Assert.True(actual);
        Assert.False(shouldRetry);
    }

    // -- TimeoutException (not retryable via ShouldRetryOn) --

    /// <summary>TimeoutException is not retryable (handled separately as command timeout).</summary>
    [Fact]
    public void TimeoutException_is_not_retryable()
    {
        // Arrange
        var input = new TimeoutException();

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.False(actual);
    }

    // -- IsCommandTimeout --

    /// <summary>TimeoutException is recognized as command timeout.</summary>
    [Fact]
    public void TimeoutException_is_command_timeout()
    {
        // Arrange
        var input = new TimeoutException();

        // Act
        var actual = _detector.IsCommandTimeout(input);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Regular exception is not command timeout.</summary>
    [Fact]
    public void Regular_exception_is_not_command_timeout()
    {
        // Arrange
        var input = new InvalidOperationException();

        // Act
        var actual = _detector.IsCommandTimeout(input);

        // Assert
        Assert.False(actual);
    }

    // -- Inner exception traversal --

    /// <summary>SocketException nested inside another exception is detected.</summary>
    [Fact]
    public void Nested_socket_exception_is_retryable()
    {
        // Arrange
        var inner = new System.Net.Sockets.SocketException();
        var outer = new InvalidOperationException("wrapper", inner);

        // Act
        var actual = _detector.ShouldRetryOn(outer);

        // Assert
        Assert.True(actual);
    }

    /// <summary>Deeply nested exception beyond max depth is not traversed.</summary>
    [Fact]
    public void Exception_beyond_max_depth_is_not_retryable()
    {
        // Arrange
        // Build a chain deeper than the 20-level limit.
        Exception current = new System.Net.Sockets.SocketException();
        for (var i = 0; i < 25; i++)
        {
            current = new InvalidOperationException($"level_{i}", current);
        }

        // Act
        var actual = _detector.ShouldRetryOn(current);

        // Assert
        Assert.False(actual);
    }

    // -- Arbitrary exception (not retryable) --

    /// <summary>Arbitrary exceptions are not retryable.</summary>
    [Fact]
    public void Arbitrary_exception_is_not_retryable()
    {
        // Arrange
        var input = new ArgumentException();

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.False(actual);
    }

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
    public void MySqlException_with_known_retryable_error_code_is_retryable(MySqlErrorCode code)
    {
        // Arrange: The theory supplies code.

        // Act
        var actual = _detector.ShouldRetryOn(CreateMySqlException(code));

        // Assert
        Assert.True(actual);
    }

    /// <summary>MySqlExceptions whose ErrorCode is not on the known-retryable list and not transient are not retryable.</summary>
    [Theory]
    [InlineData(MySqlErrorCode.AccessDenied)]
    [InlineData(MySqlErrorCode.NoSuchTable)]
    [InlineData(MySqlErrorCode.SyntaxError)]
    public void MySqlException_with_unknown_error_code_is_not_retryable(MySqlErrorCode code)
    {
        // Arrange: The theory supplies code.

        // Act
        var actual = _detector.ShouldRetryOn(CreateMySqlException(code));

        // Assert
        Assert.False(actual);
    }

    /// <summary>MySqlException with CommandTimeoutExpired is recognized as command timeout.</summary>
    [Fact]
    public void MySqlException_with_command_timeout_error_code_is_command_timeout()
    {
        // Arrange
        var input = CreateMySqlException(MySqlErrorCode.CommandTimeoutExpired);

        // Act
        var actual = _detector.IsCommandTimeout(input);

        // Assert
        Assert.True(actual);
    }

    /// <summary>MySqlException with CommandTimeoutExpired is not retryable (timeout short-circuits ShouldRetryOn).</summary>
    [Fact]
    public void MySqlException_with_command_timeout_error_code_is_not_retryable()
    {
        // Arrange
        var input = CreateMySqlException(MySqlErrorCode.CommandTimeoutExpired);

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.False(actual);
    }

    /// <summary>Nested MySqlException with retryable code is detected via inner-exception traversal.</summary>
    [Fact]
    public void Nested_mysql_exception_with_retryable_code_is_retryable()
    {
        // Arrange
        var inner = CreateMySqlException(MySqlErrorCode.LockDeadlock);
        var outer = new InvalidOperationException("wrapper", inner);

        // Act
        var actual = _detector.ShouldRetryOn(outer);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// A connector transport wrapper without a server error number must not
    /// hide the retryable I/O cause observed after a commit acknowledgement is
    /// lost.
    /// </summary>
    [Fact]
    public void Number_zero_mysql_exception_wrapping_io_failure_is_retryable()
    {
        // Arrange
        var exception = CreateMySqlException((MySqlErrorCode)0, new IOException("unexpected EOF"));

        // Act
        var shouldRetry = _detector.ShouldRetryOn(exception);

        // Assert
        Assert.False(exception.IsTransient);
        Assert.Equal(0, exception.Number);
        Assert.True(shouldRetry);
    }

    /// <summary>
    /// A concrete non-retryable server error remains terminal even when an
    /// inner transport exception is attached.
    /// </summary>
    [Fact]
    public void Non_retryable_server_error_wrapping_io_failure_is_not_retryable()
    {
        // Arrange
        var exception = CreateMySqlException(MySqlErrorCode.AccessDenied, new IOException("transport detail"));

        // Act
        var actual = _detector.ShouldRetryOn(exception);

        // Assert
        Assert.False(actual);
    }

    /// <summary>A number-zero connector wrapper without a retryable cause remains terminal.</summary>
    [Fact]
    public void Number_zero_mysql_exception_without_transport_cause_is_not_retryable()
    {
        // Arrange
        var input = CreateMySqlException((MySqlErrorCode)0);

        // Act
        var actual = _detector.ShouldRetryOn(input);

        // Assert
        Assert.False(actual);
    }

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
