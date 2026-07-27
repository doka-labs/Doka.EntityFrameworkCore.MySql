using System.IO;
using System.Reflection;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification;

/// <summary>
/// Enforces the provider-completeness contract from ADR D-021 without requiring a live
/// database. The tests reconcile executable skips with the machine-readable disposition
/// ledger and reject undocumented or provider-owned gaps.
/// </summary>
public class SpecDispositionContractTests
{
    private static readonly string[] s_activeClassifications =
    [
        "engine-limitation",
        "framework-limitation",
        "not-applicable",
    ];

    private static readonly string[] s_supportedSuites =
    [
        "migrations",
        "query-json",
    ];

    private static readonly string[] s_officialDatabaseVendorHosts =
    [
        "bugs.mysql.com",
        "dev.mysql.com",
        "mariadb.com",
    ];

    private static readonly string[] s_officialEfCoreIssueHosts =
    [
        "github.com",
    ];

    /// <summary>
    /// Verifies schema invariants, a zero provider-gap budget, and complete primary-source
    /// evidence for every engine or upstream-framework restriction recorded in the ledger.
    /// </summary>
    [Fact]
    public void Ledger_has_zero_provider_gaps_and_complete_primary_source_evidence()
    {
        using var document = LoadLedger();
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0, root.GetProperty("policy").GetProperty("providerGapBudget").GetInt32());
        Assert.False(root.GetProperty("policy").GetProperty("silentPassesPermitted").GetBoolean());

        var supportedTargets = StringValues(root.GetProperty("supportedTargets"));
        Assert.NotEmpty(supportedTargets);
        Assert.Equal(supportedTargets.Length, supportedTargets.Distinct(StringComparer.Ordinal).Count());

        var activeDispositions = root.GetProperty("activeDispositions").EnumerateArray().ToArray();
        var activeIds = activeDispositions
            .Select(disposition => RequiredString(disposition, "id"))
            .ToArray();
        Assert.Equal(activeIds.Length, activeIds.Distinct(StringComparer.Ordinal).Count());

        var activeProviderGaps = activeDispositions
            .Where(disposition => RequiredString(disposition, "classification") == "provider-gap")
            .Select(disposition => RequiredString(disposition, "id"));
        Assert.Empty(activeProviderGaps);

        var documentedMethods = activeDispositions
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .ToArray();
        Assert.Equal(
            documentedMethods.Length,
            documentedMethods.Distinct(StringComparer.Ordinal).Count());
        var documentedTestIds = activeDispositions
            .SelectMany(disposition => StringValues(disposition.GetProperty("discoveredTestIds")))
            .ToArray();
        Assert.Equal(
            documentedTestIds.Length,
            documentedTestIds.Distinct(StringComparer.Ordinal).Count());

