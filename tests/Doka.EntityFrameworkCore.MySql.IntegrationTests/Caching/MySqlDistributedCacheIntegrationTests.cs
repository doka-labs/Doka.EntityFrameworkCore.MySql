namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies distributed-cache behavior on every supported live engine.</summary>
/// <param name="fixture">The lazily deployed per-engine cache stores.</param>
[IntegrationDatabaseTest]
public sealed partial class MySqlDistributedCacheIntegrationTests(MySqlDistributedCacheFixture fixture)
    : IClassFixture<MySqlDistributedCacheFixture>
{
    private const int CleanupProbeConnectionCount = 2;
    private const int ConcurrentUpsertProbeCount = 16;
    private static readonly int[] s_payloadSizes = [0, 23, 8192, 1024 * 1024];

    private static readonly string[] s_cleanupInitiators =
    [
        "get",
        "try-get",
        "set-array",
        "set-sequence",
        "set-segmented",
        "refresh",
        "remove",
    ];

    private static readonly string[] s_waitedRefreshOperations =
    [
        "refresh-sync",
        "refresh-async",
        "get-sync",
        "get-async",
        "try-get-sync",
        "try-get-async",
    ];

    /// <summary>Returns one independently reported test case per selected database target.</summary>
    // WHY: xUnit v2 cannot skip individual InlineData rows at runtime. Select the
    // engine rows during discovery while keeping each selected engine visible in Rider.
    public static IEnumerable<object[]> SelectedTargets =>
        IntegrationTestEnvironment
            .GetSelectedTargets()
            .Where(target => IntegrationTestEnvironment
                .GetSupportedTargets()
                .Contains(target))
            .Select(target => new object[] { target.ToString() });

    /// <summary>Returns the selected engines crossed with all byte payload sizes.</summary>
    public static IEnumerable<object[]> BytePayloadCases =>
        SelectedTargets.SelectMany(target => s_payloadSizes.Select(size => new[] { target[0], size }));

    /// <summary>Returns sizes large enough to exercise multi-segment payloads.</summary>
    public static IEnumerable<object[]> SegmentedBytePayloadCases =>
        SelectedTargets.SelectMany(target => s_payloadSizes
            .Where(size => size > 1)
            .Select(size => new[] { target[0], size }));

    /// <summary>Returns each selected engine crossed with an independent cleanup initiator.</summary>
    public static IEnumerable<object[]> CleanupCancellationCases =>
        SelectedTargets.SelectMany(target =>
            s_cleanupInitiators.Select(initiator => new[] { target[0], initiator }));

    /// <summary>Returns each selected engine with one synchronous or asynchronous cache path.</summary>
    public static IEnumerable<object[]> SyncAsyncCases =>
        SelectedTargets.SelectMany(target =>
            new[] { new[] { target[0], false }, new[] { target[0], true }, });

    /// <summary>Returns each engine with either deployment selected for the isolation operation.</summary>
    public static IEnumerable<object[]> TableIsolationCases => SyncAsyncCases;

    /// <summary>Returns each engine with one refresh or buffer-read sliding-expiration probe.</summary>
    public static IEnumerable<object[]> CappedSlidingCases => SyncAsyncCases;

    /// <summary>Returns each selected engine with one sliding-read outcome.</summary>
    public static IEnumerable<object[]> SlidingLockWaitCases =>
        SelectedTargets.SelectMany(target => new[]
        {
            new[] { target[0], false, false },
            new[] { target[0], false, true },
            new[] { target[0], true, false },
            new[] { target[0], true, true },
        });

    /// <summary>Returns each selected engine with one lock-waiting cache operation.</summary>
    public static IEnumerable<object[]> WaitedRefreshCases =>
        SelectedTargets.SelectMany(target =>
            s_waitedRefreshOperations.Select(operation => new[] { target[0], operation }));

    /// <summary>Returns each selected engine with one table-lock cancellation initiator.</summary>
    public static IEnumerable<object[]> LockedCancellationCases => CleanupCancellationCases;

    private static IntegrationDatabaseTarget ParseTarget(
        string targetName
    ) => Enum.Parse<IntegrationDatabaseTarget>(targetName);
}
