using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

public sealed class BenchmarkDatabaseTargetTests
{
    [Fact]
    public void Every_contract_target_resolves_without_a_second_engine_matrix()
    {
        var contract = PerformanceContract.Load();

        foreach (var (targetId, expected) in contract.RequiredTargets)
        {
            var target = BenchmarkDatabaseTarget.Resolve(targetId, configuredPort: null, contract);

            Assert.Equal(targetId, target.TargetId);
            Assert.Equal(expected.DisplayName, target.DisplayName);
            Assert.Equal(expected.EngineFamily, target.EngineFamily);
            Assert.Equal(Version.Parse(expected.ServerVersion), target.ServerVersion);
            Assert.Equal(expected.HostPort, target.Port);
            Assert.Equal(expected.EngineFamily == "MariaDB", target.IsMariaDb);
        }
    }

    [Fact]
    public void Explicit_port_overrides_the_contract_default()
    {
        var target = BenchmarkDatabaseTarget.Resolve("mysql84", "49152", PerformanceContract.Load());

        Assert.Equal(49152, target.Port);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void Invalid_explicit_port_is_rejected(
        string configuredPort
    )
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkDatabaseTarget.Resolve("mysql84", configuredPort, PerformanceContract.Load()));

        Assert.Contains("TCP port between 1 and 65535", exception.Message);
    }
}
