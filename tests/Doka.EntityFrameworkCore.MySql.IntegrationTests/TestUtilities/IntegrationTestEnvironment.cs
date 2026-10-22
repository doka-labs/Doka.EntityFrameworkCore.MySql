using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Resolves selected engines and source-owned schemas for live test fixtures.</summary>
internal static class IntegrationTestEnvironment
{
    private const string TargetSelectionVariable = "DOKA_INTEGRATION_TARGETS";
    private static readonly IntegrationDatabaseTarget[] s_supportedTargets =
    [
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123,
    ];

    private static TestDatabaseSession? s_session;
    private static readonly ConcurrentDictionary<(IntegrationDatabaseTarget Target, string Owner), Lazy<string>>
        s_scopedConnections = new();
    private static string? s_runId;

    /// <summary>Returns the connection string for one engine and fixture owner.</summary>
    /// <param name="target">The engine provisioned for this run.</param>
    /// <param name="ownerFilePath">The stable source identity that owns the schema.</param>
    /// <remarks>
    /// Topic partials must share a connection factory or explicitly forward the
    /// same owner. Moving only one call site changes its default caller identity.
    /// </remarks>
    public static string GetConnectionString(
        IntegrationDatabaseTarget target,
        [CallerFilePath] string ownerFilePath = ""
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerFilePath);

        var baseConnectionString = GetSession()
            .GetEndpoint(GetTargetId(target))
            .ConnectionString;

        // WHY: xUnit runs distinct classes concurrently. The caller's source
        // file gives all cases in one class one schema without a per-call stack walk.
        return s_scopedConnections.GetOrAdd(
                (target, ownerFilePath),
                static (key, connectionString) => new Lazy<string>(
                    () => CreateScopedConnectionString(key.Owner, connectionString),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                baseConnectionString)
            .Value;
    }

    internal static string GetBaseConnectionString(
        IntegrationDatabaseTarget target
    ) => GetSession()
        .GetEndpoint(GetTargetId(target))
        .ConnectionString;

    /// <summary>
    /// Returns the provider version that exactly matches an integration target.
    /// </summary>
    /// <param name="target">The live database target.</param>
    /// <returns>The provider version used to build the target-specific service graph.</returns>
    public static MySqlServerVersion GetServerVersion(
        IntegrationDatabaseTarget target
    ) => target switch
    {
        IntegrationDatabaseTarget.MySql80 => MySqlServerVersion.MySql(new Version(8, 0, 0)),
        IntegrationDatabaseTarget.MySql84 => MySqlServerVersion.MySql(
            ParsePinnedImageVersion(TestDatabaseImages.MySql84)),
        IntegrationDatabaseTarget.MySql97 => MySqlServerVersion.MySql(
            ParsePinnedImageVersion(TestDatabaseImages.MySql97)),
        IntegrationDatabaseTarget.MariaDb1011 => MySqlServerVersion.MariaDb(
            ParsePinnedImageVersion(TestDatabaseImages.MariaDb1011)),
        IntegrationDatabaseTarget.MariaDb114 => MySqlServerVersion.MariaDb(
            ParsePinnedImageVersion(TestDatabaseImages.MariaDb114)),
        IntegrationDatabaseTarget.MariaDb118 => MySqlServerVersion.MariaDb(
            ParsePinnedImageVersion(TestDatabaseImages.MariaDb118)),
        IntegrationDatabaseTarget.MariaDb123 => MySqlServerVersion.MariaDb(
            ParsePinnedImageVersion(TestDatabaseImages.MariaDb123)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(target),
            target,
            $"Unsupported integration target: {target}"),
    };

    /// <summary>Reports whether a data case belongs to the current engine selection.</summary>
    public static bool IsTargetSelected(
        IntegrationDatabaseTarget target
    ) => GetSelectedTargets().Contains(target);

    /// <summary>
    /// Returns every active repo-local database target independently of an
    /// optional per-run target selection.
    /// </summary>
    public static IReadOnlyList<IntegrationDatabaseTarget> GetSupportedTargets() => s_supportedTargets;

