namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Process-wide cache of Hi/Lo block state keyed by sequence name and block size.
/// EF Core resolves a fresh <see cref="MySqlValueGeneratorSelector"/> per DbContext;
/// without a shared state cache every context would allocate a new client-side block
/// on its first insert and the Hi/Lo round-trip savings would never materialize.
/// Key includes the block size so a misconfiguration that pairs two different sizes
/// with the same sequence name surfaces as two cache entries rather than as a silent
/// state corruption.
/// </summary>
internal static class MySqlHiLoStateCache
{
    private static readonly ConcurrentDictionary<(string SequenceName, int BlockSize), HiLoValueGeneratorState>
        s_states = new();

    /// <summary>
    /// Returns the shared <see cref="HiLoValueGeneratorState"/> for the given key,
    /// creating it on first observation. Subsequent callers for the same key receive
    /// the same instance so the underlying block window survives across DbContexts.
    /// </summary>
    public static HiLoValueGeneratorState GetOrCreate(
        string sequenceName,
        int blockSize
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        return s_states.GetOrAdd((sequenceName, blockSize), static key => new HiLoValueGeneratorState(key.BlockSize));
    }

    /// <summary>
    /// Returns the current number of cached state entries. Test-only inspection
    /// surface; production code never reads this.
    /// </summary>
    internal static int Count => s_states.Count;

    /// <summary>
    /// Drops every cached state. Test-only; production code never invokes this.
    /// </summary>
    internal static void ResetForTesting() => s_states.Clear();
}
