namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

internal static class PerformanceGate
{
    internal const int InvalidEvidenceExitCode = 78;

    public static int Run(
        string contractPath,
        string reportsDirectory,
        string target,
        string profile,
        string? soakPath
    )
    {
        var result = Evaluate(contractPath, reportsDirectory, target, profile, soakPath);
        foreach (var error in result.InvalidEvidence)
        {
            Console.Error.WriteLine($"INVALID: {error}");
        }

        foreach (var regression in result.Regressions)
        {
            Console.Error.WriteLine($"REGRESSION: {regression}");
        }

        if (result.InvalidEvidence.Count > 0)
        {
            Console.Error.WriteLine("Performance evidence is invalid.");
            return InvalidEvidenceExitCode;
        }

        if (result.Regressions.Count > 0)
        {
            Console.Error.WriteLine("Performance budgets regressed.");
            return 1;
        }

        Console.WriteLine(
            $"Performance gate passed for {target}/{profile}: "
            + $"{result.WorkloadCount} workloads and {result.ControlCount} controls.");
        return 0;
    }

    public static PerformanceGateResult Evaluate(
        string contractPath,
        string reportsDirectory,
        string target,
        string profile,
        string? soakPath
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        using var contractDocument = JsonDocument.Parse(File.ReadAllText(contractPath));
        var contract = contractDocument.RootElement;
        var invalid = new List<string>();
        var regressions = new List<string>();
        if (RequiredInt(contract, "schemaVersion") != 11)
        {
            invalid.Add("Performance contract schemaVersion must be 11.");
        }

        var contractVersion = RequiredString(contract, "contractVersion");
        var targets = contract.GetProperty("requiredTargets");
        if (!targets.TryGetProperty(target, out _))
        {
            invalid.Add($"Performance contract does not define target '{target}'.");
        }

        var profiles = contract.GetProperty("profiles");
        if (!profiles.TryGetProperty(profile, out var profileContract))
        {
            invalid.Add($"Performance contract does not define profile '{profile}'.");
            return new PerformanceGateResult(0, 0, invalid, regressions);
        }

        var definitions = contract
            .GetProperty("workloads")
            .EnumerateArray()
            .Select(ReadWorkloadDefinition)
            .Where(definition => profile != BenchmarkProfiles.SmokeProfile || definition.Smoke)
            .ToArray();

        if (definitions.Length == 0)
        {
            invalid.Add($"Performance profile '{profile}' selects no workloads.");
        }

        var duplicateDefinitions = definitions
            .GroupBy(static definition => definition.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var duplicate in duplicateDefinitions)
        {
            invalid.Add($"Performance contract repeats workload '{duplicate}'.");
        }

        var reports = Directory.Exists(reportsDirectory)
            ? Directory.GetFiles(reportsDirectory, "*-report-full.json", SearchOption.AllDirectories)
            : [];
        if (reports.Length == 0)
        {
            invalid.Add($"No BenchmarkDotNet full JSON report exists below '{reportsDirectory}'.");
            return new PerformanceGateResult(0, 0, invalid, regressions);
        }

        var observations = new List<BenchmarkObservation>();
        string? hostIdentity = null;
        foreach (var report in reports.Order(StringComparer.Ordinal))
        {
            using var reportDocument = JsonDocument.Parse(File.ReadAllText(report));
            var root = reportDocument.RootElement;
            var reportHostIdentity = ReadHostIdentity(root.GetProperty("HostEnvironmentInfo"), report, invalid);
            if (hostIdentity is null)
            {
                hostIdentity = reportHostIdentity;
            }
            else if (!string.Equals(hostIdentity, reportHostIdentity, StringComparison.Ordinal))
            {
                invalid.Add($"BenchmarkDotNet report '{report}' was produced by another host environment.");
            }

            foreach (var benchmark in root
                         .GetProperty("Benchmarks")
                         .EnumerateArray())
            {
                observations.Add(ReadObservation(benchmark, report));
            }
        }

        ValidateWorkloads(
            definitions,
            contract.GetProperty("familyBudgets"),
            observations,
            target,
            invalid,
            regressions);
        var controlCount = ValidateControls(
            contract.GetProperty("benchmarkDotNetControls"),
            targets,
            observations,
            target,
            invalid,
            regressions);

        var soakRequired = profileContract
            .GetProperty("soakRequired")
            .GetBoolean();

        if (soakRequired && string.IsNullOrWhiteSpace(soakPath))
        {
            invalid.Add($"Performance profile '{profile}' requires soak evidence.");
        }
        else if (!string.IsNullOrWhiteSpace(soakPath))
        {
            ValidateSoak(
                soakPath,
                contractVersion,
                target,
                profile,
                contract.GetProperty("soakBudgets"),
                invalid,
                regressions);
        }

        return new PerformanceGateResult(definitions.Length, controlCount, invalid, regressions);
    }

