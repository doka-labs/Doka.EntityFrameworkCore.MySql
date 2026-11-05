namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies caller cancellation at the database-session startup boundaries.
/// </summary>
public sealed class TestDatabaseSessionCancellationTests
{
    /// <summary>
    /// Verifies a canceled caller prevents request enumeration and provisioning.
    /// </summary>
    [Fact]
    public async Task Pre_canceled_startup_does_not_enumerate_database_requests()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var requests = new TrackingRequests();
        await cancellation.CancelAsync();

        // Act
        var recordedException =
            await Record.ExceptionAsync(() => TestDatabaseSession.StartAsync(requests, cancellation.Token));

        // Assert
        var canceledException = Assert.IsAssignableFrom<OperationCanceledException>(recordedException);
        Assert.Equal(cancellation.Token, canceledException.CancellationToken);
        Assert.False(requests.EnumerationStarted);
    }

    /// <summary>
    /// Verifies cancellation while waiting for an external server greeting remains caller cancellation.
    /// </summary>
    [Fact]
    public async Task Cancellation_during_external_endpoint_startup_preserves_the_caller_token()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var environmentVariable = $"DOKA_CANCELLATION_TEST_{Guid.NewGuid():N}";
        var previousConnectionString = Environment.GetEnvironmentVariable(environmentVariable);
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = IPAddress.Loopback.ToString(),
            Port = (uint)((IPEndPoint)listener.LocalEndpoint).Port,
            UserID = "cancellation_probe",
            Password = "unused",
            Pooling = false,
            SslMode = MySqlSslMode.None,
        };

        var request = new TestDatabaseRequest(
            "cancellation-probe",
            TestDatabaseEngine.MariaDb,
            "mariadb:12.3.3",
            Image: null,
            ConnectionStringEnvironmentVariable: environmentVariable);

        Environment.SetEnvironmentVariable(environmentVariable, connectionString.ConnectionString);

        try
        {
            // Act
            var startup = TestDatabaseSession.StartAsync([request], cancellation.Token);
            using var acceptedClient = await listener.AcceptTcpClientAsync(guard.Token);

            // WHY: Accepting proves OpenAsync started; withholding the greeting
            // keeps startup active until cancellation.
            await cancellation.CancelAsync();
            var recordedException = await Record.ExceptionAsync(() => startup.WaitAsync(guard.Token));

            // Assert
            var canceledException = Assert.IsAssignableFrom<OperationCanceledException>(recordedException);
            Assert.Equal(cancellation.Token, canceledException.CancellationToken);
        }
        finally
        {
            // WHY: A failed assertion must also cancel startup and restore the test's isolated endpoint override.
            await cancellation.CancelAsync();
            Environment.SetEnvironmentVariable(environmentVariable, previousConnectionString);
        }
    }

    /// <summary>
    /// Detects any attempt to enumerate requests before checking cancellation.
    /// </summary>
    private sealed class TrackingRequests : IEnumerable<TestDatabaseRequest>
    {
        /// <summary>
        /// Gets whether startup attempted to enumerate the database requests.
        /// </summary>
        public bool EnumerationStarted { get; private set; }

        /// <inheritdoc />
        public IEnumerator<TestDatabaseRequest> GetEnumerator()
        {
            EnumerationStarted = true;
            throw new InvalidOperationException("Canceled startup must not enumerate database requests.");
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
