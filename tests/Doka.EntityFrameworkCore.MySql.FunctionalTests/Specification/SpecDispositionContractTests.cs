using System.Reflection;
using System.Text.RegularExpressions;
using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification;

/// <summary>
/// Enforces the provider-completeness contract from ADR D-021 without requiring a live
/// database. The tests reconcile executable skips with the machine-readable disposition
/// ledger and reject undocumented or provider-owned gaps.
/// </summary>
[Trait("Category", "Spec")]
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
        "bulk-updates",
        "change-tracking",
        "migrations",
        "model-building",
        "query",
        "query-json",
    ];

    private static readonly string[] s_officialDatabaseVendorHosts =
    [
        "bugs.mysql.com",
        "dev.mysql.com",
        "mariadb.com",
    ];

    private static readonly string[] s_officialEfCoreHosts =
    [
        "github.com",
    ];

    private static readonly string[] s_publicBoundaryHeadings =
    [
        "General LINQ window-function API",
        "`StringComparison` overloads in translated queries",
        "Database-local relational schemas",
        "MariaDB CTE data modification",
        "Compiled-query wrappers for raw SQL and temporal roots",
        "MariaDB temporal structural migrations",
        "MySQL emulated-temporal cascade actions",
    ];

    /// <summary>
    /// Keeps discovery stable while refusing an implicit live execution target.
    /// </summary>
    [Fact]
    public void Missing_specification_target_defaults_to_mysql84()
    {
        Assert.Equal("mysql84", SpecTestTarget.Resolve(configuredTarget: null));
        _ = Assert.Throws<InvalidOperationException>(() => SpecTestTarget.ResolveForExecution(configuredTarget: null));
        _ = Assert.Throws<InvalidOperationException>(() => FunctionalDatabaseFixture.CreateRequest(
            configuredTarget: null,
            externalConnectionString: "Server=external",
            externalServerVersion: "mysql:8.4.11"));

        (string Target, string ServerVersion)[] supportedEndpoints =
        [
            ("mysql84", "mysql:8.4.11"),
            ("mysql97", "mysql:9.7.2"),
            ("mariadb1011", "mariadb:10.11.18"),
            ("mariadb114", "mariadb:11.4.12"),
            ("mariadb118", "mariadb:11.8.8"),
            ("mariadb123", "mariadb:12.3.2"),
        ];

        foreach (var (target, serverVersion) in supportedEndpoints)
        {
            var request = FunctionalDatabaseFixture.CreateRequest(target, "Server=external", serverVersion);

            Assert.Equal(target, request.TargetId);
            Assert.Equal(serverVersion, request.ServerVersionToken);
            Assert.Null(request.Image);
        }

        string[] mismatchedVersions =
        [
            "mariadb:11.4.12",
            "mysql:9.7.2",
        ];

        foreach (var mismatchedVersion in mismatchedVersions)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FunctionalDatabaseFixture.CreateRequest("mysql84", "Server=external", mismatchedVersion));

            Assert.Contains("does not match target 'mysql84'", exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Guards patch-version inheritance chains in which an intermediate override carries
    /// no xUnit rows and the authoritative theory data lives on a more distant base.
    /// </summary>
    [Fact]
    public void Inherited_theory_data_uses_the_nearest_data_bearing_base_declaration()
    {
        var method = typeof(Query.Associations.OwnedNavigationsProjectionMySqlTest).GetMethod(
            nameof(Query.Associations.OwnedNavigationsProjectionMySqlTest
                .Select_subquery_optional_related_FirstOrDefault))!;
        var attribute = method.GetCustomAttribute<InheritedTheoryDataAttribute>()!;

        Assert.NotEmpty(attribute.GetData(method));
    }

    /// <summary>
    /// Prevents an upstream <c>Skip</c> from entering the provider matrix without a
    /// provider-owned activation or an executable disposition.
    /// </summary>
    [Fact]
    public void Specification_types_do_not_inherit_upstream_skips()
    {
        using var ledger = LoadLedger();
        var dispositionedInheritedMethods = ledger
            .RootElement.GetProperty("activeDispositions")
            .EnumerateArray()
            .SelectMany(InheritedUpstreamMethods)
            .ToHashSet(StringComparer.Ordinal);
        var inheritedSkips = typeof(SpecDispositionContractTests)
            .Assembly.GetTypes()
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.Namespace?.StartsWith(
                    "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification",
                    StringComparison.Ordinal)
                == true)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.DeclaringType?.Assembly != typeof(SpecDispositionContractTests).Assembly)
                .Where(method => method
                    .GetCustomAttributes<FactAttribute>(inherit: false)
                    .Any(attribute => !string.IsNullOrWhiteSpace(attribute.Skip)))
                .Select(method => new
                {
                    ProviderType = type,
                    Method = method,
                }))
            .Where(item => !MatchesInheritedDisposition(item.ProviderType, item.Method, dispositionedInheritedMethods))
            .OrderBy(item => item.ProviderType.FullName, StringComparer.Ordinal)
            .ThenBy(item => item.Method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            inheritedSkips.Length == 0,
            "Provider specification types inherit upstream skips:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                inheritedSkips.Select(item => FormatInheritedSkip(item.ProviderType, item.Method))));
    }

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

            if (disposition.TryGetProperty("execution", out var execution))
            {
                Assert.Equal("inherited-upstream-skip", execution.GetString());
                Assert.Equal(
                    StringValues(disposition.GetProperty("testMethods")).OrderBy(value => value),
                    InheritedUpstreamMethods(disposition).OrderBy(value => value));
            }

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
            ValidatePrimarySources(disposition, s_officialEfCoreHosts);
            ValidateFrameworkEvidence(disposition);
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
    /// Keeps the public external-limitations inventories synchronized with every
    /// active engine and EF Core disposition while excluding structural
    /// non-applicability.
    /// </summary>
    [Fact]
    public void External_limitations_document_matches_active_external_dispositions()
    {
        using var ledger = LoadLedger();
        var documentationRoot = Path.Combine(FindRepositoryRoot(), "docs");

        var documentationPaths = new[]
        {
            Path.Combine(documentationRoot, "limitations.md"),
            Path.Combine(documentationRoot, "limitations", "database-engines.md"),
            Path.Combine(documentationRoot, "limitations", "ef-core.md"),
        };

        Assert.All(
            documentationPaths,
            documentationPath => Assert.True(
                File.Exists(documentationPath),
                $"External limitations document not found at '{documentationPath}'."));

        var documentation = string.Join(Environment.NewLine, documentationPaths.Select(File.ReadAllText));

        var dispositions = ledger
            .RootElement.GetProperty("activeDispositions")
            .EnumerateArray()
            .ToArray();

        foreach (var disposition in dispositions.Where(
                     item => RequiredString(item, "classification") is
                         "engine-limitation" or "framework-limitation"))
        {
            var id = RequiredString(disposition, "id");
            AssertDocumentedLimitation(documentation, $"### `{id}`");
        }

        foreach (var disposition in dispositions.Where(
                     item => RequiredString(item, "classification") == "not-applicable"))
        {
            var id = RequiredString(disposition, "id");
            Assert.DoesNotContain($"`{id}`", documentation, StringComparison.Ordinal);
        }

        foreach (var heading in s_publicBoundaryHeadings)
        {
            AssertDocumentedLimitation(documentation, $"### {heading}");
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
                    var methodIdentifiers = StringValues(disposition.GetProperty("testMethods"));
                    var expected = targetTestIds
                        .Where(testId =>
                            testId.StartsWith($"{fixture}.", StringComparison.Ordinal)
                            && methodIdentifiers.Any(
                                methodIdentifier => IsMethodDisplayId(
                                    testId,
                                    methodIdentifier)))
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
        var executableEngineTheorySkips = methods
            .Select(method => new
            {
                Method = method,
                Disposition = method.GetCustomAttribute<SpecEngineLimitationTheoryAttribute>(
                    inherit: false),
            })
            .Where(item => item.Disposition is not null)
            .Select(item => new
            {
                item.Method,
                DispositionId = item.Disposition!.DispositionId,
                UnsupportedTargets = (IReadOnlyList<string>)item.Disposition.UnsupportedTargets,
            });
        var executableEngineFactSkips = methods
            .Select(method => new
            {
                Method = method,
                Disposition = method.GetCustomAttribute<SpecEngineLimitationFactAttribute>(
                    inherit: false),
            })
            .Where(item => item.Disposition is not null)
            .Select(item => new
            {
                item.Method,
                DispositionId = item.Disposition!.DispositionId,
                UnsupportedTargets = (IReadOnlyList<string>)item.Disposition.UnsupportedTargets,
            });
        var executableEngineSkips = executableEngineTheorySkips
            .Concat(executableEngineFactSkips)
            .ToArray();

        var actualEngineMethods = new List<string>();
        foreach (var item in executableEngineSkips)
        {
            Assert.True(
                engineDispositions.TryGetValue(item.DispositionId, out var disposition),
                $"Engine disposition '{item.DispositionId}' is absent from the ledger.");

            var methodName = LedgerMethodName(item.Method);
            actualEngineMethods.Add(methodName);
            Assert.Contains(methodName, StringValues(disposition.GetProperty("testMethods")));
            Assert.Equal(
                StringValues(disposition.GetProperty("targets")).OrderBy(value => value),
                item.UnsupportedTargets.OrderBy(value => value));
        }

        var documentedEngineMethods = engineDispositions.Values
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(documentedEngineMethods, actualEngineMethods.OrderBy(value => value));

        var executableFrameworkTheorySkips = methods
            .Select(method => new
            {
                Method = method,
                Disposition = method.GetCustomAttribute<SpecFrameworkLimitationTheoryAttribute>(
                    inherit: false),
            })
            .Where(item => item.Disposition is not null)
            .Select(item => new
            {
                item.Method,
                item.Disposition!.DispositionId,
            });
        var executableFrameworkFactSkips = methods
            .Select(method => new
            {
                Method = method,
                Disposition = method.GetCustomAttribute<SpecFrameworkLimitationFactAttribute>(
                    inherit: false),
            })
            .Where(item => item.Disposition is not null)
            .Select(item => new
            {
                item.Method,
                item.Disposition!.DispositionId,
            });
        var executableFrameworkSkips = executableFrameworkTheorySkips
            .Concat(executableFrameworkFactSkips)
            .ToArray();

        var actualFrameworkMethods = new List<string>();
        foreach (var item in executableFrameworkSkips)
        {
            Assert.True(
                frameworkDispositions.TryGetValue(item.DispositionId, out var disposition),
                $"Framework disposition '{item.DispositionId}' is absent from the ledger.");

            var methodName = LedgerMethodName(item.Method);
            actualFrameworkMethods.Add(methodName);
            Assert.Contains(methodName, StringValues(disposition.GetProperty("testMethods")));
        }

        var documentedFrameworkMethods = frameworkDispositions.Values
            .SelectMany(disposition => StringValues(disposition.GetProperty("testMethods")))
            .OrderBy(value => value)
            .ToArray();
        var inheritedUpstreamFrameworkMethods = frameworkDispositions.Values
            .SelectMany(InheritedUpstreamMethods)
            .OrderBy(value => value)
            .ToArray();
        Assert.All(
            inheritedUpstreamFrameworkMethods,
            method => Assert.Contains(method, documentedFrameworkMethods));
        Assert.Equal(
            documentedFrameworkMethods
                .Except(inheritedUpstreamFrameworkMethods, StringComparer.Ordinal)
                .OrderBy(value => value),
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

    private static int CountOccurrences(
        string text,
        string value
    )
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static void AssertDocumentedLimitation(
        string documentation,
        string heading
    )
    {
        Assert.Equal(1, CountOccurrences(documentation, heading));

        var section = GetMarkdownSection(documentation, heading);
        Assert.Contains("- **Primary source", section, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"retrieved\s+20\d{2}-\d{2}-\d{2}",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            section);
    }

    private static string GetMarkdownSection(
        string documentation,
        string heading
    )
    {
        var start = documentation.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing Markdown heading '{heading}'.");

        var nextLevelThree = documentation.IndexOf(
            "\n### ",
            start + heading.Length,
            StringComparison.Ordinal);
        var nextLevelTwo = documentation.IndexOf(
            "\n## ",
            start + heading.Length,
            StringComparison.Ordinal);
        var end = new[] { nextLevelThree, nextLevelTwo }
            .Where(index => index >= 0)
            .DefaultIfEmpty(documentation.Length)
            .Min();

        return documentation[start..end];
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

    private static string FormatInheritedSkip(
        Type providerType,
        MethodInfo method
    ) => string.Join(
        "|",
        providerType.FullName,
        method.ReturnType.FullName,
        method.Name,
        string.Join(",", method.GetParameters().Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}")));

    private static bool MatchesInheritedDisposition(
        Type providerType,
        MethodInfo method,
        IReadOnlySet<string> dispositionedMethods
    )
    {
        foreach (var identifier in dispositionedMethods)
        {
            var separator = identifier.LastIndexOf('.');
            if (separator < 0
                || identifier[(separator + 1)..] != method.Name)
            {
                continue;
            }

            var declaringTypeName = identifier[..separator];
            for (var type = providerType; type is not null; type = type.BaseType)
            {
                if (type.Name == declaringTypeName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMethodDisplayId(
        string testId,
        string methodIdentifier
    )
    {
        var declaringTypeSeparator = methodIdentifier.LastIndexOf('.');
        if (declaringTypeSeparator < 0)
        {
            return false;
        }

        var declaringTypeName = methodIdentifier[..declaringTypeSeparator];
        var methodName = methodIdentifier[(declaringTypeSeparator + 1)..];
        var marker = $".{methodName}";
        var methodStart = testId.LastIndexOf(marker, StringComparison.Ordinal);
        if (methodStart < 0)
        {
            return false;
        }

        var suffix = methodStart + marker.Length;
        if (suffix != testId.Length && testId[suffix] != '(')
        {
            return false;
        }

        var testType = typeof(SpecDispositionContractTests)
            .Assembly
            .GetType(testId[..methodStart]);
        while (testType is not null)
        {
            if (testType.Name == declaringTypeName)
            {
                return true;
            }

            testType = testType.BaseType;
        }

        return false;
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

    private static void ValidateFrameworkEvidence(
        JsonElement disposition
    )
    {
        var sources = disposition
            .GetProperty("primarySources")
            .EnumerateArray()
            .Select(source => new Uri(
                RequiredString(source, "url"),
                UriKind.Absolute).AbsolutePath)
            .ToArray();
        var evidenceKind = disposition.TryGetProperty(
            "evidenceKind",
            out var evidenceKindProperty)
                ? evidenceKindProperty.GetString()
                : "issue";

        if (evidenceKind == "issue")
        {
            Assert.All(
                sources,
                source => Assert.StartsWith(
                    "/dotnet/efcore/issues/",
                    source,
                    StringComparison.Ordinal));
            return;
        }

        Assert.Equal("source-contract", evidenceKind);
        Assert.Contains(
            sources,
            source => source.StartsWith(
                "/dotnet/efcore/blob/v",
                StringComparison.Ordinal)
                && source.Contains(
                    "/test/",
                    StringComparison.Ordinal));
        Assert.Contains(
            sources,
            source => source.StartsWith(
                "/dotnet/efcore/blob/v",
                StringComparison.Ordinal)
                && source.Contains(
                    "/src/",
                    StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> InheritedUpstreamMethods(
        JsonElement disposition
    )
    {
        if (disposition.TryGetProperty("execution", out var execution)
            && execution.GetString() == "inherited-upstream-skip")
        {
            return StringValues(disposition.GetProperty("testMethods"));
        }

        return disposition.TryGetProperty(
            "inheritedUpstreamTestMethods",
            out var inheritedMethods)
                ? StringValues(inheritedMethods)
                : [];
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
