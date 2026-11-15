using System.Text.Json.Nodes;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects the isolated allocation counter against false zero and incomplete proof.</summary>
public sealed class JsonAllocationProbeTests
{
    /// <summary>The isolated process must retain a genuinely allocation-free negative control.</summary>
    [Fact]
    public async Task Empty_control_allocates_exactly_zero()
    {
        // Arrange
        const string scenario = "empty";

        // Act
        var result = await JsonAllocationProbeProcess.RunAsync(scenario, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.AllocatedBytes);
        Assert.True(result.ResultMatches);
    }

    /// <summary>Real allocations remain observable after empty or complete comparer workloads.</summary>
    /// <param name="scenario">The workload followed by one deliberate allocation.</param>
    [Theory]
    [InlineData("allocate")]
    [InlineData("equality-allocate")]
    [InlineData("hash-allocate")]
    public async Task Positive_control_observes_deliberate_allocation(
        string scenario
    )
    {
        // Arrange
        const long minimumArrayPayloadBytes = 37;

        // Act
        var result = await JsonAllocationProbeProcess.RunAsync(scenario, CancellationToken.None);

        // Assert
        Assert.True(result.AllocatedBytes >= minimumArrayPayloadBytes);
        Assert.True(result.ResultMatches);
    }

    /// <summary>Unrecognized CLI inputs cannot fall through to successful measurement evidence.</summary>
    /// <param name="scenario">The malformed or unknown workload identity.</param>
    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("Equality")]
    public async Task Invalid_scenario_is_rejected_by_the_actual_child(
        string scenario
    )
    {
        // Arrange
        Func<Task> act = async () => await JsonAllocationProbeProcess.RunAsync(scenario, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(act);

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>GC overlap, identity changes and omitted metadata cannot produce successful proof.</summary>
    /// <param name="mutation">The invalid evidence field or shape.</param>
    [Theory]
    [InlineData("schema")]
    [InlineData("scenario")]
    [InlineData("iterations")]
    [InlineData("process")]
    [InlineData("thread")]
    [InlineData("runtime")]
    [InlineData("result")]
    [InlineData("allocation")]
    [InlineData("gen0")]
    [InlineData("gen1")]
    [InlineData("gen2")]
    [InlineData("background")]
    [InlineData("negative-background")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("wrong-type")]
    [InlineData("array-root")]
    [InlineData("fractional-bytes")]
    public void Invalid_measurement_proof_is_rejected(
        string mutation
    )
    {
        // Arrange
        var proof = CreateProof();

        switch (mutation)
        {
            case "schema":
                proof["SchemaVersion"] = 2;
                break;
            case "scenario":
                proof["Scenario"] = "hash";
                break;
            case "iterations":
                proof["Iterations"] = 999;
                break;
            case "process":
                proof["ProcessId"] = 18;
                break;
            case "thread":
                proof["ManagedThreadId"] = 0;
                break;
            case "runtime":
                proof["Runtime"] = ".NET other";
                break;
            case "result":
                proof["ResultMatches"] = false;
                break;
            case "allocation":
                proof["AllocatedBytes"] = -1;
                break;
            case "gen0":
                proof["Gen0Collections"] = 1;
                break;
            case "gen1":
                proof["Gen1Collections"] = 1;
                break;
            case "gen2":
                proof["Gen2Collections"] = 1;
                break;
            case "background":
                proof["BackgroundGcAfter"] = 2;
                break;
            case "negative-background":
                proof["BackgroundGcBefore"] = -1;
                break;
            case "missing":
                proof.Remove("AllocatedBytes");
                break;
            case "unknown":
                proof["Unexpected"] = 0;
                break;
            case "wrong-type":
                proof["AllocatedBytes"] = "0";
                break;
            case "fractional-bytes":
                proof["AllocatedBytes"] = 0.5;
                break;
        }

        var json = mutation switch
        {
            "duplicate" => proof.ToJsonString().Replace("\"AllocatedBytes\":0", "\"AllocatedBytes\":0,\"AllocatedBytes\":0", StringComparison.Ordinal),
            "malformed" => "not json",
            "array-root" => "[]",
            _ => proof.ToJsonString(),
        };

        // Act
        var exception = Record.Exception(() => JsonAllocationProbeProcess.Parse(json, "empty", 17));

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>Proof validation retains the observed positive bytes instead of normalizing them away.</summary>
    [Fact]
    public void Positive_bytes_are_preserved_in_valid_proof()
    {
        // Arrange
        var proof = CreateProof();
        proof["AllocatedBytes"] = 64;

        // Act
        var result = JsonAllocationProbeProcess.Parse(proof.ToJsonString(), "empty", 17);

        // Assert
        Assert.Equal(64, result.AllocatedBytes);
    }

    /// <summary>A pre-cancelled caller cannot start an independent measurement process.</summary>
    [Fact]
    public async Task Precancelled_probe_preserves_the_callers_token()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => JsonAllocationProbeProcess.RunAsync("empty", cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    private static JsonObject CreateProof() => new()
    {
        ["SchemaVersion"] = 1,
        ["Scenario"] = "empty",
        ["Iterations"] = 1000,
        ["ProcessId"] = 17,
        ["ManagedThreadId"] = 1,
        ["Runtime"] = RuntimeInformation.FrameworkDescription,
        ["AllocatedBytes"] = 0,
        ["ResultMatches"] = true,
        ["Gen0Collections"] = 0,
        ["Gen1Collections"] = 0,
        ["Gen2Collections"] = 0,
        ["BackgroundGcBefore"] = 1,
        ["BackgroundGcAfter"] = 1,
    };
}
