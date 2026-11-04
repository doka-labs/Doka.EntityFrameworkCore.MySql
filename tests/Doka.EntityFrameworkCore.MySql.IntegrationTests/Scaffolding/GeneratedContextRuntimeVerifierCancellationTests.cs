using System.IO;

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
        var exception = await Record.ExceptionAsync(
            () => GeneratedContextRuntimeVerifier.VerifyAsync(model, "unused", "unused", cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    /// <summary>Verifies caller cancellation reaps a started noncooperative child without becoming a timeout.</summary>
    [Fact]
    public async Task Caller_cancellation_after_launch_reaps_the_generated_context_process()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var guard = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"doka-child-cancellation-{Guid.NewGuid():N}");
        var markerPath = Path.Combine(temporaryRoot, "child.pid");
        var model = CreateBlockingModel(markerPath);
        Process? child = null;
        Task? verification = null;
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            // Act
            // WHY: Launch, cancel and exit observation form one lifecycle; cancellation alone cannot prove reaping.
            verification = GeneratedContextRuntimeVerifier.VerifyAsync(
                model,
                "Server=127.0.0.1;Database=unused;User ID=unused;Pooling=false",
                "mariadb:12.3",
                cancellation.Token);

            while (!File.Exists(markerPath))
            {
                if (verification.IsCompleted)
                {
                    await verification;
                    throw new InvalidOperationException("The generated context exited before recording its process.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), guard.Token);
            }

            var identity = (await File.ReadAllTextAsync(markerPath, guard.Token))
                .Split('|');

            var processId = int.Parse(identity[0], CultureInfo.InvariantCulture);
            var startedUtcTicks = long.Parse(identity[1], CultureInfo.InvariantCulture);
            child = Process.GetProcessById(processId);

            if (child.StartTime.ToUniversalTime().Ticks != startedUtcTicks)
            {
                throw new InvalidOperationException("The generated context process identity no longer matches.");
            }

            await cancellation.CancelAsync();

            // WHY: Bound a broken cancellation observer independently of the helper's execution deadline.
            var exception = await Record.ExceptionAsync(
                () => verification.WaitAsync(TimeSpan.FromSeconds(30), guard.Token));

            if (exception is OperationCanceledException)
            {
                await child.WaitForExitAsync(guard.Token);
            }

            // Assert
            var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.True(child.HasExited);
        }
        finally
        {
            // WHY: Failed synchronization or assertions must also terminate and observe the owned verification task.
            await cancellation.CancelAsync();

            try
            {
                // WHY: A regression may ignore cancellation. Signal only this
                // generated fixture to exit; never terminate a process by a recycled PID.
                await File.WriteAllTextAsync(markerPath + ".stop", string.Empty, CancellationToken.None);

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
            finally
            {
                child?.Dispose();
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    /// <summary>Creates a child context that records its identity before blocking without database access.</summary>
    /// <param name="markerPath">The test-owned atomic process marker.</param>
    /// <returns>A minimal scaffold that forces the verifier to terminate a real child process.</returns>
    private static ScaffoldedModel CreateBlockingModel(
        string markerPath
    ) => new()
    {
        ContextFile = new ScaffoldedFile(
            "RuntimeSchemaContext.cs",
            $$"""
                using System;
                using System.Diagnostics;
                using System.Globalization;
                using System.IO;
                using System.Threading;
                using Microsoft.EntityFrameworkCore;

                namespace Doka.Scaffolding;

                /// <summary>Blocks before model initialization to exercise operating-system termination.</summary>
                public sealed class RuntimeSchemaContext : DbContext
                {
                    /// <summary>Records this owned child before waiting for its parent to terminate it.</summary>
                    /// <param name="options">The options supplied by the generated program.</param>
                    public RuntimeSchemaContext(
                        DbContextOptions<RuntimeSchemaContext> options
                    ) : base(options)
                    {
                        const string marker = {{JsonSerializer.Serialize(markerPath)}};
                        using var currentProcess = Process.GetCurrentProcess();
                        var identity = Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
                            + "|" + currentProcess.StartTime.ToUniversalTime().Ticks
                                .ToString(CultureInfo.InvariantCulture);

                        File.WriteAllText(
                            marker + ".tmp",
                            identity);
                        File.Move(marker + ".tmp", marker);

                        // WHY: Caller cancellation cannot end this wait. The test-only
                        // stop file is written after assertions to protect cleanup on failure.
                        while (!File.Exists(marker + ".stop"))
                        {
                            Thread.Sleep(25);
                        }

                        Environment.Exit(0);
                    }
                }
                """),
    };
}
