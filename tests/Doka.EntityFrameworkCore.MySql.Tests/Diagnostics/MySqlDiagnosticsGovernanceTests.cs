using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Freezes the diagnostics-governance baseline for categories and event ranges.
/// </summary>
public sealed class MySqlDiagnosticsGovernanceTests
{
    private static readonly string[] s_expectedCategories =
    {
        "Doka.EntityFrameworkCore.MySql.Configuration",
        "Doka.EntityFrameworkCore.MySql.Query",
        "Doka.EntityFrameworkCore.MySql.Update",
        "Doka.EntityFrameworkCore.MySql.Migrations",
        "Doka.EntityFrameworkCore.MySql.Scaffolding",
        "Doka.EntityFrameworkCore.MySql.Resilience",
        "Doka.EntityFrameworkCore.MySql.Spatial",
    };

    /// <summary>
    /// Verifies that the stable provider logging categories match the documented taxonomy.
    /// </summary>
    [Fact]
    public void Logger_categories_match_the_documented_phase4_taxonomy()
    {
        var categories = new[]
        {
            MySqlLoggerCategory.Configuration,
            MySqlLoggerCategory.Query,
            MySqlLoggerCategory.Update,
            MySqlLoggerCategory.Migrations,
            MySqlLoggerCategory.Scaffolding,
            MySqlLoggerCategory.Resilience,
            MySqlLoggerCategory.Spatial,
        };

        Assert.Equal(s_expectedCategories, categories);

        Assert.Equal(
            categories.Length,
            categories
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    /// <summary>
    /// Verifies that the current provider event catalog retains the documented numeric contract.
    /// </summary>
    [Fact]
    public void Provider_event_ids_match_the_documented_phase4_contract()
    {
        var actualEventIds = typeof(MySqlEventId)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EventId))
            .ToDictionary(field => field.Name, field => ((EventId)field.GetValue(null)!).Id, StringComparer.Ordinal);

        var expectedEventIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(MySqlEventId.ServerVersionResolved)] = 1000,
            [nameof(MySqlEventId.InvalidConfiguration)] = 1001,
            [nameof(MySqlEventId.UnsupportedServerVersion)] = 1005,
            [nameof(MySqlEventId.SchemaUnsupported)] = 1002,
            [nameof(MySqlEventId.KeyOrIndexMaxLengthRequired)] = 1003,
            [nameof(MySqlEventId.ImplicitDecimalPrecisionDefaulted)] = 1004,
            [nameof(MySqlEventId.RetryAttempt)] = 1500,
            [nameof(MySqlEventId.RetryLimitExceeded)] = 1501,
            [nameof(MySqlEventId.SoftCancellation)] = 1502,
            [nameof(MySqlEventId.HardCancellation)] = 1503,
            [nameof(MySqlEventId.CommandTimeoutExhausted)] = 1504,
            [nameof(MySqlEventId.CommitUnknown)] = 1505,
            [nameof(MySqlEventId.ForeignKeyPrincipalTableNotScaffolded)] = 1403,
            [nameof(MySqlEventId.MissingSpatialPackageDuringScaffolding)] = 1600,
            [nameof(MySqlEventId.InvalidSpatialIndexConfiguration)] = 1601,
            [nameof(MySqlEventId.MissingSpatialTranslation)] = 1602,
            [nameof(MySqlEventId.BulkInsertParameterCountCapped)] = 1700,
            [nameof(MySqlEventId.BulkInsertPacketSizeCapped)] = 1701,
            [nameof(MySqlEventId.LockReleaseFailed)] = 1102,
            [nameof(MySqlEventId.SpatialSridMismatchDetected)] = 1603,
        };

        Assert.Equal(expectedEventIds.Count, actualEventIds.Count);

        foreach (var expectedEventId in expectedEventIds)
        {
            Assert.True(
                actualEventIds.TryGetValue(expectedEventId.Key, out var actualEventId),
                $"Missing expected MySqlEventId field '{expectedEventId.Key}'.");
            Assert.Equal(expectedEventId.Value, actualEventId);
        }

        Assert.Equal(
            actualEventIds.Count,
            actualEventIds
                .Values.Distinct()
                .Count());
    }

    /// <summary>
    /// Reverse-coverage drift gate: every <see cref="MySqlEventId"/> field must
    /// have a matching emitter method on <see cref="MySqlLoggerMessages"/> with
    /// the same name. The pairing convention is intentional: the EventId is the
    /// stable consumer-facing surface, the emitter method is the only legitimate
    /// way to fire that EventId. Drift between the two means either an EventId
    /// was added without a logger entry (no production caller can emit it) or a
    /// logger entry was removed without retiring the EventId (the consumer sees
    /// the EventId surface but the production code path no longer fires).
    /// </summary>
    [Fact]
    public void Every_event_id_has_a_matching_logger_message_emitter()
    {
        var eventIdNames = typeof(MySqlEventId)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EventId))
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        var emitterMethodNames = typeof(MySqlLoggerMessages)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method =>
                method.GetParameters()
                    .FirstOrDefault()
                    ?.ParameterType
                == typeof(ILogger))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unconsumed = eventIdNames
            .Except(emitterMethodNames, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unconsumed.Count == 0,
            $"Every MySqlEventId field must have a same-named MySqlLoggerMessages emitter. Unconsumed event IDs: {string.Join(", ", unconsumed)}.");
    }

    /// <summary>
    /// Verifies that the current provider event catalog remains inside the approved subsystem ranges.
    /// </summary>
    [Fact]
    public void Provider_event_ids_stay_inside_the_approved_subsystem_ranges()
    {
        var eventRanges = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal)
        {
            [nameof(MySqlEventId.ServerVersionResolved)] = (1000, 1099),
            [nameof(MySqlEventId.InvalidConfiguration)] = (1000, 1099),
            [nameof(MySqlEventId.UnsupportedServerVersion)] = (1000, 1099),
            [nameof(MySqlEventId.SchemaUnsupported)] = (1000, 1099),
            [nameof(MySqlEventId.KeyOrIndexMaxLengthRequired)] = (1000, 1099),
            [nameof(MySqlEventId.ImplicitDecimalPrecisionDefaulted)] = (1000, 1099),
            [nameof(MySqlEventId.RetryAttempt)] = (1500, 1599),
            [nameof(MySqlEventId.RetryLimitExceeded)] = (1500, 1599),
            [nameof(MySqlEventId.SoftCancellation)] = (1500, 1599),
            [nameof(MySqlEventId.HardCancellation)] = (1500, 1599),
            [nameof(MySqlEventId.CommandTimeoutExhausted)] = (1500, 1599),
            [nameof(MySqlEventId.CommitUnknown)] = (1500, 1599),
            [nameof(MySqlEventId.ForeignKeyPrincipalTableNotScaffolded)] = (1400, 1499),
            [nameof(MySqlEventId.MissingSpatialPackageDuringScaffolding)] = (1600, 1699),
            [nameof(MySqlEventId.InvalidSpatialIndexConfiguration)] = (1600, 1699),
            [nameof(MySqlEventId.MissingSpatialTranslation)] = (1600, 1699),
            [nameof(MySqlEventId.BulkInsertParameterCountCapped)] = (1700, 1799),
            [nameof(MySqlEventId.BulkInsertPacketSizeCapped)] = (1700, 1799),
            [nameof(MySqlEventId.LockReleaseFailed)] = (1100, 1199),
            [nameof(MySqlEventId.SpatialSridMismatchDetected)] = (1600, 1699),
        };

        foreach (var field in typeof(MySqlEventId).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(EventId))
            {
                continue;
            }

            var eventId = (EventId)field.GetValue(null)!;

            Assert.True(
                eventRanges.TryGetValue(field.Name, out var range),
                $"Missing diagnostics-governance range mapping for event '{field.Name}'.");
            Assert.InRange(eventId.Id, range.Start, range.End);
        }
    }
}
