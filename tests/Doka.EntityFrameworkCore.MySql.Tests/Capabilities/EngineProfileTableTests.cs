namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies every version boundary that changes an engine capability consumed
/// by provider behavior.
/// </summary>
public sealed partial class EngineProfileTableTests
{
    /// <summary>
    /// Preserves stable profile references for repeated exact versions so the
    /// bounded cache still supports EF Core service-provider reuse.
    /// </summary>
    [Fact]
    public void Repeated_exact_version_returns_the_same_profile_reference()
    {
        // Arrange
        var version = new Version(8, 4, 101);

        // Act
        var first = EngineProfileTable.Resolve(EngineFamily.MySql, version);
        var second = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 101));

        // Assert
        Assert.Same(first, second);
        Assert.Equal(version, first.Version);
    }

    /// <summary>
    /// Preserves value identity after eviction so equivalent server versions keep
    /// using the same EF Core internal service-provider cache key.
    /// </summary>
    [Fact]
    public void Evicted_exact_version_preserves_structural_profile_identity()
    {
        // Arrange
        var version = new Version(8, 4, 102);
        var first = EngineProfileTable.Resolve(EngineFamily.MySql, version);

        // Act
        for (var versionIndex = 0; versionIndex <= EngineProfileTable.Capacity; versionIndex++)
        {
            _ = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(3000 + versionIndex, 0));
        }

        var second = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 102));

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>
    /// Reproduces high-cardinality version input and proves the process-wide cache
    /// retains no more than its explicit resource budget.
    /// </summary>
    [Fact]
    public void High_cardinality_version_input_stays_within_cache_capacity()
    {
        // Arrange
        var versions = Enumerable.Range(0, EngineProfileTable.Capacity * 4)
            .Select(versionIndex => new Version(1000 + versionIndex, 0))
            .ToArray();

        // Act
        var profiles = versions.Select(version => EngineProfileTable.Resolve(EngineFamily.MySql, version)).ToArray();

        // Assert
        Assert.All(versions.Zip(profiles), pair => Assert.Equal(pair.First, pair.Second.Version));
        Assert.InRange(EngineProfileTable.Count, 1, EngineProfileTable.Capacity);
    }

    /// <summary>
    /// Proves concurrent cache admission cannot escape the same hard capacity.
    /// </summary>
    [Fact]
    public void Concurrent_high_cardinality_version_input_stays_within_cache_capacity()
    {
        // Arrange
        var versions = Enumerable.Range(0, EngineProfileTable.Capacity * 8)
            .Select(versionIndex => new Version(2000 + versionIndex, 0))
            .ToArray();

        var profiles = new EngineProfile[versions.Length];

        // Act
        Parallel.For(
            0,
            versions.Length,
            versionIndex =>
            {
                profiles[versionIndex] = EngineProfileTable.Resolve(EngineFamily.MariaDb, versions[versionIndex]);
            });

        // Assert
        Assert.All(versions.Zip(profiles), pair => Assert.Equal(pair.First, pair.Second.Version));
        Assert.InRange(EngineProfileTable.Count, 1, EngineProfileTable.Capacity);
    }

    /// <summary>
    /// Ensures each engine capability becomes active at its documented release boundary and not before it.
    /// </summary>
    /// <param name="family">The engine family name.</param>
    /// <param name="version">The exact server version at the boundary.</param>
    /// <param name="capability">The capability name to resolve.</param>
    /// <param name="expected">Whether that exact version must advertise the capability.</param>
    [Theory]
    [MemberData(nameof(CapabilityBoundaryCases))]
    public void Versioned_capabilities_follow_their_documented_boundaries(
        string family,
        string version,
        string capability,
        bool expected
    )
    {
        // Arrange
        var engineFamily = Enum.Parse<EngineFamily>(family);
        var engineVersion = Version.Parse(version);
        var engineCapability = Enum.Parse<EngineCapability>(capability);

        // Act
        var profile = EngineProfileTable.Resolve(engineFamily, engineVersion);

        // Assert
        Assert.Equal(expected, profile.Has(engineCapability));
    }

    /// <summary>
    /// Ensures an undefined engine family cannot inherit another family's
    /// capability set.
    /// </summary>
    [Fact]
    public void Undefined_engine_family_fails_closed()
    {
        // Arrange
        var family = (EngineFamily)int.MaxValue;

        // Act
        var exception = Record.Exception(() => EngineProfileTable.Resolve(family, new Version(1, 0)));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }
}
