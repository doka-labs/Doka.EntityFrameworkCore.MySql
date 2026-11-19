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
    public static TheoryData<string> SelectedTargets => new(IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(target => IntegrationTestEnvironment
            .GetSupportedTargets()
            .Contains(target))
        .Select(target => target.ToString()));

    /// <summary>Returns the selected engines crossed with all byte payload sizes.</summary>
    public static TheoryData<string, int> BytePayloadCases
    {
        get
        {
            var data = new TheoryData<string, int>();

            foreach (var target in SelectedTargets)
            {
                foreach (var size in s_payloadSizes)
                {
                    data.Add(target, size);
                }
            }

            return data;
        }
    }

    /// <summary>Returns sizes large enough to exercise multi-segment payloads.</summary>
    public static TheoryData<string, int> SegmentedBytePayloadCases
    {
        get
        {
            var data = new TheoryData<string, int>();

            foreach (var target in SelectedTargets)
            {
                foreach (var size in s_payloadSizes)
                {
                    if (size > 1)
                    {
                        data.Add(target, size);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>Returns each selected engine crossed with an independent cleanup initiator.</summary>
    public static TheoryData<string, string> CleanupCancellationCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                foreach (var initiator in s_cleanupInitiators)
                {
                    data.Add(target, initiator);
                }
            }

            return data;
        }
    }

    /// <summary>Returns each selected engine with one synchronous or asynchronous cache path.</summary>
    public static TheoryData<string, bool> SyncAsyncCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, false);
                data.Add(target, true);
            }

            return data;
        }
    }

    /// <summary>Returns each engine with either deployment selected for the isolation operation.</summary>
    public static TheoryData<string, bool> TableIsolationCases => SyncAsyncCases;

    /// <summary>Returns each engine with one refresh or buffer-read sliding-expiration probe.</summary>
    public static TheoryData<string, bool> CappedSlidingCases => SyncAsyncCases;

    /// <summary>Returns each selected engine with one sliding-read outcome.</summary>
    public static TheoryData<string, bool, bool> SlidingLockWaitCases
    {
        get
        {
            var data = new TheoryData<string, bool, bool>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, false, false);
                data.Add(target, false, true);
                data.Add(target, true, false);
                data.Add(target, true, true);
            }

            return data;
        }
    }

    /// <summary>Returns each selected engine with one lock-waiting cache operation.</summary>
    public static TheoryData<string, string> WaitedRefreshCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                foreach (var operation in s_waitedRefreshOperations)
                {
                    data.Add(target, operation);
                }
            }

            return data;
        }
    }

    /// <summary>Returns each selected engine with one table-lock cancellation initiator.</summary>
    public static TheoryData<string, string> LockedCancellationCases => CleanupCancellationCases;

    private static IntegrationDatabaseTarget ParseTarget(
        string targetName
    ) => Enum.Parse<IntegrationDatabaseTarget>(targetName);
}
