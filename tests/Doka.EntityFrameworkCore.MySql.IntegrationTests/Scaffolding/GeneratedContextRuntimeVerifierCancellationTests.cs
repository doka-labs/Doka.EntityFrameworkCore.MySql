using System.Net;
using System.Net.Sockets;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies cancellation cannot leave a generated-context process running.</summary>
public sealed class GeneratedContextRuntimeVerifierCancellationTests
{
    /// <summary>Verifies cancellation is checked before generating or starting a scaffolded project.</summary>
    [Fact]
    public async Task Pre_canceled_verification_does_not_access_the_unconfigured_scaffold()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // WHY: Leaving ContextFile unset distinguishes the entry guard from cancellation after project generation.
        var model = new ScaffoldedModel();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            GeneratedContextRuntimeVerifier.VerifyAsync(model, "unused", "unused", cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    /// <summary>Verifies caller cancellation terminates a started noncooperative child without becoming a timeout.</summary>
    [Fact]
    public async Task Caller_cancellation_after_launch_terminates_the_generated_context_process()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var nonce = Guid.NewGuid().ToByteArray();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var model = CreateBlockingModel(port, Convert.ToHexString(nonce));
        var connection = listener.AcceptTcpClientAsync(guard.Token).AsTask();
        TcpClient? child = null;
        Task? verification = null;

        try
        {
            // Act
            // WHY: Observe this child's lifetime without cross-process StartTime comparisons;
            // Linux derives UTC start times from independently sampled boot-time estimates.
            verification = GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                "Server=127.0.0.1;Database=unused;User ID=unused;Pooling=false",
                "mariadb:12.3",
                cancellation.Token);

            await Task.WhenAny(connection, verification);

            if (verification.IsCompleted)
            {
                await verification;

                throw new InvalidOperationException("The generated context exited before connecting to its test.");
            }

            child = await connection;
            var stream = child.GetStream();
            var handshake = new byte[nonce.Length];
            await stream.ReadExactlyAsync(handshake, guard.Token);

            if (!handshake.AsSpan().SequenceEqual(nonce))
            {
                throw new InvalidOperationException("The generated context connection did not identify this test.");
            }

            await cancellation.CancelAsync();

            // WHY: Bound a broken cancellation observer independently of the helper's execution deadline.
            var exception =
                await Record.ExceptionAsync(() => verification.WaitAsync(TimeSpan.FromSeconds(30), guard.Token));

            // WHY: The child holds its socket while blocking without a cancellation token.
            // Read EOF before parent cleanup; no clock value or execution speed determines success.
            var received = exception is OperationCanceledException
                ? await stream.ReadAsync(new byte[1], guard.Token)
                : -1;

            // Assert
            var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, received);
        }
        finally
        {
            // WHY: Failed synchronization or assertions must also terminate and observe the owned verification task.
            await cancellation.CancelAsync();

            // WHY: Closing the owned connection releases the fixture even if termination
            // regresses. This happens after the EOF assertion, so cleanup cannot satisfy it.
            child?.Dispose();
            await guard.CancelAsync();
            listener.Stop();

            try
            {
                using var accepted = await connection;
            }
            catch (OperationCanceledException)
            {
                // An unaccepted connection is canceled by the test's cleanup above.
            }
            finally
            {
                if (verification is not null)
                {
                    try
                    {
                        await verification;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected after the cleanup cancellation above.
                    }
                }
            }
        }
    }

    /// <summary>Creates a child context that identifies its connection before blocking without database access.</summary>
    /// <param name="port">The test-owned loopback listener port.</param>
    /// <param name="nonce">The hexadecimal identity sent only by this generated fixture.</param>
    /// <returns>A minimal scaffold that forces the verifier to terminate a real child process.</returns>
    private static ScaffoldedModel CreateBlockingModel(
        int port,
        string nonce
    ) => new()
    {
        ContextFile = new ScaffoldedFile(
            "RuntimeSchemaContext.cs",
            $$"""
              using System;
              using System.Net;
              using System.Net.Sockets;
              using Microsoft.EntityFrameworkCore;

              namespace Doka.Scaffolding;

              /// <summary>Blocks before model initialization to exercise operating-system termination.</summary>
              public sealed class RuntimeSchemaContext : DbContext
              {
                  /// <summary>Identifies this owned child before waiting for its parent to terminate it.</summary>
                  /// <param name="options">The options supplied by the generated program.</param>
                  public RuntimeSchemaContext(
                      DbContextOptions<RuntimeSchemaContext> options
                  ) : base(options)
                  {
                      using var client = new TcpClient();
                      client.Connect(IPAddress.Loopback, {{port.ToString(CultureInfo.InvariantCulture)}});
                      using var stream = client.GetStream();
                      stream.Write(Convert.FromHexString("{{nonce}}"));

                      // WHY: Caller cancellation cannot end this blocking read. The parent
                      // sends no data and keeps its endpoint open until termination is observed.
                      _ = stream.ReadByte();

                      Environment.Exit(0);
                  }
              }
              """),
    };
}