    private static void ValidateWorkloads(
        IReadOnlyList<WorkloadDefinition> definitions,
        JsonElement familyBudgets,
        IReadOnlyList<BenchmarkObservation> observations,
        string target,
        List<string> invalid,
        List<string> regressions
    )
    {
        var workloadObservations = observations
            .Where(static observation => observation is
            {
                Type: nameof(ProviderWorkloadBenchmarks), Method: nameof(ProviderWorkloadBenchmarks.Execute)
            })
            .ToArray();

        var byId = new Dictionary<string, BenchmarkObservation>(StringComparer.Ordinal);
        foreach (var observation in workloadObservations)
        {
            var actualTarget = ReadQuotedParameter(observation.FullName, nameof(ProviderWorkloadBenchmarks.Target));
            if (!string.Equals(actualTarget, target, StringComparison.Ordinal))
            {
                invalid.Add(
                    $"Provider workload report carries target '{actualTarget ?? "<missing>"}', "
                    + $"expected '{target}'.");
            }

            var workloadId = ReadQuotedParameter(observation.FullName, nameof(ProviderWorkloadBenchmarks.WorkloadId));
            if (workloadId is null)
            {
                invalid.Add("Provider workload report omits its WorkloadId parameter.");
                continue;
            }

            if (!byId.TryAdd(workloadId, observation))
            {
                invalid.Add($"BenchmarkDotNet reports workload '{workloadId}' more than once.");
            }
        }

        var expectedIds = definitions
            .Select(static definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var unexpected in byId.Keys.Except(expectedIds, StringComparer.Ordinal))
        {
            invalid.Add($"BenchmarkDotNet reports undeclared workload '{unexpected}'.");
        }

        foreach (var definition in definitions)
        {
            if (!byId.TryGetValue(definition.Id, out var observation))
            {
                invalid.Add($"BenchmarkDotNet report is missing workload '{definition.Id}'.");
                continue;
            }

            if (definition.OperationsPerInvoke <= 0)
            {
                invalid.Add($"Workload '{definition.Id}' has no positive operation batch.");
                continue;
            }

            if (!familyBudgets.TryGetProperty(definition.Family, out var budget))
            {
                invalid.Add($"Workload '{definition.Id}' references unknown family '{definition.Family}'.");
                continue;
            }

            var sorted = observation
                .OriginalValues
                .Order()
                .ToArray();

            var median = Percentile(sorted, 0.5) / definition.OperationsPerInvoke;
            var p95 = Percentile(sorted, 0.95) / definition.OperationsPerInvoke;
            var p99 = Percentile(sorted, 0.99) / definition.OperationsPerInvoke;
            var allocated = observation.AllocatedBytes / definition.OperationsPerInvoke;
            AddMaximumRegression(
                definition.Id,
                "medianNanoseconds",
                median,
                RequiredDouble(budget, "medianNanoseconds"),
                regressions);
            AddMaximumRegression(
                definition.Id,
                "p95Nanoseconds",
                p95,
                RequiredDouble(budget, "p95Nanoseconds"),
                regressions);
            AddMaximumRegression(
                definition.Id,
                "p99Nanoseconds",
                p99,
                RequiredDouble(budget, "p99Nanoseconds"),
                regressions);
            AddMaximumRegression(
                definition.Id,
                "allocatedBytes",
                allocated,
                RequiredDouble(budget, "allocatedBytes"),
                regressions);
            AddMaximumRegression(
                definition.Id,
                "gen2CollectionsPer1000",
                observation.Gen2Collections,
                RequiredDouble(budget, "gen2CollectionsPer1000"),
                regressions);
        }
    }

