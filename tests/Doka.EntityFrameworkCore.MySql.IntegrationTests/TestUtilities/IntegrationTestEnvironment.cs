namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class IntegrationTestEnvironment
{
    private const string TargetSelectionVariable = "DOKA_INTEGRATION_TARGETS";
    private static readonly IntegrationDatabaseTarget[] s_supportedTargets =
    [
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
    ];

    private static TestDatabaseSession? s_session;

    public static string GetConnectionString(
        IntegrationDatabaseTarget target
    )
    {
        return GetSession()
            .GetEndpoint(GetTargetId(target))
            .ConnectionString;
    }

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
        IntegrationDatabaseTarget.MySql84 => MySqlServerVersion.MySql(new Version(8, 4, 0)),
        IntegrationDatabaseTarget.MariaDb114 => MySqlServerVersion.MariaDb(new Version(11, 4, 0)),
        IntegrationDatabaseTarget.MariaDb118 => MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(target),
            target,
            $"Unsupported integration target: {target}"),
    };

    public static bool IsTargetSelected(
        IntegrationDatabaseTarget target
    ) => GetSelectedTargets().Contains(target);

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
    }

    public static void Reset(
        TestDatabaseSession session
    )
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ReferenceEquals(Interlocked.CompareExchange(ref s_session, null, session), session))
        {
            throw new InvalidOperationException("The integration database session does not own the active environment.");
        }
    }

    private static TestDatabaseSession GetSession()
    {
        return Volatile.Read(ref s_session)
            ?? throw new InvalidOperationException(
                "The integration database fixture has not initialized. "
                + $"Live integration test classes must use collection '{IntegrationDatabaseTestGroup.Name}'.");
    }

    private static IntegrationDatabaseTarget ParseTargetId(
        string targetId
    )
    {
        return targetId.ToLowerInvariant() switch
        {
            "mysql80" => IntegrationDatabaseTarget.MySql80,
            "mysql84" => IntegrationDatabaseTarget.MySql84,
            "mariadb114" => IntegrationDatabaseTarget.MariaDb114,
            "mariadb118" => IntegrationDatabaseTarget.MariaDb118,
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
            IntegrationDatabaseTarget.MariaDb114 => "mariadb114",
            IntegrationDatabaseTarget.MariaDb118 => "mariadb118",
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Unsupported integration target: {target}"),
        };
    }
}
