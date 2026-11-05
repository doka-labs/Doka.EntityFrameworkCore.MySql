namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies canceling one observer leaves the proxy's pending fault signal usable.
/// </summary>
public sealed class TcpFaultProxyCancellationTests
{
    private static readonly TimeSpan s_guardTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Verifies a canceled COMMIT observer neither consumes the armed fault nor cancels its signal.
    /// </summary>
    [Fact]
    public async Task Canceling_commit_fault_wait_preserves_the_armed_fault_for_the_next_observer()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(s_guardTimeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        await using var proxy = new TcpFaultProxy(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)listener.LocalEndpoint).Port);

        proxy.ArmCommitFault(CommitFaultMode.BeforeRequest);

        using var client = new TcpClient();
        byte[] commitPacket = [7, 0, 0, 0, 3, .. "COMMIT"u8];

        // Act
        // WHY: Cancel-then-observe is one lifecycle: the second wait must reuse the same still-armed signal.
        var canceledWait = proxy.WaitForCommitFaultAsync(s_guardTimeout, cancellation.Token);
        await cancellation.CancelAsync();
        var recordedException = await Record.ExceptionAsync(() => canceledWait.WaitAsync(guard.Token));

        await client.ConnectAsync(proxy.Host, proxy.Port, guard.Token);
        using var upstreamClient = await listener.AcceptTcpClientAsync(guard.Token);
        await client
            .GetStream()
            .WriteAsync(commitPacket, guard.Token);

        var observedFault = await proxy.WaitForCommitFaultAsync(s_guardTimeout, guard.Token);

        // Assert
        var canceledException = Assert.IsAssignableFrom<OperationCanceledException>(recordedException);
        Assert.Equal(cancellation.Token, canceledException.CancellationToken);
        Assert.Equal(CommitFaultMode.BeforeRequest, observedFault);
        Assert.Equal("COMMIT", Assert.Single(proxy.GetObservedQueries()));
    }

    /// <summary>
    /// Verifies a canceled response observer leaves the pending fault available after a query response.
    /// </summary>
    [Fact]
    public async Task Canceling_query_response_fault_wait_preserves_the_signal_for_the_next_observer()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(s_guardTimeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        await using var proxy = new TcpFaultProxy(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)listener.LocalEndpoint).Port);

        const string marker = "SELECT 1";
        proxy.ArmQueryResponseFault(marker, responsePacketsToForward: 1);

        using var client = new TcpClient();
        byte[] queryPacket = [9, 0, 0, 0, 3, .. "SELECT 1"u8];
        byte[] responsePacket = [1, 0, 0, 1, 0];
        var forwardedPacket = new byte[queryPacket.Length];

        // Act
        // WHY: The query and response must exercise the original signal after its first observer canceled.
        var canceledWait = proxy.WaitForQueryResponseFaultAsync(s_guardTimeout, cancellation.Token);
        await cancellation.CancelAsync();
        var recordedException = await Record.ExceptionAsync(() => canceledWait.WaitAsync(guard.Token));

        await client.ConnectAsync(proxy.Host, proxy.Port, guard.Token);
        using var upstreamClient = await listener.AcceptTcpClientAsync(guard.Token);
        await client
            .GetStream()
            .WriteAsync(queryPacket, guard.Token);

        // WHY: Receiving the complete query proves the proxy armed response counting before the response is sent.
        await upstreamClient
            .GetStream()
            .ReadExactlyAsync(forwardedPacket, guard.Token);

        await upstreamClient
            .GetStream()
            .WriteAsync(responsePacket, guard.Token);

        await proxy.WaitForQueryResponseFaultAsync(s_guardTimeout, guard.Token);

        // Assert
        var canceledException = Assert.IsAssignableFrom<OperationCanceledException>(recordedException);
        Assert.Equal(cancellation.Token, canceledException.CancellationToken);
        Assert.Equal(queryPacket, forwardedPacket);
        Assert.Equal(marker, Assert.Single(proxy.GetObservedQueries()));
    }
}
