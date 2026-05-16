namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Static lookup table mapping (engine-family, version) to a frozen capability
/// snapshot. Replaces the per-call <c>ServerCapabilities.Create(isMariaDb, version)</c>
/// computation with a deterministic build-once / read-many table that scales
/// linearly with the number of supported engine versions rather than quadratically
/// with the number of capabilities times the number of version thresholds.
///
/// Adding support for a new engine version becomes a single entry: list the
/// minimum version that introduces the new capabilities, and the table propagates
/// the feature set to every <see cref="EngineProfile"/> resolved at or above that
/// version via the lower-bound search in <see cref="Resolve"/>.
/// </summary>
internal static class EngineProfileTable
{
    private static readonly Version s_mySql57 = new(5, 7, 0);
    private static readonly Version s_mySql8 = new(8, 0, 0);
    private static readonly Version s_mySql8031 = new(8, 0, 31);
    private static readonly Version s_mariaDb102 = new(10, 2, 0);
    private static readonly Version s_mariaDb103 = new(10, 3, 0);
    private static readonly Version s_mariaDb1034 = new(10, 3, 4);
    private static readonly Version s_mariaDb105 = new(10, 5, 0);
    private static readonly Version s_mariaDb1052 = new(10, 5, 2);

    // Per-(family, version) instance cache. EF Core caches its internal service
    // provider per options-graph; the cache hit requires identical references to
    // every option-extension property, and EngineProfile's FrozenSet defaults to
    // reference equality. Without the cache here every fresh MySqlServerVersion()
    // produces a fresh profile, every fresh profile invalidates EF Core's service-
    // provider cache, and the "more than twenty service providers" warning escalates
    // to an error in unit tests that build many DbContexts per run.
    private static readonly ConcurrentDictionary<(EngineFamily Family, Version Version), EngineProfile> s_cache = new();

    /// <summary>
    /// Resolves an <see cref="EngineProfile"/> for the supplied engine family and
    /// version. The resulting instance is cached per (family, version) so repeated
    /// resolution returns the same reference and downstream service-provider caches
    /// stay warm.
    /// </summary>
    public static EngineProfile Resolve(
        EngineFamily family,
        Version version
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        return s_cache.GetOrAdd((family, version), static key => Build(key.Family, key.Version));
    }

    private static EngineProfile Build(
        EngineFamily family,
        Version version
    )
    {
        var builder = new HashSet<Capability>
        {
            // Baseline capabilities every supported engine version advertises.
            // Kept as explicit entries rather than implicit so a future engine that
            // drops one of them surfaces as an explicit profile change rather than a
            // silent global assumption.
            Capability.SupportsDateTime6,
            Capability.SupportsSavepoints,
            Capability.SupportsFullTextIndex,
        };

        if (family == EngineFamily.MariaDb)
        {
            AccumulateMariaDb(version, builder);
        }
        else
        {
            AccumulateMySql(version, builder);
        }

        return new EngineProfile(family, version, builder.ToFrozenSet());
    }

    private static void AccumulateMySql(
        Version version,
        HashSet<Capability> capabilities
    )
    {
        if (IsAtLeast(version, s_mySql57))
        {
            capabilities.Add(Capability.SupportsNativeJsonType);
            capabilities.Add(Capability.SupportsVirtualGeneratedColumns);
            capabilities.Add(Capability.SupportsStoredGeneratedColumns);
        }

        if (IsAtLeast(version, s_mySql8))
        {
            capabilities.Add(Capability.SupportsCommonTableExpressions);
            capabilities.Add(Capability.SupportsWindowFunctions);
            capabilities.Add(Capability.SupportsGeneratedInvisiblePrimaryKeys);
            capabilities.Add(Capability.SupportsRenameColumnSyntax);
        }

        if (IsAtLeast(version, s_mySql8031))
        {
            capabilities.Add(Capability.SupportsIntersectExcept);
        }

        // MySQL-only invariants (no version gate): nullability clause on generated
        // columns + spatial-column SRID attribute have shipped since the earliest
        // version this provider targets.
        capabilities.Add(Capability.SupportsGeneratedColumnNullabilityClause);
        capabilities.Add(Capability.SupportsSpatialColumnSridAttribute);
    }

    private static void AccumulateMariaDb(
        Version version,
        HashSet<Capability> capabilities
    )
    {
        // MariaDB stores JSON as LONGTEXT with utf8mb4_bin and a JSON_VALID CHECK;
        // the provider routes JSON columns through that alias regardless of version.
        capabilities.Add(Capability.UsesJsonAliasForJsonColumns);

        if (IsAtLeast(version, s_mariaDb102))
        {
            capabilities.Add(Capability.SupportsCommonTableExpressions);
            capabilities.Add(Capability.SupportsWindowFunctions);
            capabilities.Add(Capability.SupportsVirtualGeneratedColumns);
            capabilities.Add(Capability.SupportsStoredGeneratedColumns);
        }

        if (IsAtLeast(version, s_mariaDb103))
        {
            capabilities.Add(Capability.SupportsNativeSequences);
            capabilities.Add(Capability.SupportsIntersectExcept);
        }

        if (IsAtLeast(version, s_mariaDb1034))
        {
            capabilities.Add(Capability.SupportsSystemVersioning);
        }

        if (IsAtLeast(version, s_mariaDb105))
        {
            capabilities.Add(Capability.SupportsReturningClause);
        }

        if (IsAtLeast(version, s_mariaDb1052))
        {
            capabilities.Add(Capability.SupportsRenameColumnSyntax);
        }
    }

    private static bool IsAtLeast(
        Version version,
        Version minimumVersion
    ) => version.CompareTo(minimumVersion) >= 0;
}
