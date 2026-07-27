using Doka.EntityFrameworkCore.MySql.SpecificationContract;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Exercises the failure modes that protect the EF Core specification inventory,
/// monotonic provider-debt baseline, exact discovery set, and TRX reconciliation.
/// </summary>
public class SpecificationContractTests
{
    private const string EfCoreVersion = "10.0.8";
    private const string UpstreamBaseId = "Specification.Tests:ExampleTestBase";

    private const string Fixture =
        "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.ExampleMySqlTest";

    private const string PassedTestId = Fixture + ".Passes";
    private const string SkippedTestId = Fixture + ".Skipped(async: False)";
    private const string MissingTestId = Fixture + ".Missing";
    private const string UnexpectedTestId = Fixture + ".Unexpected";

    private static readonly string[] s_testTargets = ["mysql84"];
    private static readonly string[] s_testMethods = ["ExampleMySqlTest.Skipped"];

    [Fact]
    public void Baseline_accepts_existing_provider_debt_without_growth()
    {
        var entry = Entry();
        var errors = Validate(Baseline(entry));

        Assert.Empty(errors);
    }

    [Fact]
    public void Baseline_rejects_duplicate_and_unknown_base_identifiers()
    {
        var entry = Entry();
        var duplicateErrors = Validate(Baseline(entry, entry));
        var missingErrors = Validate(Baseline());

        Assert.Contains(
            duplicateErrors,
            error => error.Contains("Duplicate baseline base ID", StringComparison.Ordinal));
        Assert.Contains(
            missingErrors,
            error => error.Contains("is absent from the baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void Baseline_rejects_removed_implemented_mapping()
    {
        var entry = Entry(
            baselineState: "implemented",
            providerTypes: ["Provider.Original"],
            closurePhase: null,
            expiresAt: null);
        var actualMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [UpstreamBaseId] = ["Provider.Replacement"],
        };
        var errors = Validate(Baseline(entry), actualMappings);

        Assert.Contains(errors, error => error.Contains("Provider.Original", StringComparison.Ordinal));
    }

    [Fact]
    public void Baseline_rejects_gap_growth_and_invalid_phase_assignment()
    {
        var entry = Entry(closurePhase: 3);
        var baseline = Baseline(entry) with
        {
            InitialProviderGapCount = 0,
        };
        var errors = Validate(baseline);

        Assert.Contains(errors, error => error.Contains("closurePhase 4, 5, or 6", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Provider gap count grew", StringComparison.Ordinal));
    }

    [Fact]
    public void Baseline_rejects_assignment_fingerprint_change()
    {
        var baseline = Baseline(Entry()) with
        {
            AssignmentFingerprint = "tampered",
        };
        var errors = Validate(baseline);

        Assert.Contains(errors, error => error.Contains("Assignment fingerprint mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Baseline_rejects_implemented_contract_without_provider_mapping()
    {
        var entry = Entry(
            baselineState: "implemented",
            providerTypes: ["Provider.Original"],
            closurePhase: null,
            expiresAt: null);
        var errors = Validate(Baseline(entry));

        Assert.Contains(errors, error => error.Contains("no longer has a provider mapping", StringComparison.Ordinal));
    }

    [Fact]
    public void Publication_gate_rejects_nonzero_provider_debt()
    {
        var report = SpecificationContractValidator.EnforceZeroProviderDebt(
            new SpecificationContractReport(EfCoreVersion, 1, 1, []));

        Assert.False(report.IsValid);
        Assert.Contains(
            report.Errors,
            error => error.Contains("Publication requires zero provider suite debt", StringComparison.Ordinal));
    }

    [Fact]
    public void Discovery_rejects_duplicate_missing_unexpected_and_fixture_drift()
    {
        var expected = DiscoveryContract.Update(
            null,
            EfCoreVersion,
            "Provider.Tests",
            "mysql84",
            [
                PassedTestId,
                SkippedTestId
            ]);
        var duplicateTarget = expected with
        {
            Targets =
            [
                expected.Targets[0],
                expected.Targets[0]
            ],
        };
        var actual = new[]
        {
            PassedTestId,
            PassedTestId,
            "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query." + "DifferentMySqlTest.Unexpected",
        };

        var errors = DiscoveryContract.Validate(duplicateTarget, "mysql84", actual, requireAllTargets: false);

        Assert.Contains(errors, error => error.Contains("Duplicate discovery target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicate test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing expected test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unexpected test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("fixture set changed", StringComparison.Ordinal));
    }

    [Fact]
    public void Trx_accepts_only_exact_results_and_declared_not_executed_ids()
    {
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");
        try
        {
            var trxPath = Path.Combine(directory.FullName, "valid.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(trxPath, Trx((PassedTestId, "Passed"), (SkippedTestId, "NotExecuted")));
            File.WriteAllText(dispositionPath, Dispositions(SkippedTestId));

            var report = TrxContract.Validate(
                Discovery(PassedTestId, SkippedTestId),
                "mysql84",
                [trxPath],
                dispositionPath);

            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
            Assert.Equal(2, report.Total);
            Assert.Equal(1, report.Passed);
            Assert.Equal(1, report.NotExecuted);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Trx_rejects_duplicate_missing_failed_and_undeclared_results()
    {
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");
        try
        {
            var trxPath = Path.Combine(directory.FullName, "invalid.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(
                trxPath,
                Trx(
                    (PassedTestId, "Failed"),
                    (PassedTestId, "Passed"),
                    (SkippedTestId, "NotExecuted"),
                    (UnexpectedTestId, "Passed")));
            File.WriteAllText(dispositionPath, Dispositions());

            var report = TrxContract.Validate(
                Discovery(PassedTestId, SkippedTestId, MissingTestId),
                "mysql84",
                [trxPath],
                dispositionPath);

            Assert.Contains(report.Errors, error => error.Contains("2 outcomes", StringComparison.Ordinal));
            Assert.Contains(
                report.Errors,
                error => error.Contains("missing expected test ID", StringComparison.Ordinal));
            Assert.Contains(
                report.Errors,
                error => error.Contains("unexpected specification test ID", StringComparison.Ordinal));
            Assert.Contains(report.Errors, error => error.Contains("outcome", StringComparison.Ordinal));
            Assert.Contains(report.Errors, error => error.Contains("undeclared NotExecuted", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Trx_rejects_dispositioned_test_that_now_passes()
    {
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");
        try
        {
            var trxPath = Path.Combine(directory.FullName, "passed.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(trxPath, Trx((SkippedTestId, "Passed")));
            File.WriteAllText(dispositionPath, Dispositions(SkippedTestId));

            var report = TrxContract.Validate(Discovery(SkippedTestId), "mysql84", [trxPath], dispositionPath);

            Assert.Contains(
                report.Errors,
                error => error.Contains("passed and must be re-evaluated", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static List<string> Validate(
        SpecificationBaselineDocument baseline,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? actualMappings = null
    )
    {
        var errors = new List<string>();
        SpecificationContractValidator.ValidateBaseline(
            Inventory(),
            baseline,
            actualMappings ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            errors);
        return errors;
    }

    private static SpecificationInventoryDocument Inventory() => new(
        SpecificationInventory.SchemaVersion,
        EfCoreVersion,
        "2026-07-27",
        new AssemblyIdentity("Specification.Tests", "10.0.8.0", "10.0.8"),
        new AssemblyIdentity("Relational.Specification.Tests", "10.0.8.0", "10.0.8"),
        [],
        [
            new SpecificationBaseDescriptor(
                UpstreamBaseId,
                "Specification.Tests",
                "ExampleTestBase",
                null,
                true,
                0,
                "migration-update",
                [],
                [],
                []),
        ]);

    private static SpecificationBaselineDocument Baseline(
        params SpecificationBaselineEntry[] entries
    ) => new(
        SpecificationBaseline.SchemaVersion,
        [EfCoreVersion],
        SpecificationBaseline.SupportedTargets,
        "publication-zero-gap",
        entries.Count(entry => entry.BaselineState == "provider-debt"),
        SpecificationBaseline.AssignmentFingerprint(entries),
        entries);

    private static SpecificationBaselineEntry Entry(
        string baselineState = "provider-debt",
        IReadOnlyList<string>? providerTypes = null,
        int? closurePhase = 4,
        string? expiresAt = "publication-zero-gap"
    ) => new(
        UpstreamBaseId,
        [EfCoreVersion],
        "migration-update",
        baselineState,
        providerTypes ?? [],
        closurePhase,
        "migrations-updates-transactions",
        SpecificationBaseline.SupportedTargets,
        "Reviewed test evidence.",
        expiresAt ?? string.Empty,
        null);

    private static DiscoveryContractDocument Discovery(
        params string[] testIds
    ) => DiscoveryContract.Update(null, EfCoreVersion, "Provider.Tests", "mysql84", testIds);

    private static string Dispositions(
        params string[] discoveredTestIds
    ) => JsonSerializer.Serialize(
        new
        {
            schemaVersion = 2,
            activeDispositions = new[]
            {
                new
                {
                    id = "TEST-SKIP",
                    suite = "query",
                    fixture = Fixture,
                    targets = s_testTargets,
                    testMethods = s_testMethods,
                    discoveredTestIds,
                },
            },
        });

    private static string Trx(
        params (string TestId, string Outcome)[] results
    )
    {
        var body = string.Join(
            string.Empty,
            results.Select(result => $"<UnitTestResult testName=\"{result.TestId}\" outcome=\"{result.Outcome}\" />"));
        return $"<TestRun><Results>{body}</Results></TestRun>";
    }
}
