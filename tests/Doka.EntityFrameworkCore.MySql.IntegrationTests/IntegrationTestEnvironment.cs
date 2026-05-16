namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

internal static class IntegrationTestEnvironment
{
    private const string TargetSelectionVariable = "DOKA_INTEGRATION_TARGETS";

    // Use root credentials for repo-local Docker test containers. These are throwaway
    // containers with no production data -- root access simplifies tests that need
    // CREATE/DROP DATABASE, GRANT, or other elevated operations.
    private const string MySql80DefaultConnectionString =
        "Server=127.0.0.1;Port=33066;Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;";

    private const string MySql84DefaultConnectionString =
        "Server=127.0.0.1;Port=33068;Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;";

    private const string MariaDb114DefaultConnectionString =
        "Server=127.0.0.1;Port=33067;Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;";

    private const string MariaDb118DefaultConnectionString =
        "Server=127.0.0.1;Port=33069;Database=doka_provider;User ID=root;Password=root_password;Persist Security Info=True;";

    private const string ComposeStartCommand = "docker compose -f docker/compose.yml up -d";

    private static readonly ConcurrentDictionary<IntegrationDatabaseTarget, IntegrationDatabaseAvailability>
        s_availabilityCache = new();

    public static string GetConnectionString(
        IntegrationDatabaseTarget target
    )
    {
        return ResolveConfiguration(target)
                ?.ConnectionString
            ?? throw new InvalidOperationException(
                $"Integration database target '{GetTargetName(target)}' does not have a resolved connection string.");
    }

    public static bool IsAvailable(
        IntegrationDatabaseTarget target
    )
    {
        return GetAvailability(target)
            .IsAvailable;
    }

    public static bool IsTargetSelected(
        IntegrationDatabaseTarget target
    )
    {
        var configuredTargetIds = GetConfiguredTargetIds();

        return configuredTargetIds is null || configuredTargetIds.Contains(GetTargetId(target));
    }

    public static string GetTargetSelectionSkipReason(
        IEnumerable<IntegrationDatabaseTarget> targets
    )
    {
        var configuredTargetIds = GetConfiguredTargetIds();
        var requestedTargetIds = targets
            .Select(GetTargetId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredTargetIds is null)
        {
            return
                $"Integration target selection did not enable any of the requested targets ({string.Join(", ", requestedTargetIds)}). "
                + $"Set {TargetSelectionVariable} to a comma-separated subset of the supported ids when explicit target scoping is required.";
        }

        return $"Integration target selection '{string.Join(",", configuredTargetIds)}' excludes the requested targets "
            + $"({string.Join(", ", requestedTargetIds)}).";
    }

    public static string GetSkipReason(
        IntegrationDatabaseTarget target
    )
    {
        return GetAvailability(target)
                .SkipReason
            ?? $"Integration database target '{GetTargetName(target)}' is not available.";
    }

    private static IntegrationDatabaseAvailability GetAvailability(
        IntegrationDatabaseTarget target
    )
    {
        return s_availabilityCache.GetOrAdd(target, ProbeAvailability);
    }

    private static IntegrationDatabaseAvailability ProbeAvailability(
        IntegrationDatabaseTarget target
    )
    {
        var configuration = ResolveConfiguration(target);

        if (configuration is null)
        {
            var environmentVariableName = GetEnvironmentVariableName(target);
            var skipReason =
                $"Integration database target '{GetTargetName(target)}' requires the explicit environment variable "
                + $"'{environmentVariableName}'. No bundled local default exists for this hosted target. "
                + $"Use scoped hosted credentials locally or run the protected hosted workflow.";

            return new IntegrationDatabaseAvailability(false, null, skipReason);
        }

        var builder = new MySqlConnectionStringBuilder(configuration.ConnectionString)
        {
            ConnectionTimeout = 2,
            Pooling = false,
        };

        try
        {
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();

            return new IntegrationDatabaseAvailability(true, configuration.ConnectionString, null);
        }
        catch (Exception exception)
        {
            var skipReason = $"Integration database target '{GetTargetName(target)}' is not reachable. "
                + $"Checked environment variable '{configuration.EnvironmentVariableName}' first and then the bundled local Compose default "
                + $"'{configuration.EndpointDescription}'. Start the bundled stack with '{ComposeStartCommand}' "
                + $"or set '{configuration.EnvironmentVariableName}'. Last probe failure: {exception.GetType().Name}: {exception.Message}";

            return new IntegrationDatabaseAvailability(false, configuration.ConnectionString, skipReason);
        }
    }

