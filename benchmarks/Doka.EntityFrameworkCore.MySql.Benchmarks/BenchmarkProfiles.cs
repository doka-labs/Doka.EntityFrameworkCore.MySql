namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkProfiles
{
    internal const string SmokeProfile = "smoke";
    internal const string PairedBlockProfile = "paired-block";
    internal const string ScorecardProfile = "scorecard";
    internal const string StressProfile = "stress";

    /// <summary>
    /// Profiles that measure the complete workload matrix.
    /// </summary>
    /// <remarks>
    /// Falling back to <see cref="SmokeProfile"/> for an unrecognized name is
    /// not a safe default: smoke narrows the row counts, the entity counts, and
    /// the workload set itself. A paired comparison that landed there measured
    /// a small subset while reporting the profile it was asked for, so the
    /// scope claim in its evidence described a matrix it never ran.
    /// </remarks>
    private static readonly string[] s_completeMatrixProfiles =
    [
        PairedBlockProfile,
        ScorecardProfile,
        StressProfile,
    ];

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

            return string.Equals(configuredProfile, PairedBlockProfile, StringComparison.OrdinalIgnoreCase)
                ? PairedBlockProfile
                : SmokeProfile;
        }
    }

    public static IEnumerable<int> HotPathRowCounts() => Current switch
    {
        StressProfile =>
        [
            1,
            100,
            1000,
            10000,
        ],
        ScorecardProfile or PairedBlockProfile =>
        [
            1,
            100,
            1000,
        ],
        _ => [100],
    };

    public static IEnumerable<int> JsonRowCounts() => Current switch
    {
        StressProfile =>
        [
            1,
            100,
            1000,
            10000,
        ],
        ScorecardProfile or PairedBlockProfile =>
        [
            1,
            100,
            1000,
        ],
        _ => [100],
    };

    public static IEnumerable<int> SaveChangesEntityCounts() => Current switch
    {
        StressProfile or ScorecardProfile or PairedBlockProfile =>
        [
            10,
            100,
            1000,
            10000,
        ],
        _ => [100],
    };

    public static bool IsScorecard() => string.Equals(Current, ScorecardProfile, StringComparison.OrdinalIgnoreCase);

    public static bool IsStress() => string.Equals(Current, StressProfile, StringComparison.OrdinalIgnoreCase);

    public static bool IsPairedBlock() =>
        string.Equals(Current, PairedBlockProfile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the active profile measures the complete workload matrix.
    /// </summary>
    public static bool MeasuresCompleteMatrix() =>
        s_completeMatrixProfiles.Contains(Current, StringComparer.OrdinalIgnoreCase);
}
