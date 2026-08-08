using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

public sealed class PerformanceSamplingTests
{
    [Fact]
    public void Stable_population_keeps_the_current_sample_target()
    {
        var target = PerformanceSampling.NextSampleTarget(
            256,
            1024,
            32,
            0.25,
            0.25);

        Assert.Equal(256, target);
    }

    [Fact]
    public void Unstable_population_extends_by_one_calibration_block()
    {
        var target = PerformanceSampling.NextSampleTarget(
            256,
            1024,
            32,
            0.273598,
            0.25);

        Assert.Equal(288, target);
    }

    [Fact]
    public void Unstable_population_stops_at_the_contract_cap()
    {
        var target = PerformanceSampling.NextSampleTarget(
            1024,
            1024,
            32,
            0.30,
            0.25);

        Assert.Equal(1024, target);
    }

    [Fact]
    public void Relative_standard_error_uses_sample_error_and_median()
    {
        var actual = PerformanceSampling.RelativeStandardError([1d, 2d, 3d]);

        Assert.Equal(0.28867513459481287, actual, 12);
    }

    [Fact]
    public void Uniform_population_has_zero_relative_standard_error()
    {
        var actual = PerformanceSampling.RelativeStandardError([2d, 2d, 2d]);

        Assert.Equal(0, actual);
    }
}
