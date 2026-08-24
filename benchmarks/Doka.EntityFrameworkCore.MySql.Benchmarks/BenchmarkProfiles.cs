namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkProfiles
{
    internal const string SmokeProfile = "smoke";
    internal const string ScorecardProfile = "scorecard";
    internal const string StressProfile = "stress";

    /// <summary>
    /// Profiles that measure the complete workload matrix.
    /// </summary>
    /// <remarks>
    /// Falling back to <see cref="SmokeProfile"/> for an unrecognized name is
    /// not safe because smoke narrows the workload set.
    /// </remarks>
    private static readonly string[] s_completeMatrixProfiles = [ScorecardProfile, StressProfile];

    public static string Current
    {
        get
        {
            var configuredProfile = Environment.GetEnvironmentVariable("DOKA_BENCHMARK_PROFILE");

            if (string.Equals(configuredProfile, StressProfile, StringComparison.OrdinalIgnoreCase))
            {
                return StressProfile;
            }

            if (string.Equals(configuredProfile, ScorecardProfile, StringComparison.OrdinalIgnoreCase))
            {
                return ScorecardProfile;
            }

            if (string.IsNullOrWhiteSpace(configuredProfile)
                || string.Equals(configuredProfile, SmokeProfile, StringComparison.OrdinalIgnoreCase))
            {
                return SmokeProfile;
            }

            throw new InvalidOperationException($"Unsupported benchmark profile '{configuredProfile}'.");
        }
    }

    public static IEnumerable<int> HotPathRowCounts() => Current switch
    {
        StressProfile => [1, 100, 1000, 10000],
        ScorecardProfile => [1, 100, 1000],
        _ => [100],
    };

    public static IEnumerable<int> JsonRowCounts() => Current switch
    {
        StressProfile => [1, 100, 1000, 10000],
        ScorecardProfile => [1, 100, 1000],
        _ => [100],
    };

    public static IEnumerable<int> SaveChangesEntityCounts() => Current switch
    {
        StressProfile or ScorecardProfile => [10, 100, 1000, 10000],
        _ => [100],
    };

    public static bool IsScorecard() => string.Equals(Current, ScorecardProfile, StringComparison.OrdinalIgnoreCase);

    public static bool IsStress() => string.Equals(Current, StressProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the active profile measures the complete workload matrix.
    /// </summary>
    public static bool MeasuresCompleteMatrix() =>
        s_completeMatrixProfiles.Contains(Current, StringComparer.OrdinalIgnoreCase);
}
