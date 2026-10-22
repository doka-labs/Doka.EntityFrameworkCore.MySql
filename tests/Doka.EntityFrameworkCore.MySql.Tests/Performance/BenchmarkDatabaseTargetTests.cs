using Doka.EntityFrameworkCore.MySql.Benchmarks;

namespace Doka.EntityFrameworkCore.MySql.Tests.Performance;

/// <summary>
/// Verifies benchmark targets follow the shared engine matrix and explicit connection overrides.
/// </summary>
public sealed class BenchmarkDatabaseTargetTests
{
    /// <summary>
    /// Supplies every target from the contract without maintaining a duplicate engine list.
    /// </summary>
    public static IEnumerable<object[]> ContractTargets() => PerformanceContract.Load().RequiredTargets.Keys
        .Select(targetId => new object[] { targetId });

    /// <summary>
    /// Verifies every contract target resolves without a second engine matrix.
    /// </summary>
    /// <param name="targetId">The target selected from the shared performance contract.</param>
    [Theory]
    [MemberData(nameof(ContractTargets))]
    public void Every_contract_target_resolves_without_a_second_engine_matrix(
        string targetId
    )
    {
        // Arrange
        var contract = PerformanceContract.Load();
        var expected = contract.RequiredTargets[targetId];

        // Act
        var target = BenchmarkDatabaseTarget.Resolve(targetId, configuredPort: null, contract);

        // Assert
        Assert.Equal(targetId, target.TargetId);
        Assert.Equal(expected.DisplayName, target.DisplayName);
        Assert.Equal(expected.EngineFamily, target.EngineFamily);
        Assert.Equal(Version.Parse(expected.ServerVersion), target.ServerVersion);
        Assert.Equal(expected.HostPort, target.Port);
        Assert.Equal(expected.EngineFamily == "MariaDB", target.IsMariaDb);
    }

    /// <summary>
    /// Verifies explicit port overrides the contract default.
    /// </summary>
    [Fact]
    public void Explicit_port_overrides_the_contract_default()
    {
        // Arrange
        var contract = PerformanceContract.Load();

        // Act
        var target = BenchmarkDatabaseTarget.Resolve("mysql84", "49152", contract);

        // Assert
        Assert.Equal(49152, target.Port);
    }

    /// <summary>
    /// Verifies invalid explicit port is rejected.
    /// </summary>
    /// <param name="configuredPort">The malformed or out-of-range port override.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void Invalid_explicit_port_is_rejected(
        string configuredPort
    )
    {
        // Arrange
        Action act = () =>
            BenchmarkDatabaseTarget.Resolve("mysql84", configuredPort, PerformanceContract.Load());

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Contains("TCP port between 1 and 65535", exception.Message);
    }
}
