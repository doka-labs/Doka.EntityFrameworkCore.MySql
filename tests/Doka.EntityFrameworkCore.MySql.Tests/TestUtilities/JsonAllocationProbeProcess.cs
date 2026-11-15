using System.Diagnostics;
using BenchmarkProgram = Doka.EntityFrameworkCore.MySql.Benchmarks.Program;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Runs one JSON allocation window outside the shared test host's GC heap.</summary>
internal static class JsonAllocationProbeProcess
{
    private static readonly string[] s_properties =
    [
        "SchemaVersion", "Scenario", "Iterations", "ProcessId", "ManagedThreadId", "Runtime",
        "AllocatedBytes", "ResultMatches", "Gen0Collections", "Gen1Collections", "Gen2Collections",
        "BackgroundGcBefore", "BackgroundGcAfter",
    ];

    /// <summary>Executes exactly one child measurement and validates its complete proof.</summary>
    /// <param name="scenario">The explicitly selected allocation or control workload.</param>
    /// <param name="cancellationToken">Cancellation for the child process.</param>
    /// <returns>The exact measured bytes and comparer result.</returns>
    internal static async Task<JsonAllocationProbeResult> RunAsync(
        string scenario,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(typeof(BenchmarkProgram).Assembly.Location);
        startInfo.ArgumentList.Add("--allocation-probe");
        startInfo.ArgumentList.Add(scenario);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the JSON allocation probe.");

        // WHY: Draining both streams also during cancellation prevents a child
        // blocked on a full pipe from surviving the test that owns it.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child exited between the observation and Kill.
            }

            // WHY: Cleanup must finish even when the caller has cancelled;
            // otherwise the isolated probe could keep consuming host resources.
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);

            throw;
        }

        var standardOutput = await output;
        var standardError = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"JSON allocation probe failed with exit code {process.ExitCode}: {standardError}{standardOutput}");
        }

        return Parse(standardOutput, scenario, process.Id);
    }

    /// <summary>Rejects incomplete, foreign or GC-overlapping measurement evidence.</summary>
    /// <param name="json">The complete child standard output.</param>
    /// <param name="scenario">The workload the caller actually requested.</param>
    /// <param name="processId">The observed child process identity.</param>
    /// <returns>The exact measured bytes after validating the measurement window.</returns>
    internal static JsonAllocationProbeResult Parse(
        string json,
        string scenario,
        int processId
    )
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("JSON allocation proof must be an object.");
            }

            var properties = root.EnumerateObject().Select(static property => property.Name).ToArray();
            if (properties.Length != s_properties.Length
                || properties.Distinct(StringComparer.Ordinal).Count() != s_properties.Length
                || properties.Except(s_properties, StringComparer.Ordinal).Any())
            {
                throw new InvalidDataException("JSON allocation proof has an incomplete or duplicate property inventory.");
            }

            if (root.GetProperty("SchemaVersion").GetInt32() != 1
                || root.GetProperty("Scenario").GetString() != scenario
                || root.GetProperty("Iterations").GetInt32() != 1000
                || processId <= 0
                || root.GetProperty("ProcessId").GetInt32() != processId
                || root.GetProperty("ManagedThreadId").GetInt32() <= 0
                || root.GetProperty("Runtime").GetString() != RuntimeInformation.FrameworkDescription
                || !root.GetProperty("ResultMatches").GetBoolean())
            {
                throw new InvalidDataException("JSON allocation proof does not identify the requested workload and runtime.");
            }

            var allocatedBytes = root.GetProperty("AllocatedBytes").GetInt64();
            var backgroundBefore = root.GetProperty("BackgroundGcBefore").GetInt64();
            if (allocatedBytes < 0
                || root.GetProperty("Gen0Collections").GetInt32() != 0
                || root.GetProperty("Gen1Collections").GetInt32() != 0
                || root.GetProperty("Gen2Collections").GetInt32() != 0
                || backgroundBefore < 0
                || root.GetProperty("BackgroundGcAfter").GetInt64() != backgroundBefore)
            {
                throw new InvalidDataException("JSON allocation window contains GC overlap or invalid allocation counters.");
            }

            return new JsonAllocationProbeResult(allocatedBytes, true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("JSON allocation proof is malformed.", exception);
        }
    }
}

/// <summary>The unchanged measured result after independent process and GC-window validation.</summary>
/// <param name="AllocatedBytes">The exact same-thread allocation delta, without a noise allowance.</param>
/// <param name="ResultMatches">Whether the actual comparer preserved its expected result.</param>
internal readonly record struct JsonAllocationProbeResult(
    long AllocatedBytes,
    bool ResultMatches);