    private static int ValidateControls(
        JsonElement controls,
        JsonElement requiredTargets,
        IReadOnlyList<BenchmarkObservation> observations,
        string target,
        List<string> invalid,
        List<string> regressions
    )
    {
        var count = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls.EnumerateArray())
        {
            count++;
            var id = RequiredString(control, "id");
            if (!ids.Add(id))
            {
                invalid.Add($"Performance contract repeats BenchmarkDotNet control '{id}'.");
                continue;
            }

            var maximum = ReadControlMaximum(control, requiredTargets, target, id, invalid);
            var type = RequiredString(control, "type");
            var method = RequiredString(control, "method");
            var measured = SelectSingleObservation(observations, type, method, id, invalid);
            if (measured is null || maximum is null)
            {
                continue;
            }

            var metric = RequiredString(control, "metric");
            double actual;
            switch (metric)
            {
                case "allocatedBytes":
                    actual = measured.AllocatedBytes;
                    break;
                case "meanRatio":
                case "allocationRatio":
                    var baselineMethod = RequiredString(control, "baselineMethod");
                    var baseline = SelectSingleObservation(observations, type, baselineMethod, id, invalid);
                    if (baseline is null)
                    {
                        continue;
                    }

                    var numerator = metric == "meanRatio" ? measured.Mean : measured.AllocatedBytes;
                    var denominator = metric == "meanRatio" ? baseline.Mean : baseline.AllocatedBytes;
                    if (denominator == 0)
                    {
                        if (numerator != 0)
                        {
                            invalid.Add($"Control '{id}' has a zero baseline for non-zero evidence.");
                            continue;
                        }

                        actual = 0;
                    }
                    else
                    {
                        actual = numerator / denominator;
                    }

                    break;
                default:
                    invalid.Add($"Control '{id}' declares unsupported metric '{metric}'.");
                    continue;
            }

            AddMaximumRegression(id, metric, actual, maximum.Value, regressions);
        }

