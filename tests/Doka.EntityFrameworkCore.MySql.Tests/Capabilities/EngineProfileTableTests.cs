namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies every version boundary that changes an engine capability consumed
/// by provider behavior.
/// </summary>
public sealed class EngineProfileTableTests
{
    /// <summary>
    /// Preserves stable profile references for repeated exact versions so the
    /// bounded cache still supports EF Core service-provider reuse.
    /// </summary>
    [Fact]
    public void Repeated_exact_version_returns_the_same_profile_reference()
    {
        var version = new Version(8, 4, 101);

        var first = EngineProfileTable.Resolve(EngineFamily.MySql, version);
        var second = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 101));

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
        var version = new Version(8, 4, 102);
        var first = EngineProfileTable.Resolve(EngineFamily.MySql, version);

        for (var versionIndex = 0; versionIndex <= EngineProfileTable.Capacity; versionIndex++)
        {
            _ = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(3000 + versionIndex, 0));
        }

        var second = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4, 102));

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
        for (var versionIndex = 0; versionIndex < EngineProfileTable.Capacity * 4; versionIndex++)
        {
            var version = new Version(1000 + versionIndex, 0);
            var profile = EngineProfileTable.Resolve(EngineFamily.MySql, version);

            Assert.Equal(version, profile.Version);
        }

        Assert.InRange(EngineProfileTable.Count, 1, EngineProfileTable.Capacity);
    }

    /// <summary>
    /// Proves concurrent cache admission cannot escape the same hard capacity.
    /// </summary>
    [Fact]
    public void Concurrent_high_cardinality_version_input_stays_within_cache_capacity()
    {
        Parallel.For(
            0,
            EngineProfileTable.Capacity * 8,
            versionIndex =>
            {
                var version = new Version(2000 + versionIndex, 0);
                var profile = EngineProfileTable.Resolve(EngineFamily.MariaDb, version);

                Assert.Equal(version, profile.Version);
            });

        Assert.InRange(EngineProfileTable.Count, 1, EngineProfileTable.Capacity);
    }

    /// <summary>
    /// Ensures engine capabilities become active at their documented release
    /// boundary and not before it.
    /// </summary>
    [Fact]
    public void Versioned_capabilities_follow_their_documented_boundaries()
    {
        var cases = new (EngineFamily Family, string Version, EngineCapability Capability, bool Expected)[]
        {
            (EngineFamily.MySql, "5.7.5", EngineCapability.VirtualGeneratedColumns, false),
            (EngineFamily.MySql, "5.7.6", EngineCapability.VirtualGeneratedColumns, true),
            (EngineFamily.MySql, "5.7.5", EngineCapability.StoredGeneratedColumns, false),
            (EngineFamily.MySql, "5.7.6", EngineCapability.StoredGeneratedColumns, true),
            (EngineFamily.MySql, "5.7.5", EngineCapability.GeneratedColumnNullabilityClause, false),
            (EngineFamily.MySql, "5.7.6", EngineCapability.GeneratedColumnNullabilityClause, true),
            (EngineFamily.MySql, "5.7.7", EngineCapability.NativeJsonType, false),
            (EngineFamily.MySql, "5.7.8", EngineCapability.NativeJsonType, true),
            (EngineFamily.MySql, "8.0.0", EngineCapability.CommonTableExpressions, false),
            (EngineFamily.MySql, "8.0.1", EngineCapability.CommonTableExpressions, true),
            (EngineFamily.MySql, "8.0.2", EngineCapability.RenameColumnSyntax, false),
            (EngineFamily.MySql, "8.0.3", EngineCapability.RenameColumnSyntax, true),
            (EngineFamily.MySql, "8.0.2", EngineCapability.SpatialColumnSridAttribute, false),
            (EngineFamily.MySql, "8.0.3", EngineCapability.SpatialColumnSridAttribute, true),
            (EngineFamily.MySql, "8.0.3", EngineCapability.RegexpLikeFunction, false),
            (EngineFamily.MySql, "8.0.4", EngineCapability.RegexpLikeFunction, true),
            (EngineFamily.MySql, "8.0.3", EngineCapability.JsonTableExistsRequiresWorkaround, false),
            (EngineFamily.MySql, "8.0.4", EngineCapability.JsonTableExistsRequiresWorkaround, true),
            (EngineFamily.MySql, "8.0.12", EngineCapability.FunctionalIndexExpressionMetadata, false),
            (EngineFamily.MySql, "8.0.13", EngineCapability.FunctionalIndexExpressionMetadata, true),
            (EngineFamily.MySql, "8.0.13", EngineCapability.LateralDerivedTables, false),
            (EngineFamily.MySql, "8.0.14", EngineCapability.LateralDerivedTables, true),
            (EngineFamily.MariaDb, "5.1.99", EngineCapability.VirtualGeneratedColumns, false),
            (EngineFamily.MariaDb, "5.2.0", EngineCapability.VirtualGeneratedColumns, true),
            (EngineFamily.MariaDb, "5.1.99", EngineCapability.StoredGeneratedColumns, false),
            (EngineFamily.MariaDb, "5.2.0", EngineCapability.StoredGeneratedColumns, true),
            (EngineFamily.MariaDb, "5.1.99", EngineCapability.StoredGeneratedColumnUsesPersistentKeyword, false),
            (EngineFamily.MariaDb, "5.2.0", EngineCapability.StoredGeneratedColumnUsesPersistentKeyword, true),
            (EngineFamily.MariaDb, "10.2.0", EngineCapability.StoredGeneratedColumnUsesPersistentKeyword, true),
            (EngineFamily.MariaDb, "10.2.1", EngineCapability.StoredGeneratedColumnUsesPersistentKeyword, false),
            (EngineFamily.MariaDb, "10.2.1", EngineCapability.CommonTableExpressions, false),
            (EngineFamily.MariaDb, "10.2.2", EngineCapability.CommonTableExpressions, true),
            (EngineFamily.MariaDb, "10.2.2", EngineCapability.JsonValidationFunction, false),
            (EngineFamily.MariaDb, "10.2.3", EngineCapability.JsonValidationFunction, true),
            (EngineFamily.MariaDb, "10.2.99", EngineCapability.NativeSequences, false),
            (EngineFamily.MariaDb, "10.3.0", EngineCapability.NativeSequences, true),
            (EngineFamily.MariaDb, "10.3.3", EngineCapability.SystemVersionedTables, false),
            (EngineFamily.MariaDb, "10.3.4", EngineCapability.SystemVersionedTables, true),
            (EngineFamily.MariaDb, "10.4.99", EngineCapability.ReturningClause, false),
            (EngineFamily.MariaDb, "10.5.0", EngineCapability.ReturningClause, true),
            (EngineFamily.MariaDb, "10.5.1", EngineCapability.RenameColumnSyntax, false),
            (EngineFamily.MariaDb, "10.5.2", EngineCapability.RenameColumnSyntax, true),
        };

        foreach (var testCase in cases)
        {
            var profile = EngineProfileTable.Resolve(testCase.Family, Version.Parse(testCase.Version));

            Assert.Equal(testCase.Expected, profile.Has(testCase.Capability));
        }
    }

    /// <summary>
    /// Ensures an undefined engine family cannot inherit another family's
    /// capability set.
    /// </summary>
    [Fact]
    public void Undefined_engine_family_fails_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EngineProfileTable.Resolve(
            (EngineFamily)int.MaxValue,
            new Version(1, 0)));
    }
}
