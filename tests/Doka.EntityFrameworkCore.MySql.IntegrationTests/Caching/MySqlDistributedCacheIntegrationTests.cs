namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies distributed-cache behavior on every supported live engine.</summary>
[IntegrationDatabaseTest]
public sealed partial class MySqlDistributedCacheIntegrationTests
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

    private readonly MySqlDistributedCacheFixture _fixture;

    /// <summary>Uses the shared lazy fixture while retaining independent per-test cache ownership.</summary>
    /// <param name="fixture">The lazily deployed per-engine cache stores.</param>
    public MySqlDistributedCacheIntegrationTests(
        MySqlDistributedCacheFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Returns one independently reported test case per selected database target.</summary>

    // WHY: Select supported engine rows during discovery while keeping each
    // selected engine visible as an independent case in Rider.
    public static IEnumerable<TheoryDataRow<string>> SelectedTargets => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => new TheoryDataRow<string>(target.ToString()));

    /// <summary>Returns the selected engines crossed with all byte payload sizes.</summary>
    public static IEnumerable<TheoryDataRow<string, int>> BytePayloadCases => SelectedTargets.SelectMany(target
        => s_payloadSizes.Select(size
            => new TheoryDataRow<string, int>(target.Data, size)));

    /// <summary>Returns sizes large enough to exercise multi-segment payloads.</summary>
    public static IEnumerable<TheoryDataRow<string, int>> SegmentedBytePayloadCases => SelectedTargets.SelectMany(target
        => s_payloadSizes
            .Where(size => size > 1)
            .Select(size => new TheoryDataRow<string, int>(target.Data, size)));

    /// <summary>Returns each selected engine crossed with an independent cleanup initiator.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> CleanupCancellationCases
        => SelectedTargets.SelectMany(target =>
            s_cleanupInitiators.Select(initiator => new TheoryDataRow<string, string>(target.Data, initiator)));

    /// <summary>Returns each selected engine with one synchronous or asynchronous cache path.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> SyncAsyncCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
    });

    /// <summary>Returns each engine with either deployment selected for the isolation operation.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> TableIsolationCases => SyncAsyncCases;

    /// <summary>Returns each engine with one refresh or buffer-read sliding-expiration probe.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> CappedSlidingCases => SyncAsyncCases;

    /// <summary>Returns each selected engine with one sliding-read outcome.</summary>
    public static IEnumerable<TheoryDataRow<string, bool, bool>> SlidingLockWaitCases
        => SelectedTargets.SelectMany(target => new[]
        {
            new TheoryDataRow<string, bool, bool>(target.Data, false, false),
            new TheoryDataRow<string, bool, bool>(target.Data, false, true),
            new TheoryDataRow<string, bool, bool>(target.Data, true, false),
            new TheoryDataRow<string, bool, bool>(target.Data, true, true),
        });

    /// <summary>Returns each selected engine with one lock-waiting cache operation.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> WaitedRefreshCases => SelectedTargets.SelectMany(target =>
        s_waitedRefreshOperations.Select(operation => new TheoryDataRow<string, string>(target.Data, operation)));

    /// <summary>Returns each selected engine with one table-lock cancellation initiator.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> LockedCancellationCases => CleanupCancellationCases;

    private static IntegrationDatabaseTarget ParseTarget(
        string targetName
    ) => Enum.Parse<IntegrationDatabaseTarget>(targetName);
}
