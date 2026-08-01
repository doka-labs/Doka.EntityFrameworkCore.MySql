namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Derives the database-scoped MySQL advisory lock name used by
/// <see cref="MySqlHistoryRepository"/> for migration serialization. Database
/// scoping ensures applications that share a MySQL server but use different
/// schemas do not block each other during migrations.
/// </summary>
internal static class MySqlAdvisoryLockNaming
{
    /// <summary>
    /// Prefix for the advisory lock name. The full lock name is
    /// <c>__ef_migrations_lock:{databaseName}</c>; when the combined length
    /// exceeds MySQL's 64-character <c>GET_LOCK</c> limit the database name
    /// is replaced by a SHA-256 suffix.
    /// </summary>
    internal const string LockNamePrefix = "__ef_migrations_lock:";

    /// <summary>
    /// MySQL <c>GET_LOCK</c> truncates names longer than 64 characters as of
    /// 5.7.5 (documented as an error since 8.0.1). We treat the limit as hard.
    /// </summary>
    internal const int MySqlLockNameMaxLength = 64;

    /// <summary>
    /// Derives the lock name from the given connection string. A
    /// <see langword="null"/> or whitespace input yields the prefix alone so a
    /// missing database name surfaces as an obvious collision-prone marker
    /// rather than silently re-introducing the historical server-global name.
    /// </summary>
    /// <param name="connectionString">The MySQL connection string supplying the database name.</param>
    /// <returns>A lock name that fits within MySQL's 64-character limit.</returns>
    public static string BuildLockName(
        string? connectionString
    )
    {
        var databaseName = string.IsNullOrWhiteSpace(connectionString)
            ? string.Empty
            : new MySqlConnectionStringBuilder(connectionString).Database ?? string.Empty;

        var candidate = LockNamePrefix + databaseName;

        if (candidate.Length <= MySqlLockNameMaxLength)
        {
            return candidate;
        }

        // 16 hex chars (8 bytes from SHA-256) keep collision probability
        // negligible for the small set of databases an application is
        // realistically deployed against.
        var suffix = MySqlDiagnosticScopeId.Create(databaseName);

        return LockNamePrefix + suffix;
    }

    /// <summary>
    /// Produces a stable, opaque SHA-256-derived identifier for diagnostics.
    /// Telemetry uses this deterministic pseudonym instead of the lock name
    /// because the latter may contain a customer database name.
    /// </summary>
    public static string BuildDiagnosticScopeId(
        string lockName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);

        return MySqlDiagnosticScopeId.Create(lockName);
    }
}
