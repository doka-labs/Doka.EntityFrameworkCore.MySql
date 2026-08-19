using System.Diagnostics;
using System.Reflection;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Freezes the provider observability contract across logs, traces, metrics,
/// privacy rules, alerts, and runbook ownership.
/// </summary>
[Collection(MySqlDiagnosticsTestGroup.Name)]
public sealed class MySqlObservabilityContractTests
{
    private static readonly string[] s_alertSeverities =
    [
        "critical",
        "warning"
    ];

    [Fact]
    public void Machine_readable_contract_matches_the_public_diagnostic_surface()
    {
        using var contract = LoadContract();
        var root = contract.RootElement;
        var operations = root
            .GetProperty("operations")
            .EnumerateArray()
            .ToList();

        var contractSpans = operations
            .Select(operation => operation
                .GetProperty("span")
                .GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var contractMetrics = operations
            .SelectMany(MetricNames)
            .ToHashSet(StringComparer.Ordinal);

        var publicConstants = typeof(MySqlDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

        var publicSpans = publicConstants
            .Where(pair => pair.Key.EndsWith("SpanName", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToHashSet(StringComparer.Ordinal);

        var publicMetrics = publicConstants
            .Where(pair => pair.Key.EndsWith("MetricName", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            operations.Count,
            operations
                .Select(OperationId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(operations.Count, contractSpans.Count);
        Assert.Equal(
            operations.Sum(operation => MetricNames(operation).Count),
            contractMetrics.Count);
        Assert.True(publicSpans.SetEquals(contractSpans));
        Assert.True(publicMetrics.SetEquals(contractMetrics));
        Assert.Equal(
            MySqlDiagnostics.SourceName,
            root
                .GetProperty("sources")
                .GetProperty("provider")
                .GetString());
        Assert.Equal(
            MySqlDiagnostics.MySqlConnectorSourceName,
            root
                .GetProperty("sources")
                .GetProperty("driver")
                .GetString());
        Assert.Equal(
            MySqlDiagnostics.EfCoreDiagnosticSourceName,
            root
                .GetProperty("sources")
                .GetProperty("efCoreDiagnosticListener")
                .GetString());
        Assert.Equal(
            MySqlDiagnostics.DefaultDriverPoolName,
            root
                .GetProperty("privacy")
                .GetProperty("defaultDriverPoolName")
                .GetString());

        var privacy = root.GetProperty("privacy");

        AssertDomain(
            privacy.GetProperty("boundedLogFieldDomains"),
            "configurationFailureReason",
            Enum.GetNames<MySqlConfigurationFailureReason>());

        Assert.Equal(
            "SHA-256",
            privacy
                .GetProperty("objectScopeId")
                .GetProperty("algorithm")
                .GetString());
        Assert.Equal(
            16,
            privacy
                .GetProperty("objectScopeId")
                .GetProperty("hexCharacters")
                .GetInt32());
    }

    [Fact]
    public void Every_operational_event_has_exactly_one_log_trace_metric_owner()
    {
        using var contract = LoadContract();
        var operations = contract
            .RootElement.GetProperty("operations")
            .EnumerateArray()
            .ToList();

        var contractEventIds = operations
            .SelectMany(operation => operation
                .GetProperty("eventIds")
                .EnumerateArray())
            .Select(value => value.GetInt32())
            .ToList();

        var expectedOperationalEventIds = typeof(MySqlEventId)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(EventId))
            .Select(field => ((EventId)field.GetValue(null)!).Id)
            .Where(eventId => eventId is 1000 or 1005 or (>= 1100 and <= 1199) or (>= 1500 and <= 1599))
            .Order()
            .ToList();

        Assert.Equal(
            contractEventIds.Count,
            contractEventIds
                .Distinct()
                .Count());
        Assert.Equal(
            expectedOperationalEventIds,
            contractEventIds
                .Order()
                .ToList());

        foreach (var operation in operations)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(
                    operation
                        .GetProperty("span")
                        .GetString()));
            Assert.NotEmpty(
                operation
                    .GetProperty("eventIds")
                    .EnumerateArray()
                    .ToList());
            Assert.NotEmpty(MetricNames(operation));
        }
    }

    [Fact]
    public void Alerts_resolve_to_contract_signals_and_runbook_anchors()
    {
        using var contract = LoadContract();
        var root = contract.RootElement;
        var operations = root
            .GetProperty("operations")
            .EnumerateArray()
            .ToList();

        var alerts = root
            .GetProperty("alerts")
            .EnumerateArray()
            .ToList();

        var alertIds = alerts
            .Select(alert => alert
                .GetProperty("id")
                .GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var operationsRoot = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "operations");

        var runbooks = string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(operationsRoot, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.Equal(alerts.Count, alertIds.Count);

        foreach (var alert in alerts)
        {
            var anchor = alert
                .GetProperty("runbookAnchor")
                .GetString()!;

            Assert.Contains($"<a id=\"{anchor}\"></a>", runbooks, StringComparison.Ordinal);
            Assert.False(
                string.IsNullOrWhiteSpace(
                    alert
                        .GetProperty("condition")
                        .GetString()));
            Assert.Contains(
                alert
                    .GetProperty("severity")
                    .GetString()!,
                s_alertSeverities);
        }

        foreach (var operation in operations.Where(operation => operation.TryGetProperty("alert", out _)))
        {
            var alertId = operation
                .GetProperty("alert")
                .GetString()!;

            var anchor = operation
                .GetProperty("runbookAnchor")
                .GetString()!;

            Assert.Contains(alertId, alertIds);
            Assert.Contains(
                alerts,
                alert => alert
                        .GetProperty("id")
                        .GetString()
                    == alertId
                    && alert
                        .GetProperty("runbookAnchor")
                        .GetString()
                    == anchor);
        }
    }

    [Fact]
    public void Metric_tag_domains_are_finite_and_match_runtime_enums()
    {
        using var contract = LoadContract();
        var operations = contract
            .RootElement.GetProperty("operations")
            .EnumerateArray()
            .ToList();

        var serverDomains = operations
            .Single(operation => OperationId(operation) == "server-version-resolution")
            .GetProperty("metricTagDomains");

        var lockDomains = operations
            .Single(operation => OperationId(operation) == "migration-lock-acquire")
            .GetProperty("metricTagDomains");

        var retryDomains = operations
            .Single(operation => OperationId(operation) == "retry-attempt")
            .GetProperty("metricTagDomains");

        var cancellationDomains = operations
            .Single(operation => OperationId(operation) == "command-cancellation")
            .GetProperty("metricTagDomains");

        foreach (var operation in operations)
        {
            AssertDomain(
                operation.GetProperty("metricTagDomains"),
                MySqlDiagnosticTags.Engine,
                MySqlDiagnosticTags.MariaDb,
                MySqlDiagnosticTags.MySql);
        }

        var handlerOperation = operations
            .Single(operation => OperationId(operation) == "migration-operation-handler");

        var handlerBounds = handlerOperation.GetProperty("metricTagBounds");

        Assert.True(handlerBounds.TryGetProperty(MySqlDiagnosticTags.MigrationHandlerId, out _));
        Assert.True(handlerBounds.TryGetProperty(MySqlDiagnosticTags.MigrationHandlerOutcome, out _));
        Assert.True(handlerBounds.TryGetProperty(MySqlDiagnosticTags.MigrationOperationType, out _));
        Assert.True(handlerBounds.TryGetProperty(MySqlDiagnosticTags.ErrorType, out _));

        AssertDomain(serverDomains, "support_status", Enum.GetNames<MySqlServerVersionSupportStatus>());
        AssertDomain(serverDomains, "compatibility_mode", Enum.GetNames<MySqlServerVersionCompatibilityMode>());
        AssertDomain(
            lockDomains,
            "outcome",
            MySqlDiagnosticTags.Acquired,
            MySqlDiagnosticTags.Failed,
            MySqlDiagnosticTags.Timeout);
        AssertDomain(retryDomains, "outcome", MySqlDiagnosticTags.Attempt);
        AssertDomain(cancellationDomains, "path", MySqlDiagnosticTags.Hard, MySqlDiagnosticTags.Soft);
    }

    [Fact]
    public void Provider_spans_obey_the_bounded_privacy_safe_tag_contract()
    {
        using var contract = LoadContract();
        using var sink = new ActivitySink();
        using var rootActivity = new Activity("observability-tag-contract").Start();
        var secret = "password=secret;database=tenant-42;SELECT secret_column";
        var privateData = "tenant=private_tenant";
        var exception = new InvalidOperationException(secret) { Data = { ["private-context"] = privateData } };

        using (MySqlActivitySource.StartMigrationLockAcquire(EngineFamily.MySql)) { }

        using (MySqlActivitySource.StartMigrationLockReleaseFailed(EngineFamily.MySql, exception)) { }

        using (MySqlActivitySource.StartRetryAttempt(2, EngineFamily.MySql)) { }

        using (MySqlActivitySource.StartRetryLimitExceeded(EngineFamily.MySql, exception)) { }

        using (MySqlActivitySource.StartCancellation(
                   MySqlDiagnosticTags.Hard,
                   "Broken",
                   EngineFamily.MySql,
                   exception)) { }

        using (MySqlActivitySource.StartCommandTimeout("Open", EngineFamily.MySql, exception)) { }

        using (MySqlActivitySource.StartCommitUnknown("Closed", EngineFamily.MySql, exception)) { }

        using (MySqlActivitySource.StartServerVersionResolve(EngineFamily.MariaDb)) { }

        using (var handlerActivity = MySqlActivitySource.StartMigrationOperationHandler(
                   "tests.handler",
                   "Tests.CustomOperation",
                   "default",
                   EngineFamily.MySql))
        {
            handlerActivity?.SetTag(MySqlDiagnosticTags.MigrationHandlerOutcome, "generated");
        }

        var tagContract = contract.RootElement.GetProperty("spanTagContract");
        var required = tagContract
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var allowed = required
            .Concat(
                tagContract
                    .GetProperty("conditionallyAllowed")
                    .EnumerateArray()
                    .Select(value => value.GetString()!))
            .ToHashSet(StringComparer.Ordinal);

        var forbidden = tagContract
            .GetProperty("forbidden")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var activities = sink
            .Activities.Where(activity => activity.TraceId == rootActivity.TraceId)
            .ToList();

        Assert.Equal(9, activities.Count);

        foreach (var activity in activities)
        {
            var tags = activity.TagObjects.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            Assert.Empty(tags.Keys.Except(allowed, StringComparer.Ordinal));
            Assert.Empty(tags.Keys.Intersect(forbidden, StringComparer.Ordinal));
            Assert.All(
                required,
                tag => Assert.True(tags.ContainsKey(tag), $"Span '{activity.OperationName}' misses '{tag}'."));
            Assert.DoesNotContain(
                tags.Values,
                value => value
                        ?.ToString()
                        ?.Contains(secret, StringComparison.Ordinal)
                    == true);
            Assert.DoesNotContain(
                tags.Values,
                value => value
                        ?.ToString()
                        ?.Contains(privateData, StringComparison.Ordinal)
                    == true);
        }

        Assert.Contains(
            activities,
            activity => Equals(activity.GetTagItem(MySqlDiagnosticTags.DatabaseSystem), MySqlDiagnosticTags.MariaDb));
    }

    [Fact]
    public void Failure_log_is_correlated_without_serializing_the_exception()
    {
        using var sink = new ActivitySink();
        using var rootActivity = new Activity("observability-log-contract").Start();
        var logSink = new TestLogSink();
        var logger = new TestLogger(MySqlLoggerCategory.Resilience, logSink);
        var exception = new InvalidOperationException("password=secret;SELECT private_data");

        using (var activity = MySqlActivitySource.StartCommitUnknown("Broken", EngineFamily.MySql, exception))
        {
            Assert.NotNull(activity);
            MySqlLoggerMessages.CommitUnknown(logger, Guid.NewGuid(), "Broken", exception);
        }

        var logEntry = Assert.Single(logSink.Entries);
        var providerActivity = Assert.Single(
            sink.Activities,
            activity => activity.TraceId == rootActivity.TraceId
                && activity.OperationName == MySqlDiagnostics.CommitUnknownSpanName);

        Assert.Equal(providerActivity.TraceId.ToString(), logEntry.TraceId);
        Assert.Equal(providerActivity.SpanId.ToString(), logEntry.SpanId);
        Assert.Null(logEntry.ExceptionType);
        Assert.DoesNotContain("password", logEntry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_data", logEntry.Message, StringComparison.Ordinal);
        Assert.Equal(
            exception.GetType()
                .FullName,
            providerActivity.GetTagItem(MySqlDiagnosticTags.ErrorType));
    }

    private static string OperationId(
        JsonElement operation
    ) => operation
        .GetProperty("id")
        .GetString()!;

    private static IReadOnlyList<string> MetricNames(
        JsonElement operation
    ) => operation
        .GetProperty("metrics")
        .EnumerateArray()
        .Select(value => value.GetString()!)
        .ToArray();

    private static void AssertDomain(
        JsonElement domains,
        string name,
        params string[] expectedValues
    )
    {
        var actual = domains
            .GetProperty(name)
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(actual.SetEquals(expectedValues));
    }

    private static JsonDocument LoadContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "docs", "operations", "observability-contract.json");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Doka.EntityFrameworkCore.MySql repository root.");
    }

    private sealed class ActivitySink : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivitySink()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MySqlDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentQueue<Activity> Activities { get; } = new();

        public void Dispose() => _listener.Dispose();
    }
}