    private static IntegrationDatabaseConfiguration? ResolveConfiguration(
        IntegrationDatabaseTarget target
    )
    {
        var environmentVariableName = GetEnvironmentVariableName(target);
        var configuredConnectionString = Environment.GetEnvironmentVariable(environmentVariableName);

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return CreateConfiguration(configuredConnectionString, environmentVariableName);
        }

        if (!HasBundledLocalDefault(target))
        {
            return null;
        }

        return CreateConfiguration(GetDefaultConnectionString(target), environmentVariableName);
    }

    private static IntegrationDatabaseConfiguration CreateConfiguration(
        string connectionString,
        string environmentVariableName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);

        var builder = new MySqlConnectionStringBuilder(connectionString);
        var endpointDescription = $"{builder.Server}:{builder.Port}/{builder.Database}";

        return new IntegrationDatabaseConfiguration(connectionString, environmentVariableName, endpointDescription);
    }

    private static string GetDefaultConnectionString(
        IntegrationDatabaseTarget target
    )
    {
        return target switch
        {
            IntegrationDatabaseTarget.MySql80 => MySql80DefaultConnectionString,
            IntegrationDatabaseTarget.MySql84 => MySql84DefaultConnectionString,
            IntegrationDatabaseTarget.MariaDb114 => MariaDb114DefaultConnectionString,
            IntegrationDatabaseTarget.MariaDb118 => MariaDb118DefaultConnectionString,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Unsupported integration target: {target}"),
        };
    }

    private static bool HasBundledLocalDefault(
        IntegrationDatabaseTarget target
    )
    {
        return target is IntegrationDatabaseTarget.MySql80
            or IntegrationDatabaseTarget.MySql84
            or IntegrationDatabaseTarget.MariaDb114
            or IntegrationDatabaseTarget.MariaDb118;
    }

    private static string GetEnvironmentVariableName(
        IntegrationDatabaseTarget target
    )
    {
        return target switch
        {
            IntegrationDatabaseTarget.MySql80 => IntegrationConnectionStringSettings.MySql80Variable,
            IntegrationDatabaseTarget.MySql84 => IntegrationConnectionStringSettings.MySql84Variable,
            IntegrationDatabaseTarget.MariaDb114 => IntegrationConnectionStringSettings.MariaDb114Variable,
            IntegrationDatabaseTarget.MariaDb118 => IntegrationConnectionStringSettings.MariaDb118Variable,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"Unsupported integration target: {target}"),
        };
    }

    private static string GetTargetName(
        IntegrationDatabaseTarget target
    )
    {
        return target switch
        {
            IntegrationDatabaseTarget.MySql80 => "MySQL 8.0",
            IntegrationDatabaseTarget.MySql84 => "MySQL 8.4",
            IntegrationDatabaseTarget.MariaDb114 => "MariaDB 11.4",
            IntegrationDatabaseTarget.MariaDb118 => "MariaDB 11.8",
            _ => target.ToString(),
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

    private static HashSet<string>? GetConfiguredTargetIds()
    {
        var configuredTargets = Environment.GetEnvironmentVariable(TargetSelectionVariable);

        if (string.IsNullOrWhiteSpace(configuredTargets))
        {
            return null;
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

        var supportedTargetIds = new[]
        {
            "mysql80",
            "mysql84",
            "mariadb114",
            "mariadb118",
        };

        var invalidTargetIds = targetIds
            .Except(supportedTargetIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(targetId => targetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTargetIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unsupported integration target id(s) in {TargetSelectionVariable}: {string.Join(", ", invalidTargetIds)}. "
                + $"Supported values are: {string.Join(", ", supportedTargetIds)}.");
        }

        return targetIds;
    }

    private sealed record IntegrationDatabaseConfiguration(
        string ConnectionString,
        string EnvironmentVariableName,
        string EndpointDescription
    );

    private sealed record IntegrationDatabaseAvailability(
        bool IsAvailable,
        string? ConnectionString,
        string? SkipReason
    );
}
