namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Resolves the database target used by specification discovery and execution.
/// </summary>
internal static class SpecTestTarget
{
    /// <summary>
    /// Environment variable used to select a supported specification-test target.
    /// </summary>
    public const string EnvironmentVariableName = "DOKA_SPEC_TEST_TARGET";

    /// <summary>
    /// Environment variable which makes engine-limited tests executable for re-evaluation.
    /// </summary>
    public const string EngineLimitationProbeEnvironmentVariableName = "DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS";

    /// <summary>
    /// Environment variable which makes framework-limited tests executable for re-evaluation.
    /// </summary>
    public const string FrameworkLimitationProbeEnvironmentVariableName = "DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS";

    /// <summary>
    /// Gets the local target shared by discovery and execution when none is configured.
    /// </summary>
    public const string DefaultTarget = "mariadb123";

    /// <summary>
    /// Resolves the target used by target-dependent discovery.
    /// </summary>
    /// <returns>The target identifier shared by discovery and database execution.</returns>
    public static string Resolve() => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// Resolves the selected live target, requiring an explicit target in CI.
    /// </summary>
    public static string ResolveForExecution() => ResolveForExecution(
        Environment.GetEnvironmentVariable(EnvironmentVariableName),
        MySqlTestEnvironment.IsCi);

    /// <summary>
    /// Determines whether engine-limitation skips are disabled for a re-evaluation probe.
    /// </summary>
    public static bool IsEngineLimitationProbeEnabled() => IsEnabled(EngineLimitationProbeEnvironmentVariableName);

    /// <summary>
    /// Determines whether framework-limitation skips are disabled for a re-evaluation probe.
    /// </summary>
    public static bool IsFrameworkLimitationProbeEnabled()
        => IsEnabled(FrameworkLimitationProbeEnvironmentVariableName);

    /// <summary>
    /// Resolves an explicit discovery target or the stable default when it is absent.
    /// </summary>
    /// <param name="configuredTarget">Configured target value, or <see langword="null" />.</param>
    /// <returns>The configured target or <see cref="DefaultTarget" />.</returns>
    internal static string Resolve(
        string? configuredTarget
    ) => configuredTarget ?? DefaultTarget;

    /// <summary>Resolves a local default while retaining explicit CI target ownership.</summary>
    /// <param name="configuredTarget">The explicit target, or null when none is configured.</param>
    /// <param name="isCi">Whether a missing target must be rejected.</param>
    /// <returns>The explicit target or the local MariaDB target.</returns>
    internal static string ResolveForExecution(
        string? configuredTarget,
        bool isCi
    )
    {
        // WHY: Native MTP does not apply the VSTest runsettings default. Resolve it
        // here so CLI and IDE hosts agree without making CI matrix rows implicit.
        if (configuredTarget is null
            && !isCi)
        {
            return DefaultTarget;
        }

        if (string.IsNullOrWhiteSpace(configuredTarget))
        {
            throw new InvalidOperationException(
                $"Live FunctionalTests execute exactly one database target per process. Set "
                + $"{EnvironmentVariableName} to mysql84, mysql97, mariadb1011, mariadb114, "
                + "mariadb118, or mariadb123. CI executes the complete six-target matrix.");
        }

        return configuredTarget;
    }

    private static bool IsEnabled(
        string environmentVariableName
    ) => string.Equals(
        Environment.GetEnvironmentVariable(environmentVariableName),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