    internal static Version ParsePinnedImageVersion(
        string image
    )
    {
        var digestSeparator = image.IndexOf('@');
        var tagSeparator = digestSeparator > 0 ? image.LastIndexOf(':', digestSeparator - 1) : -1;

        if (tagSeparator < 0
            || digestSeparator <= tagSeparator + 1
            || !Version.TryParse(image.AsSpan(tagSeparator + 1, digestSeparator - tagSeparator - 1), out var version)
            || version.Build < 0)
        {
            throw new InvalidOperationException(
                $"The integration database image does not contain a numeric version tag: {image}");
        }

        return version;
    }

    public static string GetTargetSelectionSkipReason(
        IEnumerable<IntegrationDatabaseTarget> targets
    )
    {
        var requestedTargetIds = targets
            .Select(GetTargetId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectedTargetIds = GetSelectedTargets()
            .Select(GetTargetId)
            .ToArray();

        return $"Integration target selection '{string.Join(",", selectedTargetIds)}' excludes the requested targets "
            + $"({string.Join(", ", requestedTargetIds)}).";
    }

    /// <summary>Returns the validated explicit selection or the supported engine matrix.</summary>
    public static IReadOnlyList<IntegrationDatabaseTarget> GetSelectedTargets()
    {
        var configuredTargets = Environment.GetEnvironmentVariable(TargetSelectionVariable);
        if (string.IsNullOrWhiteSpace(configuredTargets))
        {
            return s_supportedTargets;
        }

        var targetIds = configuredTargets
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(targetId => !string.IsNullOrWhiteSpace(targetId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"{TargetSelectionVariable} must contain at least one target id when it is configured.");
        }

        var supportedTargetIds = Enum.GetValues<IntegrationDatabaseTarget>()
            .Select(GetTargetId)
            .ToArray();

        var invalidTargetIds = targetIds
            .Except(supportedTargetIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(targetId => targetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTargetIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unsupported integration target id(s) in {TargetSelectionVariable}: {string.Join(", ", invalidTargetIds)}. "
                + $"Accepted values are: {string.Join(", ", supportedTargetIds)}.");
        }

        return targetIds
            .Select(ParseTargetId)
            .OrderBy(target => target)
            .ToArray();
    }

    public static TestDatabaseRequest CreateRequest(
        IntegrationDatabaseTarget target
    )
    {
        return target switch
        {
            IntegrationDatabaseTarget.MySql80 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MySql,
                "mysql:8.0",
                null,
                IntegrationConnectionStringSettings.MySql80Variable),
            IntegrationDatabaseTarget.MySql84 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MySql,
                "mysql:8.4",
                TestDatabaseImages.MySql84,
                IntegrationConnectionStringSettings.MySql84Variable),
            IntegrationDatabaseTarget.MySql97 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MySql,
                "mysql:9.7",
                TestDatabaseImages.MySql97,
                IntegrationConnectionStringSettings.MySql97Variable),
            IntegrationDatabaseTarget.MariaDb1011 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MariaDb,
                "mariadb:10.11",
                TestDatabaseImages.MariaDb1011,
                IntegrationConnectionStringSettings.MariaDb1011Variable),
            IntegrationDatabaseTarget.MariaDb114 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MariaDb,
                "mariadb:11.4",
                TestDatabaseImages.MariaDb114,
                IntegrationConnectionStringSettings.MariaDb114Variable),
            IntegrationDatabaseTarget.MariaDb118 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MariaDb,
                "mariadb:11.8",
                TestDatabaseImages.MariaDb118,
                IntegrationConnectionStringSettings.MariaDb118Variable),
            IntegrationDatabaseTarget.MariaDb123 => new TestDatabaseRequest(
                GetTargetId(target),
                TestDatabaseEngine.MariaDb,
                "mariadb:12.3",
                TestDatabaseImages.MariaDb123,
                IntegrationConnectionStringSettings.MariaDb123Variable),
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Unsupported integration target: {target}"),
        };
    }

    public static void Initialize(
        TestDatabaseSession session
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        if (Interlocked.CompareExchange(ref s_session, session, null) is not null)
        {
            throw new InvalidOperationException("The integration database session is already initialized.");
        }

        s_runId = Guid.NewGuid().ToString("N");
    }

    public static async Task ResetAsync(
        TestDatabaseSession session
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ReferenceEquals(Interlocked.CompareExchange(ref s_session, null, session), session))
        {
            throw new InvalidOperationException("The integration database session does not own the active environment.");
        }

        List<Exception>? cleanupFailures = null;

        try
        {
            foreach (var scopedConnection in s_scopedConnections.Values)
            {
                if (scopedConnection.IsValueCreated)
                {
                    try
                    {
                        await IntegrationDatabaseUtilities
                            .DropDatabaseAsync(scopedConnection.Value)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures ??= [];
                        cleanupFailures.Add(exception);
                    }
                }
            }
        }
        finally
        {
            s_scopedConnections.Clear();
            s_runId = null;
        }

        if (cleanupFailures is not null)
        {
            throw new AggregateException("Failed to drop one or more integration-test databases.", cleanupFailures);
        }
    }

    internal static string CreateScopedDatabaseName(
        string runId,
        string ownerFilePath
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerFilePath);

        var ownerHash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerFilePath));

        return $"doka_it_{runId}_{Convert.ToHexString(ownerHash.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    private static string CreateScopedConnectionString(
        string ownerFilePath,
        string baseConnectionString
    )
    {
        var runId = Volatile.Read(ref s_runId)
            ?? throw new InvalidOperationException("The integration database run has not started.");
        var databaseName = CreateScopedDatabaseName(runId, ownerFilePath);
        var scopedConnectionString = IntegrationDatabaseUtilities.BuildConnectionString(
            baseConnectionString,
            databaseName);

        // WHY: This synchronous compatibility surface is used by both sync and
        // async tests; schema provisioning runs once per class and target.
        IntegrationDatabaseUtilities.EnsureDatabaseExists(scopedConnectionString);

        return scopedConnectionString;
    }

    private static TestDatabaseSession GetSession()
    {
        return Volatile.Read(ref s_session)
            ?? throw new InvalidOperationException(
                "The integration database fixture has not initialized. "
                + "Live integration test classes must carry IntegrationDatabaseTestAttribute.");
    }

    private static IntegrationDatabaseTarget ParseTargetId(
        string targetId
    )
    {
        return targetId.ToLowerInvariant() switch
        {
            "mysql80" => IntegrationDatabaseTarget.MySql80,
            "mysql84" => IntegrationDatabaseTarget.MySql84,
            "mysql97" => IntegrationDatabaseTarget.MySql97,
            "mariadb1011" => IntegrationDatabaseTarget.MariaDb1011,
            "mariadb114" => IntegrationDatabaseTarget.MariaDb114,
            "mariadb118" => IntegrationDatabaseTarget.MariaDb118,
            "mariadb123" => IntegrationDatabaseTarget.MariaDb123,
            _ => throw new ArgumentOutOfRangeException(
                nameof(targetId),
                targetId,
                $"Unsupported integration target id: {targetId}"),
        };
    }

    private static string GetTargetId(
        IntegrationDatabaseTarget target
    )
    {
        return target switch
        {
            IntegrationDatabaseTarget.MySql80 => "mysql80",
            IntegrationDatabaseTarget.MySql84 => "mysql84",
            IntegrationDatabaseTarget.MySql97 => "mysql97",
            IntegrationDatabaseTarget.MariaDb1011 => "mariadb1011",
            IntegrationDatabaseTarget.MariaDb114 => "mariadb114",
            IntegrationDatabaseTarget.MariaDb118 => "mariadb118",
            IntegrationDatabaseTarget.MariaDb123 => "mariadb123",
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Unsupported integration target: {target}"),
        };
    }
}
