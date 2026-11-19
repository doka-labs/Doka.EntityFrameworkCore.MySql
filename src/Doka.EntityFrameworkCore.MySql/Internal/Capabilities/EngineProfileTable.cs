namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Static lookup table mapping an engine family and version to a frozen engine-fact
/// snapshot. Provider support is derived separately by <see cref="ProviderProfile" />.
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
    private static readonly Version s_mySql577 = new(5, 7, 7);
    private static readonly Version s_mySql578 = new(5, 7, 8);
    private static readonly Version s_mySql800 = new(8, 0, 0);
    private static readonly Version s_mySql801 = new(8, 0, 1);
    private static readonly Version s_mySql802 = new(8, 0, 2);
    private static readonly Version s_mySql803 = new(8, 0, 3);
    private static readonly Version s_mySql804 = new(8, 0, 4);
    private static readonly Version s_mySql8013 = new(8, 0, 13);
    private static readonly Version s_mySql8014 = new(8, 0, 14);
    private static readonly Version s_mySql8016 = new(8, 0, 16);
    private static readonly Version s_mySql8019 = new(8, 0, 19);
    private static readonly Version s_mySql8024 = new(8, 0, 24);
    private static readonly Version s_mariaDb52 = new(5, 2, 0);
    private static readonly Version s_mariaDb102 = new(10, 2, 0);
    private static readonly Version s_mariaDb1021 = new(10, 2, 1);
    private static readonly Version s_mariaDb1022 = new(10, 2, 2);
    private static readonly Version s_mariaDb1023 = new(10, 2, 3);
    private static readonly Version s_mariaDb103 = new(10, 3, 0);
    private static readonly Version s_mariaDb1034 = new(10, 3, 4);
    private static readonly Version s_mariaDb1043 = new(10, 4, 3);
    private static readonly Version s_mariaDb105 = new(10, 5, 0);
    private static readonly Version s_mariaDb1052 = new(10, 5, 2);
    private static readonly Version s_mariaDb1053 = new(10, 5, 3);
    private static readonly Version s_mariaDb106 = new(10, 6, 0);
    private static readonly Version s_mariaDb1061 = new(10, 6, 1);
    private static readonly Version s_mariaDb1062 = new(10, 6, 2);
    private static readonly Version s_mariaDb107 = new(10, 7, 0);
    private static readonly Version s_mariaDb108 = new(10, 8, 0);
    private static readonly Version s_mariaDb10101 = new(10, 10, 1);
    private static readonly Version s_mariaDb114 = new(11, 4, 0);
    private static readonly Version s_mariaDb120 = new(12, 0, 0);
    private static readonly Version s_mariaDb1231 = new(12, 3, 1);
    private static readonly Version s_mariaDb1233 = new(12, 3, 3);
    private static readonly Version s_mariaDb1302 = new(13, 0, 2);

    // EF Core's options graph requires stable profile references for repeated
    // versions. Resolution is configuration-time work, so one short critical
    // section provides a strict bound for both the entries and FIFO ownership
    // metadata without putting a lock in query execution.
    private static readonly Lock s_cacheLock = new();
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
    /// Resolves an <see cref="EngineProfile" /> for the supplied engine family and
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
            capabilities.Add(EngineCapability.SpatialIsValidFunction);
        }

        if (IsAtLeast(version, s_mySql577))
        {
            capabilities.Add(EngineCapability.SpatialBufferStrategies);
        }

        if (IsAtLeast(version, s_mySql578))
        {
            capabilities.Add(EngineCapability.NativeJsonType);
            capabilities.Add(EngineCapability.JsonContainsComparesDecodedStrings);
        }

        if (IsAtLeast(version, s_mySql800))
        {
            capabilities.Add(EngineCapability.AtomicDdl);
        }

        if (IsAtLeast(version, s_mySql801))
        {
            capabilities.Add(EngineCapability.CommonTableExpressions);
            capabilities.Add(EngineCapability.CteUpdateDelete);
            capabilities.Add(EngineCapability.DescendingIndexes);
            capabilities.Add(EngineCapability.SpatialWktAxisOrderOptions);

            // WHY: Runtime behavior consumes syntax facts, not family identity. MySQL added
            // these locking clauses together in 8.0.1, independently of provider support policy.
            // Source: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-1.html
            capabilities.Add(EngineCapability.ForShareClause);
            capabilities.Add(EngineCapability.ForUpdateNoWaitClause);
            capabilities.Add(EngineCapability.ForUpdateSkipLockedClause);
        }

        if (IsAtLeast(version, s_mySql802))
        {
            // WHY: The original 8.0.2 window grammar already accepts the third LAG/LEAD
            // argument and parameter markers in both ROWS and RANGE frame bounds.
            // Source: https://github.com/mysql/mysql-server/blob/mysql-8.0.2/sql/sql_yacc.yy
            capabilities.Add(EngineCapability.WindowFunctions);
            capabilities.Add(EngineCapability.WindowNavigationDefaultArgument);
            capabilities.Add(EngineCapability.WindowFrameParameterOffsets);
        }

        if (IsAtLeast(version, s_mySql803))
        {
            capabilities.Add(EngineCapability.RenameColumnSyntax);
            capabilities.Add(EngineCapability.SpatialColumnSridAttribute);

            // WHY: CTE syntax arrived in 8.0.1, but its native ceiling variable
            // was added separately in 8.0.3. Bounded recursion must check both facts.
            // Source: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-3.html
            capabilities.Add(EngineCapability.RecursiveCteLimitUsesCteMaxRecursionDepth);
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

        if (IsAtLeast(version, s_mySql8019))
        {
            capabilities.Add(EngineCapability.InsertRowAlias);
        }

        if (IsAtLeast(version, s_mySql8024))
        {
            capabilities.Add(EngineCapability.SpatialCollectAggregate);
        }

        capabilities.Add(EngineCapability.SelfReferencingMutationRequiresIsolation);
    }

    private static void AccumulateMariaDb(
        Version version,
        HashSet<EngineCapability> capabilities
    )
    {
        capabilities.Add(EngineCapability.MariaDbSpatialSemantics);
        capabilities.Add(EngineCapability.ColumnCommentPrecedesVisibilityAttribute);
        capabilities.Add(EngineCapability.CheckConstraintCatalogIncludesTableName);
        capabilities.Add(EngineCapability.SharedLockingReadUsesLockInShareMode);

        // WHY: This is the selected MariaDB incoming-row dialect. MySQL uses the
        // row-alias capability instead, without falling back to deprecated VALUES.
        capabilities.Add(EngineCapability.OnDuplicateKeyValuesFunction);

        if (IsAtLeast(version, s_mariaDb52))
        {
            capabilities.Add(EngineCapability.VirtualGeneratedColumns);
            capabilities.Add(EngineCapability.StoredGeneratedColumns);
            capabilities.Add(EngineCapability.IndexPrefixLengths);
        }

        if (IsAtLeast(version, s_mariaDb102))
        {
            // WHY: The qualified MariaDB LAG_SYM/LEAD_SYM productions omit defaults;
            // window_frame_start/window_frame_bound accept literals instead of parameter markers.
            // Keep both optional capabilities unset until that syntax is qualified.
            // Source: https://github.com/MariaDB/server/blob/mariadb-12.3.3/sql/sql_yacc.yy
            capabilities.Add(EngineCapability.WindowFunctions);
        }

        if (IsAtLeast(version, s_mariaDb1021))
        {
            capabilities.Add(EngineCapability.CheckConstraints);
            capabilities.Add(EngineCapability.ExpressionDefaults);
            capabilities.Add(EngineCapability.SpatialSridCheckConstraint);
        }

        if (IsAtLeast(version, s_mariaDb52)
            && !IsAtLeast(version, s_mariaDb1021))
        {
            capabilities.Add(EngineCapability.StoredGeneratedColumnUsesPersistentKeyword);
        }

        if (IsAtLeast(version, s_mariaDb1022))
        {
            capabilities.Add(EngineCapability.CommonTableExpressions);
            capabilities.Add(EngineCapability.RecursiveCteLimitUsesMaxRecursiveIterations);
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
            capabilities.Add(EngineCapability.ForUpdateNoWaitClause);
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

        if (IsAtLeast(version, s_mariaDb106))
        {
            // WHY: SKIP LOCKED predates the separate 10.6.1 atomic-DDL boundary.
            // Source: https://mariadb.com/docs/release-notes/community-server/10.6/10.6.0
            capabilities.Add(EngineCapability.ForUpdateSkipLockedClause);
        }

        if (IsAtLeast(version, s_mariaDb1061))
        {
            capabilities.Add(EngineCapability.AtomicDdl);
        }

        if (IsAtLeast(version, s_mariaDb1062))
        {
            capabilities.Add(EngineCapability.PreparedDdl);
        }

        if (IsAtLeast(version, s_mariaDb107))
        {
            capabilities.Add(EngineCapability.NativeUuidType);
        }

        if (IsAtLeast(version, s_mariaDb108))
        {
            capabilities.Add(EngineCapability.DescendingIndexes);
        }

        if (IsAtLeast(version, s_mariaDb10101))
        {
            capabilities.Add(EngineCapability.SharedUca1400Collations);
        }

        if (IsAtLeast(version, s_mariaDb114))
        {
            capabilities.Add(EngineCapability.TemporalPeriodCatalog);
        }

        if (IsAtLeast(version, s_mariaDb120))
        {
            capabilities.Add(EngineCapability.SpatialIsValidFunction);
            capabilities.Add(EngineCapability.SpatialCollectAggregate);
        }

        if (IsAtLeast(version, s_mariaDb1231))
        {
            // WHY: The manual's 12.3 series shorthand first shipped in 12.3.1.
            // Source: https://mariadb.com/docs/release-notes/community-server/12.3/12.3.1
            capabilities.Add(EngineCapability.CteUpdateDelete);
        }

        // WHY: MDEV-39995 was fixed separately in 12.3.3 and 13.0.2; a single
        // monotone threshold would incorrectly enable the native path for 13.0.0/1.
        if ((version.Major == 12 && IsAtLeast(version, s_mariaDb1233))
            || (version.Major == 13 && IsAtLeast(version, s_mariaDb1302))
            || version.Major > 13)
        {
            capabilities.Add(EngineCapability.JsonContainsComparesDecodedStrings);
        }
    }

    private static bool IsAtLeast(
        Version version,
        Version minimumVersion
    ) => version.CompareTo(minimumVersion) >= 0;
}
