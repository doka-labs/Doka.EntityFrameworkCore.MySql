namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// A packet-aware loopback proxy for deterministic MySQL transport-fault tests.
/// It can cut arbitrary active sessions or inject a one-shot fault at the
/// request/response boundary of a plain-text COMMIT command.
/// </summary>
public sealed class TcpFaultProxy : IAsyncDisposable
{
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<long, ProxySession> _sessions = new();
    private readonly ConcurrentQueue<string> _observedQueries = new();
    private readonly Lock _queryFaultGate = new();
    private readonly Task _acceptLoop;
    private TaskCompletionSource<CommitFaultMode> _faultObserved = CreateFaultSignal();
    private TaskCompletionSource<bool> _queryFaultObserved = CreateQueryFaultSignal();
    private QueryFaultPlan? _armedQueryFault;
    private long _nextSessionId;
    private int _armedCommitFault;

    /// <summary>
    /// Starts a loopback listener that forwards packets to the given MySQL endpoint.
    /// </summary>
    /// <param name="upstreamHost">The upstream database host.</param>
    /// <param name="upstreamPort">The upstream database port.</param>
    /// <param name="listenerAddress">
    /// The loopback address to expose, or <see langword="null"/> for the
    /// platform's default IPv4 loopback address.
    /// </param>
    public TcpFaultProxy(
        string upstreamHost,
        int upstreamPort,
        IPAddress? listenerAddress = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamHost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(upstreamPort);
        listenerAddress ??= IPAddress.Loopback;

        if (!IPAddress.IsLoopback(listenerAddress))
        {
            throw new ArgumentOutOfRangeException(
                nameof(listenerAddress),
                listenerAddress,
                "The fault proxy must remain confined to a loopback interface.");
        }

        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _listener = new TcpListener(listenerAddress, 0);
        _listener.Start();
        _acceptLoop = AcceptConnectionsAsync(_shutdown.Token);
    }

    /// <summary>
    /// Gets the loopback address on which the proxy accepts connections.
    /// </summary>
    public string Host => ((IPEndPoint)_listener.LocalEndpoint).Address.ToString();

    /// <summary>
    /// Gets the ephemeral loopback port on which the proxy accepts connections.
    /// </summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Arms one fault for the next COMMIT packet. TLS must be disabled on the
    /// proxied test connection so the command boundary remains observable.
    /// </summary>
    public void ArmCommitFault(
        CommitFaultMode mode
    )
    {
        if (mode == CommitFaultMode.None)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (Interlocked.CompareExchange(ref _armedCommitFault, (int)mode, (int)CommitFaultMode.None)
            != (int)CommitFaultMode.None)
        {
            throw new InvalidOperationException("A COMMIT fault is already armed.");
        }

        _faultObserved = CreateFaultSignal();
    }

    /// <summary>
    /// Waits until the armed COMMIT fault reaches its selected protocol boundary.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for the fault.</param>
    /// <returns>The protocol boundary observed by the proxy.</returns>
    public async Task<CommitFaultMode> WaitForCommitFaultAsync(
        TimeSpan timeout
    ) => await _faultObserved
        .Task.WaitAsync(timeout)
        .ConfigureAwait(false);

    /// <summary>
    /// Arms a one-shot disconnect after the proxy forwards the requested number
    /// of response packets for the next query containing <paramref name="marker"/>.
    /// </summary>
    public void ArmQueryResponseFault(
        string marker,
        int responsePacketsToForward
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(responsePacketsToForward);

        lock (_queryFaultGate)
        {
            if (_armedQueryFault is not null)
            {
                throw new InvalidOperationException("A query-response fault is already armed.");
            }

            _queryFaultObserved = CreateQueryFaultSignal();
            _armedQueryFault = new QueryFaultPlan(marker, responsePacketsToForward);
        }
    }

    /// <summary>
    /// Waits until the armed response-packet fault disconnects the session.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for the fault.</param>
    public async Task WaitForQueryResponseFaultAsync(
        TimeSpan timeout
    ) => await _queryFaultObserved
        .Task.WaitAsync(timeout)
        .ConfigureAwait(false);

    /// <summary>
    /// Returns a stable snapshot of the plain-text COM_QUERY packets observed by
    /// the proxy. Intended for protocol-boundary assertions and failure evidence.
    /// </summary>
    public IReadOnlyList<string> GetObservedQueries() => _observedQueries.ToArray();

    /// <summary>
    /// Aborts every active proxied session and returns the number observed.
    /// </summary>
    public int DropActiveConnections()
    {
        var sessions = _sessions.Values.ToList();

        foreach (var session in sessions)
        {
            session.AbortWithReset();
        }

        return sessions.Count;
    }

