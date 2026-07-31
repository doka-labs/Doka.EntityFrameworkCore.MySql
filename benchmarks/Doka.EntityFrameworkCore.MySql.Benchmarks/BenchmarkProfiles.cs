namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class BenchmarkProfiles
{
    internal const string SmokeProfile = "smoke";
    internal const string ScorecardProfile = "scorecard";
    internal const string StressProfile = "stress";

    public static string Current
    {
        get
        {
            var configuredProfile = Environment.GetEnvironmentVariable("DOKA_BENCHMARK_PROFILE");

            if (string.Equals(configuredProfile, StressProfile, StringComparison.OrdinalIgnoreCase))
            {
                return StressProfile;
            }

            return string.Equals(configuredProfile, ScorecardProfile, StringComparison.OrdinalIgnoreCase)
                ? ScorecardProfile
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
        ScorecardProfile =>
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
        ScorecardProfile =>
        [
            1,
            100,
            1000,
        ],
        _ => [100],
    };

    public static IEnumerable<int> SaveChangesEntityCounts() => Current switch
    {
        StressProfile =>
        [
            10,
            100,
            1000,
            10000,
        ],
        ScorecardProfile =>
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
}
