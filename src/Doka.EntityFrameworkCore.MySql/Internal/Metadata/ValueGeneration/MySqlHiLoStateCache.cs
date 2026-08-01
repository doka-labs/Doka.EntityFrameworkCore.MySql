namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Process-wide cache of Hi/Lo block state keyed by database identity, sequence name,
/// and block size.
/// EF Core resolves a fresh <see cref="MySqlValueGeneratorSelector"/> per DbContext;
/// without a shared state cache every context would allocate a new client-side block
/// on its first insert and the Hi/Lo round-trip savings would never materialize.
/// Database identity prevents two servers or databases with the same sequence name
/// from sharing a client-side range. The cache is bounded because connection strings
/// and tenant databases can be high-cardinality in long-lived processes.
/// </summary>
internal static class MySqlHiLoStateCache
{
    internal const int Capacity = 1024;

    private static readonly ConcurrentDictionary<CacheKey, HiLoValueGeneratorState> s_states = new();
    private static readonly ConcurrentQueue<CacheKey> s_insertionOrder = new();

    /// <summary>
    /// Returns the shared <see cref="HiLoValueGeneratorState"/> for the given key,
    /// creating it on first observation. Subsequent callers for the same key receive
    /// the same instance so the underlying block window survives across DbContexts.
    /// </summary>
    public static HiLoValueGeneratorState GetOrCreate(
        MySqlDatabaseIdentity databaseIdentity,
        string sequenceName,
        int blockSize
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        var key = new CacheKey(databaseIdentity, sequenceName, blockSize);
        var candidate = new HiLoValueGeneratorState(blockSize);
        var state = s_states.GetOrAdd(key, candidate);

        if (ReferenceEquals(state, candidate))
        {
            s_insertionOrder.Enqueue(key);
            TrimToCapacity();
        }

        return state;
    }

    /// <summary>
    /// Returns the current number of cached state entries. Test-only inspection
    /// surface; production code never reads this.
    /// </summary>
    internal static int Count => s_states.Count;

    /// <summary>
    /// Drops every cached state. Test-only; production code never invokes this.
    /// </summary>
    internal static void ResetForTesting()
    {
        s_states.Clear();
        s_insertionOrder.Clear();
    }

    private static void TrimToCapacity()
    {
        while (s_states.Count > Capacity
               && s_insertionOrder.TryDequeue(out var oldestKey))
        {
            _ = s_states.TryRemove(oldestKey, out _);
        }
    }

    private readonly record struct CacheKey(
        MySqlDatabaseIdentity DatabaseIdentity,
        string SequenceName,
        int BlockSize
    );
}

/// <summary>
/// Secret-free identity for the connector transport, physical MySQL endpoint, and
/// logical database that owns a Hi/Lo sequence. Passwords and other credentials are
/// deliberately excluded so the process-wide cache cannot retain them.
/// </summary>
internal readonly record struct MySqlDatabaseIdentity(
    string Server,
    uint Port,
    string Database,
    string UserId,
    MySqlConnectionProtocol ConnectionProtocol,
    string PipeName
)
{
    /// <summary>
    /// Parses the provider connection string into the logical and protocol-specific
    /// endpoint fields that determine sequence ownership. Equivalent connection-
    /// string spellings are canonicalized by <see cref="MySqlConnectionStringBuilder"/>.
    /// </summary>
    public static MySqlDatabaseIdentity FromConnectionString(
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new MySqlConnectionStringBuilder(connectionString);

        return new MySqlDatabaseIdentity(
            builder.Server,
            builder.Port,
            builder.Database,
            builder.UserID,
            builder.ConnectionProtocol,
            builder.PipeName);
    }
}
