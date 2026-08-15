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
    /// Gets the stable target used by discovery when none is configured.
    /// </summary>
    public const string DefaultTarget = "mysql84";

    /// <summary>
    /// Resolves the target used by target-dependent discovery.
    /// </summary>
    /// <returns>The target identifier shared by discovery and database execution.</returns>
    public static string Resolve() => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// Resolves the explicitly selected target for a live specification run.
    /// </summary>
    public static string ResolveForExecution() => ResolveForExecution(
        Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// Determines whether engine-limitation skips are disabled for a re-evaluation probe.
    /// </summary>
    public static bool IsEngineLimitationProbeEnabled() => IsEnabled(EngineLimitationProbeEnvironmentVariableName);

    /// <summary>
    /// Determines whether framework-limitation skips are disabled for a re-evaluation probe.
    /// </summary>
    public static bool IsFrameworkLimitationProbeEnabled() =>
        IsEnabled(FrameworkLimitationProbeEnvironmentVariableName);

    /// <summary>
    /// Resolves an explicit discovery target or the stable default when it is absent.
    /// </summary>
    /// <param name="configuredTarget">Configured target value, or <see langword="null"/>.</param>
    /// <returns>The configured target or <see cref="DefaultTarget"/>.</returns>
    internal static string Resolve(
        string? configuredTarget
    ) => configuredTarget ?? DefaultTarget;

    internal static string ResolveForExecution(
        string? configuredTarget
    ) => string.IsNullOrWhiteSpace(configuredTarget)
        ? throw new InvalidOperationException(
            $"Live FunctionalTests execute exactly one database target per process. Set "
            + $"{EnvironmentVariableName} to mysql84, mysql97, mariadb1011, mariadb114, "
            + "mariadb118, or mariadb123. CI executes the complete six-target matrix.")
        : configuredTarget;

    private static bool IsEnabled(
        string environmentVariableName
    ) => string.Equals(
        Environment.GetEnvironmentVariable(environmentVariableName),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
