namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Reports each engine, storage format and semantic variant as a separate xUnit case.</summary>
public sealed partial class MySqlIndependentCollectionMembershipIntegrationTests
{
    /// <summary>Returns the original large-query shape for both affected GUID decoders.</summary>
    public static IEnumerable<TheoryDataRow<string, MySqlGuidFormat>> LargeGuidCases
    {
        get
        {
            foreach (var target in SelectedTargets())
            {
                yield return new TheoryDataRow<string, MySqlGuidFormat>(target.ToString(), MySqlGuidFormat.Binary16);
                yield return new TheoryDataRow<string, MySqlGuidFormat>(target.ToString(), MySqlGuidFormat.Char36);
            }
        }
    }

    /// <summary>Returns nullable and non-nullable cases, including supported native UUID storage.</summary>
    public static IEnumerable<TheoryDataRow<string, MySqlGuidFormat, string>> GuidCases
    {
        get
        {
            string[] scenarios =
            [
                "single", "duplicates", "empty", "negated",
                "nullable", "null-only", "nullable-empty", "nullable-negated",
            ];

            foreach (var target in SelectedTargets())
            {
                var formats = SupportsNativeUuid(target)
                    ? new[] { MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36, MySqlGuidFormat.Uuid }
                    : new[] { MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36 };

                foreach (var format in formats)
                {
                    foreach (var scenario in scenarios)
                    {
                        yield return new TheoryDataRow<string, MySqlGuidFormat, string>(target.ToString(),
                            format,
                            scenario);
                    }
                }
            }
        }
    }

    /// <summary>Returns independently reported decoded-text and converted-enum scenarios.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> TextCases
    {
        get
        {
            string[] scenarios = ["values", "duplicates", "empty", "nullable", "negated"];

            foreach (var target in SelectedTargets())
            {
                foreach (var scenario in scenarios)
                {
                    yield return new TheoryDataRow<string, string>(target.ToString(), scenario);
                }
            }
        }
    }

    /// <summary>Returns only selected engines that implement native UUID storage.</summary>
    public static IEnumerable<TheoryDataRow<string>> NativeUuidCases => SelectedTargets()
        .Where(SupportsNativeUuid)
        .Select(target => new TheoryDataRow<string>(target.ToString()));

    private static IEnumerable<IntegrationDatabaseTarget> SelectedTargets() => IntegrationTestEnvironment
        .GetSelectedTargets()
        .Where(IntegrationTestEnvironment.GetSupportedTargets()
            .Contains);

    private static bool SupportsNativeUuid(
        IntegrationDatabaseTarget target
    ) => target is IntegrationDatabaseTarget.MariaDb1011
        or IntegrationDatabaseTarget.MariaDb114
        or IntegrationDatabaseTarget.MariaDb118
        or IntegrationDatabaseTarget.MariaDb123;

    private static (Guid?[] Values, bool Nullable, bool Negated, int[] Expected) GuidScenario(
        string scenario
    ) => scenario switch
    {
        "single" => ([CreateGuid(0)], false, false, [1]),
        "duplicates" => ([CreateGuid(0), CreateGuid(0), CreateGuid(-1)], false, false, [1]),
        "empty" => ([], false, false, []),
        "negated" => ([CreateGuid(0)], false, true, [2, 3, 4]),
        "nullable" => ([CreateGuid(0), null], true, false, [1, 2]),
        "null-only" => ([null], true, false, [2]),
        "nullable-empty" => ([], true, false, []),
        "nullable-negated" => ([CreateGuid(0), null], true, true, [3, 4]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown GUID membership scenario."),
    };

    private static (string?[] Values, bool Negated, int[] Expected) TextScenario(
        string scenario
    ) => scenario switch
    {
        "values" => (["Hallo 'Test'", "\"\\q\""], false, [1, 3]),
        "duplicates" => (["Hallo 'Test'", "Hallo 'Test'", "missing"], false, [1]),
        "empty" => ([], false, []),
        "nullable" => (["Hallo 'Test'", null], false, [1, 2]),
        "negated" => (["Hallo 'Test'", null], true, [3, 4]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown text membership scenario."),
    };

    private static (MembershipStatus?[] Values, bool Negated, int[] Expected) EnumScenario(
        string scenario
    ) => scenario switch
    {
        "values" => ([MembershipStatus.Active], false, [1]),
        "duplicates" => ([MembershipStatus.Active, MembershipStatus.Active], false, [1]),
        "empty" => ([], false, []),
        "nullable" => ([MembershipStatus.Active, null], false, [1, 2]),
        "negated" => ([MembershipStatus.Active, null], true, [3, 4]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown enum membership scenario."),
    };
}
