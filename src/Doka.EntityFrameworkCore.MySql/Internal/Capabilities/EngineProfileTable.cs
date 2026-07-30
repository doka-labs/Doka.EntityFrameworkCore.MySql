namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Static lookup table mapping an engine family and version to a frozen engine-fact
/// snapshot. Provider support is derived separately by <see cref="ProviderProfile"/>.
///
/// Adding support for a new engine version means declaring the exact threshold
/// that changes an engine fact. The table accumulates every fact whose threshold
/// is satisfied by the requested version.
/// </summary>
internal static class EngineProfileTable
{
    private static readonly Version s_mySql576 = new(5, 7, 6);
    private static readonly Version s_mySql578 = new(5, 7, 8);
    private static readonly Version s_mySql803 = new(8, 0, 3);
    private static readonly Version s_mySql804 = new(8, 0, 4);
    private static readonly Version s_mySql8013 = new(8, 0, 13);
    private static readonly Version s_mySql8014 = new(8, 0, 14);
    private static readonly Version s_mariaDb52 = new(5, 2, 0);
    private static readonly Version s_mariaDb1021 = new(10, 2, 1);
    private static readonly Version s_mariaDb1023 = new(10, 2, 3);
    private static readonly Version s_mariaDb103 = new(10, 3, 0);
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
        var builder = new HashSet<EngineCapability>
        {
            EngineCapability.Savepoints,
        };

        switch (family)
        {
            case EngineFamily.MySql:
                AccumulateMySql(version, builder);
                break;
            case EngineFamily.MariaDb:
                AccumulateMariaDb(version, builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }

        return new EngineProfile(family, version, builder.ToFrozenSet());
    }

    private static void AccumulateMySql(
        Version version,
        HashSet<EngineCapability> capabilities
    )
    {
        if (IsAtLeast(version, s_mySql576))
        {
            capabilities.Add(EngineCapability.VirtualGeneratedColumns);
            capabilities.Add(EngineCapability.StoredGeneratedColumns);
            capabilities.Add(EngineCapability.GeneratedColumnNullabilityClause);
        }

        if (IsAtLeast(version, s_mySql578))
        {
            capabilities.Add(EngineCapability.NativeJsonType);
        }

        if (IsAtLeast(version, s_mySql803))
        {
            capabilities.Add(EngineCapability.RenameColumnSyntax);
            capabilities.Add(EngineCapability.SpatialColumnSridAttribute);
        }

        if (IsAtLeast(version, s_mySql804))
        {
            capabilities.Add(EngineCapability.RegexpLikeFunction);
            capabilities.Add(EngineCapability.JsonTableExistsRequiresWorkaround);
        }

        if (IsAtLeast(version, s_mySql8013))
        {
            capabilities.Add(EngineCapability.FunctionalIndexExpressionMetadata);
        }

        if (IsAtLeast(version, s_mySql8014))
        {
            capabilities.Add(EngineCapability.LateralDerivedTables);
        }

        capabilities.Add(EngineCapability.SelfReferencingMutationRequiresIsolation);
    }

    private static void AccumulateMariaDb(
        Version version,
        HashSet<EngineCapability> capabilities
    )
    {
        capabilities.Add(EngineCapability.MariaDbSpatialSemantics);
        capabilities.Add(EngineCapability.CheckConstraintCatalogIncludesTableName);

        if (IsAtLeast(version, s_mariaDb52))
        {
            capabilities.Add(EngineCapability.VirtualGeneratedColumns);
            capabilities.Add(EngineCapability.StoredGeneratedColumns);
        }

        if (IsAtLeast(version, s_mariaDb52)
            && !IsAtLeast(version, s_mariaDb1021))
        {
            capabilities.Add(EngineCapability.StoredGeneratedColumnUsesPersistentKeyword);
        }

        if (IsAtLeast(version, s_mariaDb1023))
        {
            // The provider can preserve JSON column semantics with LONGTEXT and
            // a JSON_VALID check as soon as MariaDB exposes the validator.
            capabilities.Add(EngineCapability.JsonValidationFunction);
        }

        if (IsAtLeast(version, s_mariaDb103))
        {
            capabilities.Add(EngineCapability.NativeSequences);
        }

        if (IsAtLeast(version, s_mariaDb105))
        {
            capabilities.Add(EngineCapability.ReturningClause);
        }

        if (IsAtLeast(version, s_mariaDb1052))
        {
            capabilities.Add(EngineCapability.RenameColumnSyntax);
        }
    }

    private static bool IsAtLeast(
        Version version,
        Version minimumVersion
    ) => version.CompareTo(minimumVersion) >= 0;
}