        return count;
    }

    private static double? ReadControlMaximum(
        JsonElement control,
        JsonElement requiredTargets,
        string target,
        string id,
        List<string> invalid
    )
    {
        var maximum = default(JsonProperty);
        var maximumCount = 0;
        foreach (var property in control.EnumerateObject())
        {
            if (property.Name is "maximum" or "maximumByTarget")
            {
                maximum = property;
                maximumCount++;
            }
        }

        if (maximumCount != 1)
        {
            invalid.Add($"Control '{id}' requires exactly one maximum or maximumByTarget property.");
            return null;
        }

        if (maximum.NameEquals("maximum"))
        {
            return ReadMaximum(maximum.Value, id, "maximum", invalid);
        }

        if (maximum.Value.ValueKind != JsonValueKind.Object)
        {
            invalid.Add($"Control '{id}' maximumByTarget must be an object.");
            return null;
        }

        var initialErrorCount = invalid.Count;
        var targetNames = new HashSet<string>(StringComparer.Ordinal);
        double? selectedMaximum = null;
        foreach (var entry in maximum.Value.EnumerateObject())
        {
            if (!targetNames.Add(entry.Name))
            {
                invalid.Add($"Control '{id}' maximumByTarget repeats target '{entry.Name}'.");
            }

            if (!requiredTargets.TryGetProperty(entry.Name, out _))
            {
                invalid.Add($"Control '{id}' maximumByTarget declares unknown target '{entry.Name}'.");
            }

            var value = ReadMaximum(entry.Value, id, $"maximumByTarget/{entry.Name}", invalid);
            if (entry.NameEquals(target))
            {
                selectedMaximum = value;
            }
        }

        foreach (var requiredTarget in requiredTargets.EnumerateObject())
        {
            if (!targetNames.Contains(requiredTarget.Name))
            {
                invalid.Add($"Control '{id}' maximumByTarget is missing target '{requiredTarget.Name}'.");
            }
        }

        return invalid.Count == initialErrorCount ? selectedMaximum : null;
    }

    private static double? ReadMaximum(
        JsonElement element,
        string id,
        string property,
        List<string> invalid
    )
    {
        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var maximum)
            && double.IsFinite(maximum)
            && maximum >= 0)
        {
            return maximum;
        }

        invalid.Add($"Control '{id}' {property} must be a finite nonnegative number.");
        return null;
    }

    private static void ValidateSoak(
        string soakPath,
        string contractVersion,
        string target,
        string profile,
        JsonElement budgets,
        List<string> invalid,
        List<string> regressions
    )
    {
        var report = PerformanceReportWriter.Read<SoakRunReport>(soakPath);
        if (report.SchemaVersion != 2
            || report.Kind != "performance-soak"
            || report.ContractVersion != contractVersion
            || report.Target != target
            || report.Profile != profile)
        {
            invalid.Add("Soak evidence does not match the current performance identity.");
            return;
        }

        var expected = new Dictionary<string, SoakRequirement[]>(StringComparer.Ordinal)
        {
            ["soak.hilo-cache-bound"] =
            [
                new SoakRequirement("cacheEntries", RequiredDouble(budgets, "hiloCacheMaximumEntries"), false),
            ],
            ["soak.pooled-buffer-return"] =
            [
                new SoakRequirement(
                    "outstandingBuffers",
                    RequiredDouble(budgets, "pooledBufferMaximumOutstanding"),
                    false),
            ],
            ["soak.connection-cleanup"] =
            [
                new SoakRequirement(
                    "connectionDelta",
                    RequiredDouble(budgets, "connectionMaximumDelta"),
                    false),
            ],
            ["soak.migration-lock-cleanup"] =
            [
                new SoakRequirement("heldLocks", RequiredDouble(budgets, "migrationLockMaximumHeld"), false),
            ],
            ["soak.working-set-stabilization"] =
            [
                new SoakRequirement(
                    "workingSetGrowthBytes",
                    RequiredDouble(budgets, "workingSetMaximumGrowthBytes"),
                    false),
                new SoakRequirement(
                    "managedHeapGrowthBytes",
                    RequiredDouble(budgets, "managedHeapMaximumGrowthBytes"),
                    false),
            ],
            ["soak.concurrent-throughput-retention"] =
            [
                new SoakRequirement(
                    "throughputRetentionRatio",
                    RequiredDouble(budgets, "minimumThroughputRetentionRatio"),
                    true),
            ],
        };

        var scenarios = new Dictionary<string, SoakScenarioResult>(StringComparer.Ordinal);
        foreach (var scenario in report.Scenarios)
        {
            if (!scenarios.TryAdd(scenario.Id, scenario))
            {
                invalid.Add($"Soak evidence repeats scenario '{scenario.Id}'.");
            }
        }

        foreach (var unexpected in scenarios.Keys.Except(expected.Keys, StringComparer.Ordinal))
        {
            invalid.Add($"Soak evidence contains undeclared scenario '{unexpected}'.");
        }

        foreach (var (id, requirements) in expected)
        {
            if (!scenarios.TryGetValue(id, out var scenario))
            {
                invalid.Add($"Soak evidence is missing scenario '{id}'.");
                continue;
            }

            var complete = true;
            var scenarioRegressed = false;
            foreach (var requirement in requirements)
            {
                if (!scenario.Metrics.TryGetValue(requirement.Metric, out var actual))
                {
                    invalid.Add($"Soak evidence is missing '{id}/{requirement.Metric}'.");
                    complete = false;
                    continue;
                }

                if (!double.IsFinite(actual))
                {
                    invalid.Add($"Soak metric '{id}/{requirement.Metric}' is not finite.");
                    complete = false;
                    continue;
                }

                var failed = requirement.Minimum ? actual < requirement.Limit : actual > requirement.Limit;
                if (failed)
                {
                    scenarioRegressed = true;
                    regressions.Add(
                        $"{id} {requirement.Metric} is {actual:G17}; required "
                        + $"{(requirement.Minimum ? "minimum" : "maximum")} {requirement.Limit:G17}.");
                }
            }

            if (complete && scenario.Success == scenarioRegressed)
            {
                invalid.Add($"Soak scenario '{id}' success flag contradicts its raw metrics.");
            }
        }

        if (!report.Success
            && regressions.Count == 0)
        {
            invalid.Add("Soak evidence reports failure without a failing declared scenario.");
        }
    }

    private static BenchmarkObservation ReadObservation(
        JsonElement benchmark,
        string report
    )
    {
        var statistics = benchmark.GetProperty("Statistics");
        var values = statistics
            .GetProperty("OriginalValues")
            .EnumerateArray()
            .Select(static value => value.GetDouble())
            .ToArray();
        if (values.Length == 0
            || values.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw new InvalidDataException($"BenchmarkDotNet report '{report}' has no finite positive samples.");
        }

        var memory = benchmark.GetProperty("Memory");
        var sampleCount = statistics
            .GetProperty("N")
            .GetInt32();
        var reportedMean = RequiredDouble(statistics, "Mean");
        var recomputedMean = values.Average();
        var meanTolerance = 1e-9 * Math.Max(1, Math.Abs(recomputedMean));
        if (sampleCount != values.Length
            || !double.IsFinite(reportedMean)
            || reportedMean <= 0
            || Math.Abs(reportedMean - recomputedMean) > meanTolerance)
        {
            throw new InvalidDataException(
                $"BenchmarkDotNet report '{report}' carries statistics that " + "do not match its raw samples.");
        }

        var observation = new BenchmarkObservation(
            RequiredString(benchmark, "Type"),
            RequiredString(benchmark, "Method"),
            RequiredString(benchmark, "FullName"),
            sampleCount,
            recomputedMean,
            values,
            RequiredDouble(memory, "BytesAllocatedPerOperation"),
            RequiredDouble(memory, "Gen2Collections"));
        if (observation.SampleCount <= 0
            || !double.IsFinite(observation.AllocatedBytes)
            || observation.AllocatedBytes < 0
            || !double.IsFinite(observation.Gen2Collections)
            || observation.Gen2Collections < 0)
        {
            throw new InvalidDataException($"BenchmarkDotNet report '{report}' contains invalid statistics.");
        }

        return observation;
    }

    private static string ReadHostIdentity(
        JsonElement host,
        string report,
        List<string> invalid
    )
    {
        if (host
            .GetProperty("HasAttachedDebugger")
            .GetBoolean())
        {
            invalid.Add($"BenchmarkDotNet report '{report}' was measured with an attached debugger.");
        }

        return string.Join(
            '|',
            RequiredString(host, "BenchmarkDotNetVersion"),
            RequiredString(host, "OsVersion"),
            RequiredString(host, "ProcessorName"),
            RequiredString(host, "RuntimeVersion"),
            RequiredString(host, "Architecture"),
            RequiredString(host, "Configuration"));
    }

    private static BenchmarkObservation? SelectSingleObservation(
        IReadOnlyList<BenchmarkObservation> observations,
        string type,
        string method,
        string controlId,
        List<string> invalid
    )
    {
        var matches = observations
            .Where(observation => observation.Type == type && observation.Method == method)
            .ToArray();
        if (matches.Length != 1)
        {
            invalid.Add($"Control '{controlId}' expected one {type}.{method} result, found {matches.Length}.");
            return null;
        }

        return matches[0];
    }

    private static string? ReadQuotedParameter(
        string fullName,
        string parameterName
    )
    {
        var prefix = $"{parameterName}: \"";
        var start = fullName.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        var end = fullName.IndexOf('"', start);
        return end < 0 ? null : fullName[start..end];
    }

    private static WorkloadDefinition ReadWorkloadDefinition(
        JsonElement definition
    ) => new(
        RequiredString(definition, "id"),
        RequiredString(definition, "family"),
        definition.TryGetProperty("smoke", out var smoke) && smoke.GetBoolean(),
        definition.TryGetProperty("operationsPerSample", out var operations) ? operations.GetInt32() : 1);

    private static void AddMaximumRegression(
        string id,
        string metric,
        double actual,
        double maximum,
        List<string> regressions
    )
    {
        if (!double.IsFinite(actual)
            || !double.IsFinite(maximum)
            || maximum < 0)
        {
            throw new InvalidDataException($"Performance metric '{id}/{metric}' is malformed.");
        }

        if (actual > maximum)
        {
            regressions.Add($"{id} {metric} is {actual:G17}; maximum {maximum:G17}.");
        }
    }

    private static double Percentile(
        double[] sortedValues,
        double percentile
    )
    {
        var position = (sortedValues.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * (position - lower));
    }

    private static int RequiredInt(
        JsonElement element,
        string property
    ) => element
        .GetProperty(property)
        .GetInt32();

    private static string RequiredString(
        JsonElement element,
        string property
    ) => element
            .GetProperty(property)
            .GetString()
        ?? throw new InvalidDataException($"Performance property '{property}' is null.");

    private static double RequiredDouble(
        JsonElement element,
        string property
    ) => element
        .GetProperty(property)
        .GetDouble();

    private sealed record WorkloadDefinition(
        string Id,
        string Family,
        bool Smoke,
        int OperationsPerInvoke
    );

    private sealed record BenchmarkObservation(
        string Type,
        string Method,
        string FullName,
        int SampleCount,
        double Mean,
        IReadOnlyList<double> OriginalValues,
        double AllocatedBytes,
        double Gen2Collections
    );

    private sealed record SoakRequirement(
        string Metric,
        double Limit,
        bool Minimum
    );
}

internal sealed record PerformanceGateResult(
    int WorkloadCount,
    int ControlCount,
    IReadOnlyList<string> InvalidEvidence,
    IReadOnlyList<string> Regressions
);
