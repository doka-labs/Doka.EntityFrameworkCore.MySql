namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class PerformanceGateTests
{
    /// <summary>
    /// Supplies every checked-in engine target at and above its sliding-cache throughput maximum.
    /// </summary>
    public static IEnumerable<object[]> CacheSlidingControlCases()
    {
        using var document = LoadCheckedInPerformanceContract();
        foreach (var target in document.RootElement.GetProperty("requiredTargets").EnumerateObject())
        {
            yield return [target.Name, 1d, 0];
            yield return [target.Name, 2d, 1];
        }
    }

    /// <summary>
    /// Supplies each checked-in generic LIKE control with boundary, regression, and missing-evidence cases.
    /// </summary>
    public static IEnumerable<object[]> GenericLikeControlCases()
    {
        using var document = LoadCheckedInPerformanceContract();
        var controls = document.RootElement.GetProperty("benchmarkDotNetControls").EnumerateArray()
            .Where(control => control.GetProperty("type").GetString() == nameof(Benchmarks.GenericLikeBenchmark));

        foreach (var control in controls)
        {
            var controlId = control.GetProperty("id").GetString()!;
            yield return [controlId, 0, true, 0];
            yield return [controlId, 1, true, 1];
            yield return [controlId, 0, false, 78];
        }
    }

    private static JsonDocument LoadCheckedInPerformanceContract() => JsonDocument.Parse(
        File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "performance-contract.json")));
}
