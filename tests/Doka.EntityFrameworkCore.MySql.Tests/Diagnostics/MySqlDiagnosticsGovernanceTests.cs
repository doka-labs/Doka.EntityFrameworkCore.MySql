using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Freezes the diagnostics-governance baseline for categories and event ranges.
/// </summary>
public sealed class MySqlDiagnosticsGovernanceTests
{
    private static readonly string[] s_expectedCategories =
    [
        "Doka.EntityFrameworkCore.MySql.Configuration",
        "Doka.EntityFrameworkCore.MySql.Query",
        "Doka.EntityFrameworkCore.MySql.Update",
        "Doka.EntityFrameworkCore.MySql.Migrations",
        "Doka.EntityFrameworkCore.MySql.Scaffolding",
        "Doka.EntityFrameworkCore.MySql.Resilience",
        "Doka.EntityFrameworkCore.MySql.Spatial",
    ];

    private static readonly Dictionary<string, (int Start, int End)> s_eventRanges = new(StringComparer.Ordinal)
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
        [nameof(MySqlEventId.MigrationLockAcquired)] = (1100, 1199),
        [nameof(MySqlEventId.MigrationLockTimeout)] = (1100, 1199),
        [nameof(MySqlEventId.MigrationLockAcquireFailed)] = (1100, 1199),
        [nameof(MySqlEventId.MigrationOperationHandlerSelected)] = (1100, 1199),
        [nameof(MySqlEventId.InvalidMigrationOperationHandlerRegistration)] = (1100, 1199),
        [nameof(MySqlEventId.MigrationOperationHandlerFailed)] = (1100, 1199),
        [nameof(MySqlEventId.MigrationOperationHandlerContractViolation)] = (1100, 1199),
        [nameof(MySqlEventId.UnknownMigrationOperation)] = (1100, 1199),
        [nameof(MySqlEventId.SpatialSridMismatchDetected)] = (1600, 1699),
    };

    /// <summary>Returns every governed event and subsystem boundary as an independent case.</summary>
    public static IEnumerable<object[]> EventRangeCases => s_eventRanges.Select(
        item => new object[] { item.Key, item.Value.Start, item.Value.End });

    /// <summary>
    /// Verifies that the stable provider logging categories match the documented taxonomy.
    /// </summary>
    [Fact]
    public void Logger_categories_match_the_documented_phase4_taxonomy()
    {
        // Arrange
        var expectedCategories = s_expectedCategories;

        // Act
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

        // Assert
        Assert.Equal(expectedCategories, categories);
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
        // Arrange
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
            [nameof(MySqlEventId.MigrationLockAcquired)] = 1100,
            [nameof(MySqlEventId.MigrationLockTimeout)] = 1101,
            [nameof(MySqlEventId.MigrationLockAcquireFailed)] = 1103,
            [nameof(MySqlEventId.MigrationOperationHandlerSelected)] = 1110,
            [nameof(MySqlEventId.InvalidMigrationOperationHandlerRegistration)] = 1111,
            [nameof(MySqlEventId.MigrationOperationHandlerFailed)] = 1112,
            [nameof(MySqlEventId.MigrationOperationHandlerContractViolation)] = 1113,
            [nameof(MySqlEventId.UnknownMigrationOperation)] = 1114,
            [nameof(MySqlEventId.SpatialSridMismatchDetected)] = 1603,
        };

        // Act
        var actualEventIds = typeof(MySqlEventId)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EventId))
            .ToDictionary(field => field.Name, field => ((EventId)field.GetValue(null)!).Id, StringComparer.Ordinal);

        // Assert
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
        // Arrange
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

        // Act
        var unconsumed = eventIdNames
            .Except(emitterMethodNames, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.True(
            unconsumed.Count == 0,
            $"Every MySqlEventId field must have a same-named MySqlLoggerMessages emitter. Unconsumed event IDs: {string.Join(", ", unconsumed)}.");
    }

    /// <summary>Verifies every public event has a range mapping before individual range checks run.</summary>
    [Fact]
    public void Every_provider_event_has_a_governed_subsystem_range()
    {
        // Arrange
        var mappedNames = s_eventRanges.Keys.ToHashSet(StringComparer.Ordinal);

        // Act
        var publicNames = typeof(MySqlEventId)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EventId))
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Assert
        Assert.True(mappedNames.SetEquals(publicNames));
    }

    /// <summary>Verifies one provider event remains inside its approved subsystem range.</summary>
    /// <param name="fieldName">The independently reported event field.</param>
    /// <param name="minimum">The inclusive subsystem range start.</param>
    /// <param name="maximum">The inclusive subsystem range end.</param>
    [Theory]
    [MemberData(nameof(EventRangeCases))]
    public void Provider_event_ids_stay_inside_the_approved_subsystem_ranges(
        string fieldName,
        int minimum,
        int maximum
    )
    {
        // Arrange
        var field = typeof(MySqlEventId).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"The governed event field '{fieldName}' is missing.");

        // Act
        var eventId = field.GetValue(null) is EventId value
            ? value.Id
            : throw new InvalidOperationException($"The governed field '{fieldName}' is not an EventId.");

        // Assert
        Assert.InRange(eventId, minimum, maximum);
    }
}
