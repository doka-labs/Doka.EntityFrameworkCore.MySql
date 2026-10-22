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

    /// <summary>
    /// Verifies baseline accepts existing provider debt without growth.
    /// </summary>
    [Fact]
    public void Baseline_accepts_existing_provider_debt_without_growth()
    {
        // Arrange
        var entry = Entry();

        // Act
        var errors = Validate(Baseline(entry));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies baseline rejects duplicate base identifiers independently of missing entries.
    /// </summary>
    [Fact]
    public void Baseline_rejects_duplicate_base_identifiers()
    {
        // Arrange
        var entry = Entry();
        var baseline = Baseline(entry, entry);

        // Act
        var errors = Validate(baseline);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("Duplicate baseline base ID", StringComparison.Ordinal));
    }

    /// <summary>Verifies baseline rejects an inventory base identifier missing from the baseline.</summary>
    [Fact]
    public void Baseline_rejects_missing_base_identifier()
    {
        // Arrange
        var baseline = Baseline();

        // Act
        var errors = Validate(baseline);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains("is absent from the baseline", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies baseline rejects removed implemented mapping.
    /// </summary>
    [Fact]
    public void Baseline_rejects_removed_implemented_mapping()
    {
        // Arrange
        var entry = Entry(
            baselineState: "implemented",
            providerTypes: ["Provider.Original"],
            closurePhase: null,
            expiresAt: null);

        var actualMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [UpstreamBaseId] = ["Provider.Replacement"],
        };

        // Act
        var errors = Validate(Baseline(entry), actualMappings);

        // Assert
        Assert.Contains(errors, error => error.Contains("Provider.Original", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies baseline rejects gap growth with an otherwise valid phase assignment.
    /// </summary>
    [Fact]
    public void Baseline_rejects_gap_growth()
    {
        // Arrange
        var entry = Entry();
        var baseline = Baseline(entry) with
        {
            InitialProviderGapCount = 0,
        };

        // Act
        var errors = Validate(baseline);

        // Assert
        Assert.Contains(errors, error => error.Contains("Provider gap count grew", StringComparison.Ordinal));
    }

    /// <summary>Verifies baseline rejects an invalid phase assignment without growing the gap count.</summary>
    [Fact]
    public void Baseline_rejects_invalid_phase_assignment()
    {
        // Arrange
        var baseline = Baseline(Entry(closurePhase: 3));

        // Act
        var errors = Validate(baseline);

        // Assert
        Assert.Contains(errors, error => error.Contains("closurePhase 4, 5, or 6", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies baseline rejects assignment fingerprint change.
    /// </summary>
    [Fact]
    public void Baseline_rejects_assignment_fingerprint_change()
    {
        // Arrange
        var baseline = Baseline(Entry()) with
        {
            AssignmentFingerprint = "tampered",
        };

        // Act
        var errors = Validate(baseline);

        // Assert
        Assert.Contains(errors, error => error.Contains("Assignment fingerprint mismatch", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies baseline rejects implemented contract without provider mapping.
    /// </summary>
    [Fact]
    public void Baseline_rejects_implemented_contract_without_provider_mapping()
    {
        // Arrange
        var entry = Entry(
            baselineState: "implemented",
            providerTypes: ["Provider.Original"],
            closurePhase: null,
            expiresAt: null);

        // Act
        var errors = Validate(Baseline(entry));

        // Assert
        Assert.Contains(errors, error => error.Contains("no longer has a provider mapping", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies publication gate rejects nonzero provider debt.
    /// </summary>
    [Fact]
    public void Publication_gate_rejects_nonzero_provider_debt()
    {
        // Arrange
        var input = new SpecificationContractReport(EfCoreVersion, 1, 1, [UpstreamBaseId], []);

        // Act
        var report = SpecificationContractValidator.EnforceZeroProviderDebt(input);

        // Assert
        Assert.False(report.IsValid);
        Assert.Contains(
            report.Errors,
            error => error.Contains("Publication requires zero provider suite debt", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies debt report sorts and preserves exact current gap ids.
    /// </summary>
    [Fact]
    public void Debt_report_sorts_and_preserves_exact_current_gap_ids()
    {
        // Arrange
        var report = new SpecificationContractReport(
            EfCoreVersion,
            2,
            2,
            ["Specification.Tests:ZuluTestBase", UpstreamBaseId],
            []);

        // Act
        var document = SpecificationDebtReport.Create(report);

        // Assert
        Assert.Equal(SpecificationDebtReport.SchemaVersion, document.SchemaVersion);
        Assert.Equal(EfCoreVersion, document.EfCoreVersion);
        Assert.Equal(2, document.InitialProviderGapCount);
        Assert.Equal(2, document.CurrentProviderGapCount);
        Assert.Equal(
            [UpstreamBaseId, "Specification.Tests:ZuluTestBase"],
            document.CurrentProviderGaps);
    }

    /// <summary>
    /// Verifies debt report rejects duplicate identifiers in an otherwise valid contract.
    /// </summary>
    [Fact]
    public void Debt_report_rejects_duplicate_ids()
    {
        // Arrange
        var duplicateIds = new SpecificationContractReport(
            EfCoreVersion,
            2,
            2,
            [UpstreamBaseId, UpstreamBaseId],
            []);

        // Act
        var exception = Record.Exception(() => SpecificationDebtReport.Create(duplicateIds));

        // Assert
        Assert.IsType<InvalidDataException>(exception);
    }

    /// <summary>Verifies debt report refuses a contract that already contains validation errors.</summary>
    [Fact]
    public void Debt_report_rejects_invalid_contract()
    {
        // Arrange
        var invalidContract = new SpecificationContractReport(
            EfCoreVersion,
            1,
            1,
            [UpstreamBaseId],
            ["Contract validation failed."]);

        // Act
        var exception = Record.Exception(() => SpecificationDebtReport.Create(invalidContract));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    /// Verifies each discovery failure through an independently reported input case.
    /// </summary>
    /// <param name="violation">The independently reported invalid discovery input.</param>
    /// <param name="expectedError">The diagnostic that must identify that failure.</param>
    [Theory]
    [InlineData("duplicate-target", "Duplicate discovery target")]
    [InlineData("duplicate-test", "duplicate test ID")]
    [InlineData("missing-test", "missing expected test ID")]
    [InlineData("unexpected-test", "unexpected test ID")]
    [InlineData("fixture-drift", "fixture set changed")]
    public void Discovery_rejects_independent_contract_violation(
        string violation,
        string expectedError
    )
    {
        // Arrange
        var expected = DiscoveryContract.Update(
            null,
            EfCoreVersion,
            "Provider.Tests",
            "mysql84",
            [
                PassedTestId,
                SkippedTestId
            ]);

        var contract = violation == "duplicate-target" ? expected with
        {
            Targets =
            [
                expected.Targets[0],
                expected.Targets[0]
            ],
        } : expected;

        string[] actual = violation switch
        {
            "duplicate-target" => [PassedTestId, SkippedTestId],
            "duplicate-test" => [PassedTestId, PassedTestId, SkippedTestId],
            "missing-test" => [PassedTestId],
            "unexpected-test" => [PassedTestId, SkippedTestId, UnexpectedTestId],
            "fixture-drift" =>
            [
                PassedTestId,
                SkippedTestId,
                "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.DifferentMySqlTest.Unexpected",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown discovery violation."),
        };

        // Act
        var errors = DiscoveryContract.Validate(contract, "mysql84", actual, requireAllTargets: false);

        // Assert
        Assert.Contains(errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    /// <summary>Verifies discovery reports every diagnostic when several inputs are invalid together.</summary>
    [Fact]
    public void Discovery_reports_all_contract_violations_in_one_pass()
    {
        // Arrange
        var expected = Discovery(PassedTestId, SkippedTestId);
        var contract = expected with
        {
            Targets = [expected.Targets[0], expected.Targets[0]],
        };

        string[] actual =
        [
            PassedTestId,
            PassedTestId,
            "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.Query.DifferentMySqlTest.Unexpected",
        ];

        // Act
        // WHY: The independent cases identify each failure. This compound input
        // additionally guards diagnostic accumulation against early-return changes.
        var errors = DiscoveryContract.Validate(contract, "mysql84", actual, requireAllTargets: false);

        // Assert
        Assert.Contains(errors, error => error.Contains("Duplicate discovery target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicate test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing expected test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unexpected test ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("fixture set changed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies contract json escapes non ascii characters without changing the value.
    /// </summary>
    [Fact]
    public void Contract_json_escapes_non_ascii_characters_without_changing_the_value()
    {
        // Arrange
        const string unicodeValue = "\u2764\U0001F44D";
        var input = new
        {
            Value = unicodeValue,
        };

        // Act
        var serialized = ContractJson.Serialize(input);
        var roundTripped = JsonSerializer.Deserialize<JsonElement>(serialized)
            .GetProperty("value")
            .GetString();

        // Assert
        Assert.DoesNotContain(unicodeValue, serialized, StringComparison.Ordinal);
        Assert.All(serialized, character => Assert.InRange(character, '\0', '\u007f'));
        Assert.Equal(unicodeValue, roundTripped);
    }

    /// <summary>
    /// Verifies trx accepts only exact results and declared not executed ids.
    /// </summary>
    [Fact]
    public void Trx_accepts_only_exact_results_and_declared_not_executed_ids()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");

        try
        {
            var trxPath = Path.Combine(directory.FullName, "valid.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(trxPath, Trx((PassedTestId, "Passed"), (SkippedTestId, "NotExecuted")));
            File.WriteAllText(dispositionPath, Dispositions(SkippedTestId));

            // Act
            var report = TrxContract.Validate(
                Discovery(PassedTestId, SkippedTestId),
                "mysql84",
                [trxPath],
                dispositionPath);

            // Assert
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

    /// <summary>
    /// Verifies trx normalizes only escaped utf16 surrogate pairs.
    /// </summary>
    [Fact]
    public void Trx_normalizes_only_escaped_utf16_surrogate_pairs()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");

        try
        {
            var trxPath = Path.Combine(directory.FullName, "unicode.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            var supplementaryCharacterId = Fixture + ".Unicode(\U0001F44D)";
            var escapedSurrogatePairId = Fixture + ".Unicode(\\ud83d\\udc4d)";
            var literalBmpEscapeId = Fixture + ".Json(\\u2764)";

            File.WriteAllText(
                trxPath,
                Trx(
                    (escapedSurrogatePairId, "Passed"),
                    (literalBmpEscapeId, "Passed")));
            File.WriteAllText(dispositionPath, EmptyDispositions());

            // Act
            // WHY: Mixed identities prove supplementary escapes normalize while
            // literal BMP escapes remain unchanged in the same reconciled report.
            var report = TrxContract.Validate(
                Discovery(supplementaryCharacterId, literalBmpEscapeId),
                "mysql84",
                [trxPath],
                dispositionPath);

            // Assert
            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
            Assert.Equal(2, report.Passed);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Verifies TRX reports every diagnostic when several results violate the contract together.
    /// </summary>
    [Fact]
    public void Trx_reports_all_contract_violations_in_one_pass()
    {
        // Arrange
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

            // Act
            // WHY: This single reconciliation must collect all diagnostics, even
            // when an earlier result has already violated another invariant.
            var report = TrxContract.Validate(
                Discovery(PassedTestId, SkippedTestId, MissingTestId),
                "mysql84",
                [trxPath],
                dispositionPath);

            // Assert
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

    /// <summary>
    /// Verifies trx rejects dispositioned test that now passes.
    /// </summary>
    [Fact]
    public void Trx_rejects_dispositioned_test_that_now_passes()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");

        try
        {
            var trxPath = Path.Combine(directory.FullName, "passed.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            File.WriteAllText(trxPath, Trx((SkippedTestId, "Passed")));
            File.WriteAllText(dispositionPath, Dispositions(SkippedTestId));

            // Act
            var report = TrxContract.Validate(Discovery(SkippedTestId), "mysql84", [trxPath], dispositionPath);

            // Assert
            Assert.Contains(
                report.Errors,
                error => error.Contains("passed and must be re-evaluated", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Verifies one independently reported TRX reconciliation failure.</summary>
    /// <param name="violation">The invalid result shape exercised by this case.</param>
    /// <param name="expectedError">The diagnostic required for that result shape.</param>
    [Theory]
    [InlineData("duplicate", "2 outcomes")]
    [InlineData("missing", "missing expected test ID")]
    [InlineData("unexpected", "unexpected specification test ID")]
    [InlineData("failed", "outcome")]
    [InlineData("undeclared-not-executed", "undeclared NotExecuted")]
    public void Trx_rejects_independent_contract_violation(
        string violation,
        string expectedError
    )
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("doka-spec-contract-");

        try
        {
            var trxPath = Path.Combine(directory.FullName, "invalid.trx");
            var dispositionPath = Path.Combine(directory.FullName, "dispositions.json");
            var expectedId = violation == "undeclared-not-executed" ? SkippedTestId : PassedTestId;
            (string TestId, string Outcome)[] results = violation switch
            {
                "duplicate" => [(PassedTestId, "Passed"), (PassedTestId, "Passed")],
                "missing" => [],
                "unexpected" => [(PassedTestId, "Passed"), (UnexpectedTestId, "Passed")],
                "failed" => [(PassedTestId, "Failed")],
                "undeclared-not-executed" => [(SkippedTestId, "NotExecuted")],
                _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Unknown TRX violation."),
            };

            File.WriteAllText(trxPath, Trx(results));
            File.WriteAllText(dispositionPath, EmptyDispositions());

            // Act
            var report = TrxContract.Validate(Discovery(expectedId), "mysql84", [trxPath], dispositionPath);

            // Assert
            Assert.Contains(report.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
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

    private static string EmptyDispositions() => JsonSerializer.Serialize(
        new
        {
            schemaVersion = 2,
            activeDispositions = Array.Empty<object>(),
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