        foreach (var disposition in activeDispositions)
        {
            var classification = RequiredString(disposition, "classification");
            Assert.Contains(classification, s_activeClassifications);
            Assert.Contains(
                RequiredString(disposition, "suite"),
                s_supportedSuites);
            var fixture = RequiredString(disposition, "fixture");

            var targets = StringValues(disposition.GetProperty("targets"));
            Assert.NotEmpty(targets);
            Assert.All(targets, target => Assert.Contains(target, supportedTargets));
            Assert.NotEmpty(StringValues(disposition.GetProperty("testMethods")));
            var discoveredTestIds = StringValues(
                disposition.GetProperty("discoveredTestIds"));
            Assert.NotEmpty(discoveredTestIds);
            Assert.Equal(
                discoveredTestIds.Length,
                discoveredTestIds.Distinct(StringComparer.Ordinal).Count());
            Assert.All(
                discoveredTestIds,
                testId => Assert.StartsWith(
                    $"{fixture}.",
                    testId,
                    StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(disposition, "reevaluateWhen")));

            if (classification == "not-applicable")
            {
                Assert.False(string.IsNullOrWhiteSpace(RequiredString(disposition, "rationale")));
                continue;
            }

            if (classification == "engine-limitation")
            {
                ValidatePrimarySources(disposition, s_officialDatabaseVendorHosts);
                Assert.False(string.IsNullOrWhiteSpace(
                    RequiredString(disposition, "providerWorkaroundAssessment")));
                ValidateProbe(
                    disposition,
                    targets,
                    "DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true");
                continue;
            }

            Assert.Equal("framework-limitation", classification);
            ValidatePrimarySources(disposition, s_officialEfCoreIssueHosts);
            Assert.All(
                disposition.GetProperty("primarySources").EnumerateArray(),
                source => Assert.StartsWith(
                    "/dotnet/efcore/issues/",
                    new Uri(RequiredString(source, "url"), UriKind.Absolute).AbsolutePath,
                    StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(
                RequiredString(disposition, "frameworkBoundaryAssessment")));
            Assert.Equal(
                supportedTargets.OrderBy(value => value),
                targets.OrderBy(value => value));
            ValidateProbe(
                disposition,
                targets,
                "DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS=true");
        }

        foreach (var workaround in root.GetProperty("providerWorkarounds").EnumerateArray())
        {
            Assert.Equal("implemented", RequiredString(workaround, "status"));
            Assert.NotEmpty(StringValues(workaround.GetProperty("targets")));
            Assert.NotEmpty(StringValues(workaround.GetProperty("testMethods")));
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(workaround, "implementation")));
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(workaround, "verification")));
            ValidatePrimarySources(workaround, s_officialDatabaseVendorHosts);
        }
    }

    /// <summary>
    /// Reconciles every disposition with every supported target in both patch-bound
    /// discovery contracts so a new Theory row or renamed test cannot inherit an old waiver.
    /// </summary>
    [Fact]
    public void Disposition_ids_match_every_version_bound_discovery_contract()
    {
        using var ledger = LoadLedger();
        var dispositions = ledger.RootElement
            .GetProperty("activeDispositions")
            .EnumerateArray()
            .ToArray();
        var contractsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests",
            "Specification",
            "Contracts");
        var contractPaths = Directory
            .EnumerateFiles(contractsDirectory, "SpecDiscovery.*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(contractPaths);

        foreach (var contractPath in contractPaths)
        {
            using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
            foreach (var target in contract.RootElement
                         .GetProperty("targets")
                         .EnumerateArray())
            {
                var targetName = RequiredString(target, "target");
                var targetTestIds = StringValues(target.GetProperty("testIds"));
                foreach (var disposition in dispositions.Where(
                             item => StringValues(item.GetProperty("targets")).Contains(
                                 targetName,
                                 StringComparer.Ordinal)))
                {
                    var fixture = RequiredString(disposition, "fixture");
                    var methodNames = StringValues(disposition.GetProperty("testMethods"))
                        .Select(MethodName)
                        .ToArray();
                    var expected = targetTestIds
                        .Where(testId =>
                            testId.StartsWith($"{fixture}.", StringComparison.Ordinal)
                            && methodNames.Any(method => IsMethodDisplayId(testId, method)))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();
                    var actual = StringValues(disposition.GetProperty("discoveredTestIds"))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();

                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    /// <summary>
    /// Reconciles every executable engine skip, upstream-framework skip, and explicit
    /// not-applicable skip against its exact ledger ID, method name, and target set.
    /// </summary>
    [Fact]
    public void Executable_skips_match_the_disposition_ledger()
    {
        using var document = LoadLedger();
        var dispositions = document.RootElement
            .GetProperty("activeDispositions")
            .EnumerateArray()
            .ToArray();

        var engineDispositions = dispositions
            .Where(disposition => RequiredString(disposition, "classification") == "engine-limitation")
            .ToDictionary(
                disposition => RequiredString(disposition, "id"),
                disposition => disposition,
                StringComparer.Ordinal);
        var frameworkDispositions = dispositions
            .Where(disposition =>
                RequiredString(disposition, "classification") == "framework-limitation")
            .ToDictionary(
                disposition => RequiredString(disposition, "id"),
                disposition => disposition,
                StringComparer.Ordinal);
        var notApplicableDispositions = dispositions
            .Where(disposition => RequiredString(disposition, "classification") == "not-applicable")
            .ToArray();

        var methods = SpecificationMethods();
        var executableEngineSkips = methods
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<SpecEngineLimitationTheoryAttribute>(
                    inherit: false),
            })
            .Where(item => item.Attribute is not null)
            .ToArray();

        var actualEngineMethods = new List<string>();
        foreach (var item in executableEngineSkips)
        {
            var attribute = item.Attribute!;
            Assert.True(
                engineDispositions.TryGetValue(attribute.DispositionId, out var disposition),
                $"Engine disposition '{attribute.DispositionId}' is absent from the ledger.");

            var methodName = LedgerMethodName(item.Method);
            actualEngineMethods.Add(methodName);
            Assert.Contains(methodName, StringValues(disposition.GetProperty("testMethods")));
            Assert.Equal(
                StringValues(disposition.GetProperty("targets")).OrderBy(value => value),
                attribute.UnsupportedTargets.OrderBy(value => value));
        }

        var documentedEngineMethods = engineDispositions.Values
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(documentedEngineMethods, actualEngineMethods.OrderBy(value => value));

        var executableFrameworkSkips = methods
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<SpecFrameworkLimitationTheoryAttribute>(
                    inherit: false),
            })
            .Where(item => item.Attribute is not null)
            .ToArray();

        var actualFrameworkMethods = new List<string>();
        foreach (var item in executableFrameworkSkips)
        {
            var attribute = item.Attribute!;
            Assert.True(
                frameworkDispositions.TryGetValue(attribute.DispositionId, out var disposition),
                $"Framework disposition '{attribute.DispositionId}' is absent from the ledger.");

            var methodName = LedgerMethodName(item.Method);
            actualFrameworkMethods.Add(methodName);
            Assert.Contains(methodName, StringValues(disposition.GetProperty("testMethods")));
        }

        var documentedFrameworkMethods = frameworkDispositions.Values
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(
            documentedFrameworkMethods,
            actualFrameworkMethods.OrderBy(value => value));

        var explicitSkippedFacts = methods
            .Select(method => new
            {
                Method = method,
                Attribute = method
                    .GetCustomAttributes<FactAttribute>(inherit: false)
                    .SingleOrDefault(attribute =>
                        attribute.GetType() == typeof(FactAttribute)
                        && !string.IsNullOrWhiteSpace(attribute.Skip)),
            })
            .Where(item => item.Attribute is not null)
            .ToArray();

        var actualNotApplicableMethods = new List<string>();
        foreach (var item in explicitSkippedFacts)
        {
            var methodName = LedgerMethodName(item.Method);
            actualNotApplicableMethods.Add(methodName);

            var matchingDisposition = Assert.Single(
                notApplicableDispositions,
                disposition =>
                    StringValues(disposition.GetProperty("testMethods")).Contains(
                        methodName,
                        StringComparer.Ordinal));
            var dispositionId = RequiredString(matchingDisposition, "id");
            Assert.Contains(
                $"[spec-not-applicable:{dispositionId}]",
                item.Attribute!.Skip!,
                StringComparison.Ordinal);
        }

        var documentedNotApplicableMethods = notApplicableDispositions
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(
            documentedNotApplicableMethods,
            actualNotApplicableMethods.OrderBy(value => value));
    }

    /// <summary>
    /// Guards against the silent-pass mechanisms that previously allowed an inherited
    /// specification assertion to be reported as successful without executing.
    /// </summary>
    [Fact]
    public void Specification_sources_contain_no_silent_pass_mechanisms()
    {
        var specificationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests",
            "Specification");
        var forbiddenFragments = new[]
        {
            "DOKA_SPEC_TEST_PROBE_" + "EXEMPTIONS",
            "Skip" + "Exception",
            "Task." + "CompletedTask",
        };

        foreach (var sourcePath in Directory.EnumerateFiles(
                     specificationDirectory,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourcePath);
            foreach (var forbiddenFragment in forbiddenFragments)
            {
                Assert.DoesNotContain(forbiddenFragment, source, StringComparison.Ordinal);
            }
        }
    }

    private static JsonDocument LoadLedger()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Specification",
            "SpecDispositions.json");
        Assert.True(File.Exists(path), $"Specification disposition ledger not found at '{path}'.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static MethodInfo[] SpecificationMethods() =>
    [
        .. typeof(SpecDispositionContractTests)
            .Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && type.Namespace?.StartsWith(
                    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification",
                    StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly)),
    ];

    private static string LedgerMethodName(
        MethodInfo method
    ) => $"{method.DeclaringType!.Name}.{method.Name}";

    private static string MethodName(
        string ledgerMethodName
    ) => ledgerMethodName[(ledgerMethodName.IndexOf('.', StringComparison.Ordinal) + 1)..];

    private static bool IsMethodDisplayId(
        string testId,
        string methodName
    )
    {
        var marker = $".{methodName}";
        var methodStart = testId.LastIndexOf(marker, StringComparison.Ordinal);
        if (methodStart < 0)
        {
            return false;
        }

        var suffix = methodStart + marker.Length;
        return suffix == testId.Length || testId[suffix] == '(';
    }

    private static string[] StringValues(
        JsonElement array
    ) =>
    [
        .. array
            .EnumerateArray()
            .Select(element => element.GetString()!),
    ];

    private static string RequiredString(
        JsonElement element,
        string propertyName
    )
    {
        var value = element.GetProperty(propertyName).GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value;
    }

    private static void ValidatePrimarySources(
        JsonElement disposition,
        IReadOnlyCollection<string> allowedHosts
    )
    {
        var dispositionId = RequiredString(disposition, "id");
        var sources = disposition.GetProperty("primarySources").EnumerateArray().ToArray();
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(source, "publisher")));
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(source, "title")));
            Assert.False(string.IsNullOrWhiteSpace(RequiredString(source, "supports")));

            var url = new Uri(RequiredString(source, "url"), UriKind.Absolute);
            Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
            Assert.Contains(url.Host, allowedHosts);

            var retrievedAtText = RequiredString(source, "retrievedAt");
            Assert.True(
                DateOnly.TryParseExact(
                    retrievedAtText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var retrievedAt),
                $"Disposition '{dispositionId}' has invalid retrievedAt '{retrievedAtText}'.");
            Assert.True(
                retrievedAt <= DateOnly.FromDateTime(DateTime.UtcNow),
                $"Disposition '{dispositionId}' has a future retrieval date.");
        }
    }

    private static void ValidateProbe(
        JsonElement disposition,
        IReadOnlyCollection<string> targets,
        string requiredEnvironmentSetting
    )
    {
        var probe = disposition.GetProperty("probe");
        Assert.False(string.IsNullOrWhiteSpace(RequiredString(probe, "performedAt")));
        Assert.Contains(
            requiredEnvironmentSetting,
            RequiredString(probe, "command"),
            StringComparison.Ordinal);

        var observedTargets = probe
            .GetProperty("observations")
            .EnumerateArray()
            .Select(observation =>
            {
                Assert.False(string.IsNullOrWhiteSpace(RequiredString(observation, "result")));
                return RequiredString(observation, "target");
            })
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(targets.OrderBy(value => value), observedTargets);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Doka.EntityFrameworkCore.MySql.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the functional-test output path.");
    }
}
