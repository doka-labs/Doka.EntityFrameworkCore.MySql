namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies the real unavailable-host boundary used by ordered failover tests.</summary>
public sealed class TcpFaultProxyFailoverTests
{
    private static readonly TimeSpan s_guardTimeout = TimeSpan.FromSeconds(30);

    /// <summary>An occupied loopback port cannot make the unavailable TCP destination valid.</summary>
    [Fact]
    public async Task Unavailable_host_stays_invalid_when_the_same_port_accepts_loopback_connections()
    {
        // Arrange
        using var guard = new CancellationTokenSource(s_guardTimeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var unavailableHost = IPAddress.Parse(TcpFaultProxy.UnavailableTcpHost);
        using var availableClient = new TcpClient(AddressFamily.InterNetwork);
        using var unavailableClient = new TcpClient(AddressFamily.InterNetwork);

        // Act
        // WHY: The successful control proves that port availability cannot explain the rejected destination.
        await availableClient.ConnectAsync(IPAddress.Loopback, port, guard.Token);
        var failure = await Record.ExceptionAsync(() => unavailableClient
            .ConnectAsync(unavailableHost, port, guard.Token)
            .AsTask());

        // Assert
        Assert.True(availableClient.Connected);
        Assert.IsType<SocketException>(failure);
        Assert.False(unavailableClient.Connected);
    }

    /// <summary>The unchanged general proxy still forwards real protocol packets.</summary>
    [Fact]
    public async Task Ordinary_proxy_still_forwards_protocol_packets()
    {
        // Arrange
        using var guard = new CancellationTokenSource(s_guardTimeout);
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();

        await using var proxy = new TcpFaultProxy(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)upstream.LocalEndpoint).Port);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        byte[] packet = [9, 0, 0, 0, 3, .. "SELECT 1"u8];
        var received = new byte[packet.Length];

        // Act
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port, guard.Token);
        using var upstreamClient = await upstream.AcceptTcpClientAsync(guard.Token);
        await client
            .GetStream()
            .WriteAsync(packet, guard.Token);

        await upstreamClient
            .GetStream()
            .ReadExactlyAsync(received, guard.Token);

        // Assert
        Assert.Equal(packet, received);
        Assert.Equal("SELECT 1", Assert.Single(proxy.GetObservedQueries()));
    }

    /// <summary>The proxy's loopback confinement remains independent of the unavailable host constant.</summary>
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Ordinary_proxy_rejects_non_loopback_listeners(
        string listenerAddress
    )
    {
        // Arrange
        var address = IPAddress.Parse(listenerAddress);

        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => new TcpFaultProxy(
            IPAddress.Loopback.ToString(),
            1,
            address));

        // Assert
        Assert.Equal("listenerAddress", failure.ParamName);
    }
}