    /// <summary>
    /// Rewrites a database connection string for deterministic packet inspection.
    /// TLS, pooling, compression, and pipelining are disabled because each can
    /// obscure the protocol boundary under test. A short command timeout bounds
    /// the intentional request-blackhole scenario.
    /// </summary>
    /// <param name="connectionString">The direct upstream connection string.</param>
    /// <returns>A connection string targeting this proxy's loopback listener.</returns>
    public string BuildConnectionString(
        string connectionString
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            Server = Host,
            Port = (uint)Port,
            Pooling = false,
            Pipelining = false,
            SslMode = MySqlSslMode.None,
            UseCompression = false,
            DefaultCommandTimeout = 2,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Stops the listener and aborts all active proxied sessions.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _shutdown
            .CancelAsync()
            .ConfigureAwait(false);
        _listener.Stop();
        DropActiveConnections();

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        _shutdown.Dispose();
    }

    private async Task AcceptConnectionsAsync(
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener
                    .AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var sessionId = Interlocked.Increment(ref _nextSessionId);
            var session = new ProxySession(this, sessionId, client);
            _sessions[sessionId] = session;
            _ = RunSessionAsync(session);
        }
    }

    private async Task RunSessionAsync(
        ProxySession session
    )
    {
        try
        {
            await session
                .RunAsync(_shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!_shutdown.IsCancellationRequested)
        {
            // Transport failures are the output of this test utility. The test
            // observes them through the client and the explicit fault signal.
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            session.Abort();
        }
    }

    private CommitFaultMode ConsumeCommitFault() =>
        (CommitFaultMode)Interlocked.Exchange(ref _armedCommitFault, (int)CommitFaultMode.None);

    private void SignalFault(
        CommitFaultMode mode
    ) => _faultObserved.TrySetResult(mode);

    private QueryFaultPlan? ConsumeQueryResponseFault(
        string? query
    )
    {
        lock (_queryFaultGate)
        {
            if (_armedQueryFault is null
                || query?.Contains(_armedQueryFault.Marker, StringComparison.Ordinal) != true)
            {
                return null;
            }

            var fault = _armedQueryFault;
            _armedQueryFault = null;
            return fault;
        }
    }

    private void SignalQueryResponseFault() => _queryFaultObserved.TrySetResult(true);

    private static TaskCompletionSource<CommitFaultMode> CreateFaultSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CreateQueryFaultSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string? ReadQuery(
        byte[] payload
    )
    {
        const byte comQuery = 0x03;

        if (payload.Length <= 1
            || payload[0] != comQuery)
        {
            return null;
        }

        var queryOffset = 1;

        // MySQL 8 negotiates CLIENT_QUERY_ATTRIBUTES. With no query attributes,
        // COM_QUERY carries length-encoded parameter_count=0 and
        // parameter_set_count=1 before the SQL text. MariaDB uses the classic
        // payload. Fault tests never attach non-zero query attributes.
        if (payload.Length > 3
            && payload[1] == 0
            && payload[2] == 1)
        {
            queryOffset = 3;
        }

        return Encoding.ASCII.GetString(payload, queryOffset, payload.Length - queryOffset);
    }

    private sealed class ProxySession : IDisposable
    {
        private readonly TcpFaultProxy _owner;
        private readonly TcpClient _client;
        private readonly TcpClient _upstream = new();
        private int _aborted;
        private int _commitResponseFault;
        private int _queryResponsePacketsRemaining;

        public ProxySession(
            TcpFaultProxy owner,
            long id,
            TcpClient client
        )
        {
            _owner = owner;
            Id = id;
            _client = client;
            _client.NoDelay = true;
            _upstream.NoDelay = true;
        }

        public long Id { get; }

        public async Task RunAsync(
            CancellationToken cancellationToken
        )
        {
            await _upstream
                .ConnectAsync(_owner._upstreamHost, _owner._upstreamPort, cancellationToken)
                .ConfigureAwait(false);

            var clientToServer = ForwardClientPacketsAsync(cancellationToken);
            var serverToClient = ForwardServerPacketsAsync(cancellationToken);

            await Task
                .WhenAny(clientToServer, serverToClient)
                .ConfigureAwait(false);
        }

        public void Abort() => Close(reset: false);

        /// <summary>
        /// Closes both sockets with zero linger so a simulated transport loss is
        /// observed as TCP RST rather than a graceful end-of-stream.
        /// </summary>
        public void AbortWithReset()
        {
            Close(reset: true);
        }

        public void Dispose() => Abort();

        private void Close(
            bool reset
        )
        {
            if (Interlocked.Exchange(ref _aborted, 1) != 0)
            {
                return;
            }

            if (reset)
            {
                _client.Client.LingerState = new LingerOption(enable: true, seconds: 0);
                _upstream.Client.LingerState = new LingerOption(enable: true, seconds: 0);
            }

            _client.Dispose();
            _upstream.Dispose();
        }

        private async Task ForwardClientPacketsAsync(
            CancellationToken cancellationToken
        )
        {
            var source = _client.GetStream();
            var destination = _upstream.GetStream();

            while (await ReadPacketAsync(source, cancellationToken)
                       .ConfigureAwait(false) is { } packet)
            {
                var query = ReadQuery(packet.Payload);
                if (query is not null)
                {
                    _owner._observedQueries.Enqueue(query);
                }

                var queryFault = _owner.ConsumeQueryResponseFault(query);
                if (queryFault is not null)
                {
                    Volatile.Write(ref _queryResponsePacketsRemaining, queryFault.ResponsePacketsToForward);
                }

                var fault = IsCommit(query) ? _owner.ConsumeCommitFault() : CommitFaultMode.None;

                if (fault == CommitFaultMode.BeforeRequest)
                {
                    _owner.SignalFault(fault);
                    continue;
                }

                if (fault is CommitFaultMode.BeforeResponse or CommitFaultMode.AfterResponse)
                {
                    Volatile.Write(ref _commitResponseFault, (int)fault);
                }

                await WritePacketAsync(destination, packet, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task ForwardServerPacketsAsync(
            CancellationToken cancellationToken
        )
        {
            var source = _upstream.GetStream();
            var destination = _client.GetStream();

            while (await ReadPacketAsync(source, cancellationToken)
                       .ConfigureAwait(false) is { } packet)
            {
                var fault = (CommitFaultMode)Volatile.Read(ref _commitResponseFault);

                if (fault == CommitFaultMode.BeforeResponse)
                {
                    _owner.SignalFault(fault);
                    AbortWithReset();
                    return;
                }

                await WritePacketAsync(destination, packet, cancellationToken)
                    .ConfigureAwait(false);

                if (Volatile.Read(ref _queryResponsePacketsRemaining) > 0
                    && Interlocked.Decrement(ref _queryResponsePacketsRemaining) == 0)
                {
                    _owner.SignalQueryResponseFault();
                    AbortWithReset();
                    return;
                }

                if (fault == CommitFaultMode.AfterResponse)
                {
                    _owner.SignalFault(fault);
                    Abort();
                    return;
                }
            }
        }

        private static bool IsCommit(
            string? query
        )
        {
            if (query is null)
            {
                return false;
            }

            var command = query.Trim();
            if (!command.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return command.Length == "COMMIT".Length
                || char.IsWhiteSpace(command["COMMIT".Length])
                || command["COMMIT".Length] == ';';
        }

        private static async Task<MySqlPacket?> ReadPacketAsync(
            NetworkStream stream,
            CancellationToken cancellationToken
        )
        {
            var header = new byte[4];

            if (!await ReadExactlyOrEofAsync(stream, header, cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }

            var payloadLength = header[0] | header[1] << 8 | header[2] << 16;
            var payload = new byte[payloadLength];

            await stream
                .ReadExactlyAsync(payload, cancellationToken)
                .ConfigureAwait(false);

            return new MySqlPacket(header, payload);
        }

        private static async Task<bool> ReadExactlyOrEofAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken cancellationToken
        )
        {
            var offset = 0;

            while (offset < buffer.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(offset), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    if (offset == 0)
                    {
                        return false;
                    }

                    throw new EndOfStreamException("The MySQL packet header ended prematurely.");
                }

                offset += read;
            }

            return true;
        }

        private static async Task WritePacketAsync(
            NetworkStream stream,
            MySqlPacket packet,
            CancellationToken cancellationToken
        )
        {
            await stream
                .WriteAsync(packet.Header, cancellationToken)
                .ConfigureAwait(false);
            await stream
                .WriteAsync(packet.Payload, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed record MySqlPacket(
        byte[] Header,
        byte[] Payload
    );

    private sealed record QueryFaultPlan(
        string Marker,
        int ResponsePacketsToForward
    );
}

/// <summary>
/// Selects the exact COMMIT protocol boundary at which the proxy disconnects.
/// </summary>
public enum CommitFaultMode
{
    /// <summary>
    /// No COMMIT fault is armed.
    /// </summary>
    None,

    /// <summary>
    /// Drops the COMMIT request before it reaches the server and leaves the
    /// connection blackholed until the driver command timeout expires.
    /// </summary>
    BeforeRequest,

    /// <summary>
    /// Forwards COMMIT to the server, then resets the connection before the
    /// server response reaches the client.
    /// </summary>
    BeforeResponse,

    /// <summary>
    /// Forwards both COMMIT and its server response, then closes the connection.
    /// </summary>
    AfterResponse,
}
