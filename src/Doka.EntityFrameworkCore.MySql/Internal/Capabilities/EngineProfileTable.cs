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
    /// <summary>
    /// Maximum number of exact engine versions retained by the process-wide
    /// reference cache. Capability construction remains available after eviction.
    /// </summary>
    internal const int Capacity = 128;

    private static readonly Version s_mySql57 = new(5, 7, 0);
    private static readonly Version s_mySql571 = new(5, 7, 1);
    private static readonly Version s_mySql576 = new(5, 7, 6);
    private static readonly Version s_mySql578 = new(5, 7, 8);
    private static readonly Version s_mySql800 = new(8, 0, 0);
    private static readonly Version s_mySql801 = new(8, 0, 1);
    private static readonly Version s_mySql803 = new(8, 0, 3);
    private static readonly Version s_mySql804 = new(8, 0, 4);
    private static readonly Version s_mySql8013 = new(8, 0, 13);
    private static readonly Version s_mySql8014 = new(8, 0, 14);
    private static readonly Version s_mySql8016 = new(8, 0, 16);
    private static readonly Version s_mariaDb52 = new(5, 2, 0);
    private static readonly Version s_mariaDb1021 = new(10, 2, 1);
    private static readonly Version s_mariaDb1022 = new(10, 2, 2);
    private static readonly Version s_mariaDb1023 = new(10, 2, 3);
    private static readonly Version s_mariaDb103 = new(10, 3, 0);
    private static readonly Version s_mariaDb1034 = new(10, 3, 4);
    private static readonly Version s_mariaDb1043 = new(10, 4, 3);
    private static readonly Version s_mariaDb105 = new(10, 5, 0);
    private static readonly Version s_mariaDb1052 = new(10, 5, 2);
    private static readonly Version s_mariaDb1053 = new(10, 5, 3);
    private static readonly Version s_mariaDb1061 = new(10, 6, 1);
    private static readonly Version s_mariaDb1062 = new(10, 6, 2);
    private static readonly Version s_mariaDb108 = new(10, 8, 0);
    private static readonly Version s_mariaDb114 = new(11, 4, 0);

    // EF Core's options graph requires stable profile references for repeated
    // versions. Resolution is configuration-time work, so one short critical
    // section provides a strict bound for both the entries and FIFO ownership
    // metadata without putting a lock in query execution.
    private static readonly object s_cacheLock = new();
    private static readonly Dictionary<(EngineFamily Family, Version Version), EngineProfile> s_cache = [];
    private static readonly Queue<(EngineFamily Family, Version Version)> s_insertionOrder = [];

    /// <summary>
    /// Returns the currently retained exact-version profile count for resource
    /// invariant tests.
    /// </summary>
    internal static int Count
    {
        get
        {
            lock (s_cacheLock)
            {
                return s_cache.Count;
            }
        }
    }

    /// <summary>
    /// Resolves an <see cref="EngineProfile"/> for the supplied engine family and
    /// version. Recent instances are cached per (family, version) so repeated
    /// resolution returns the same reference and downstream service-provider caches
    /// stay warm without permitting unbounded process retention.
    /// </summary>
    public static EngineProfile Resolve(
        EngineFamily family,
        Version version
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        var key = (family, version);

        lock (s_cacheLock)
        {
            if (s_cache.TryGetValue(key, out var cachedProfile))
            {
                return cachedProfile;
            }

            var profile = Build(family, version);

            if (s_cache.Count == Capacity)
            {
                var oldestKey = s_insertionOrder.Dequeue();
                _ = s_cache.Remove(oldestKey);
            }

            s_cache.Add(key, profile);
            s_insertionOrder.Enqueue(key);
            return profile;
        }
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
        if (IsAtLeast(version, s_mySql57))
        {
            capabilities.Add(EngineCapability.IndexPrefixLengths);
            capabilities.Add(EngineCapability.PreparedDdl);
        }

        if (IsAtLeast(version, s_mySql571))
        {
            capabilities.Add(EngineCapability.RenameIndex);
        }

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

        if (IsAtLeast(version, s_mySql800))
        {
            capabilities.Add(EngineCapability.AtomicDdl);
        }

        if (IsAtLeast(version, s_mySql801))
        {
            capabilities.Add(EngineCapability.CommonTableExpressions);
            capabilities.Add(EngineCapability.DescendingIndexes);
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
            capabilities.Add(EngineCapability.FunctionalIndexes);
            capabilities.Add(EngineCapability.ExpressionDefaults);
        }

        if (IsAtLeast(version, s_mySql8014))
        {
            capabilities.Add(EngineCapability.LateralDerivedTables);
        }

        if (IsAtLeast(version, s_mySql8016))
        {
            capabilities.Add(EngineCapability.CheckConstraints);
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
            capabilities.Add(EngineCapability.IndexPrefixLengths);
        }

        if (IsAtLeast(version, s_mariaDb1021))
        {
            capabilities.Add(EngineCapability.CheckConstraints);
            capabilities.Add(EngineCapability.ExpressionDefaults);
        }

        if (IsAtLeast(version, s_mariaDb52)
            && !IsAtLeast(version, s_mariaDb1021))
        {
            capabilities.Add(EngineCapability.StoredGeneratedColumnUsesPersistentKeyword);
        }

        if (IsAtLeast(version, s_mariaDb1022))
        {
            capabilities.Add(EngineCapability.CommonTableExpressions);
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

        if (IsAtLeast(version, s_mariaDb1034))
        {
            capabilities.Add(EngineCapability.SystemVersionedTables);
        }

        if (IsAtLeast(version, s_mariaDb1043))
        {
            capabilities.Add(EngineCapability.ApplicationTimePeriods);
        }

        if (IsAtLeast(version, s_mariaDb105))
        {
            capabilities.Add(EngineCapability.ReturningClause);
        }

        if (IsAtLeast(version, s_mariaDb1052))
        {
            capabilities.Add(EngineCapability.RenameColumnSyntax);
            capabilities.Add(EngineCapability.RenameIndex);
        }

        if (IsAtLeast(version, s_mariaDb1053))
        {
            capabilities.Add(EngineCapability.ApplicationTimeWithoutOverlaps);
        }

        if (IsAtLeast(version, s_mariaDb1061))
        {
            capabilities.Add(EngineCapability.AtomicDdl);
        }

        if (IsAtLeast(version, s_mariaDb1062))
        {
            capabilities.Add(EngineCapability.PreparedDdl);
        }

        if (IsAtLeast(version, s_mariaDb108))
        {
            capabilities.Add(EngineCapability.DescendingIndexes);
        }

        if (IsAtLeast(version, s_mariaDb114))
        {
            capabilities.Add(EngineCapability.TemporalPeriodCatalog);
        }
    }

    private static bool IsAtLeast(
        Version version,
        Version minimumVersion
    ) => version.CompareTo(minimumVersion) >= 0;
}
