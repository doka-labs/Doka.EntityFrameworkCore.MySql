namespace Doka.EntityFrameworkCore.MySql;

internal sealed record ServerCapabilities(
    bool SupportsCommonTableExpressions,
    bool SupportsWindowFunctions,
    bool SupportsNativeJsonType,
    bool UsesJsonAliasForJsonColumns,
    bool SupportsReturningClause,
    bool SupportsDateTime6,
    bool SupportsGeneratedInvisiblePrimaryKeys,
    bool SupportsSavepoints,
    bool SupportsGeneratedColumnNullabilityClause,
    bool SupportsVirtualGeneratedColumns,
    bool SupportsStoredGeneratedColumns,
    bool SupportsSpatialColumnSridAttribute,
    bool SupportsNativeSequences,
    bool SupportsIntersectExcept,
    bool SupportsSystemVersioning,
    bool SupportsFullTextIndex
)
{
    private static readonly Version s_mySql8 = new(8, 0, 0);
    private static readonly Version s_mySql8031 = new(8, 0, 31);
    private static readonly Version s_mariaDb102 = new(10, 2, 0);
    private static readonly Version s_mariaDb103 = new(10, 3, 0);
    private static readonly Version s_mariaDb1034 = new(10, 3, 4);
    private static readonly Version s_mariaDb105 = new(10, 5, 0);
    private static readonly Version s_mySql57 = new(5, 7, 0);

    public static ServerCapabilities Create(
        bool isMariaDb,
        Version version
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        return new ServerCapabilities(
            SupportsCommonTableExpressions: isMariaDb ? IsAtLeast(version, s_mariaDb102) : IsAtLeast(version, s_mySql8),
            SupportsWindowFunctions: isMariaDb ? IsAtLeast(version, s_mariaDb102) : IsAtLeast(version, s_mySql8),
            SupportsNativeJsonType: !isMariaDb && IsAtLeast(version, s_mySql57),
            UsesJsonAliasForJsonColumns: isMariaDb,
            SupportsReturningClause: isMariaDb && IsAtLeast(version, s_mariaDb105),
            SupportsDateTime6: true,
            SupportsGeneratedInvisiblePrimaryKeys: !isMariaDb && IsAtLeast(version, s_mySql8),
            SupportsSavepoints: true,
            SupportsGeneratedColumnNullabilityClause: !isMariaDb,
            SupportsVirtualGeneratedColumns: isMariaDb
                ? IsAtLeast(version, s_mariaDb102)
                : IsAtLeast(version, s_mySql57),
            SupportsStoredGeneratedColumns: isMariaDb
                ? IsAtLeast(version, s_mariaDb102)
                : IsAtLeast(version, s_mySql57),
            SupportsSpatialColumnSridAttribute: !isMariaDb,
            SupportsNativeSequences: isMariaDb && IsAtLeast(version, s_mariaDb103),
            SupportsIntersectExcept: isMariaDb ? IsAtLeast(version, s_mariaDb103) : IsAtLeast(version, s_mySql8031),
            SupportsSystemVersioning: isMariaDb && IsAtLeast(version, s_mariaDb1034),
            SupportsFullTextIndex: true);
    }

    private static bool IsAtLeast(
        Version version,
        Version minimumVersion
    ) => version.CompareTo(minimumVersion) >= 0;
}
