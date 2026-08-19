namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Validates the committed inventory and monotonic provider-suite baseline against the exact
/// assemblies loaded for the current EF Core patch.
/// </summary>
internal static class SpecificationContractValidator
{
    private const string ContractsRelativePath =
        "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts";

    /// <summary>
    /// Validates the repository contract for the current restored EF Core version.
    /// </summary>
    internal static SpecificationContractReport ValidateRepository(
        string repositoryRoot,
        string providerAssemblyPath
    )
    {
        var contractsRoot = Path.Combine(repositoryRoot, ContractsRelativePath);
        var currentVersion = SpecificationInventory.CurrentEfCoreVersion();
        var inventoryPath = Path.Combine(contractsRoot, $"SpecSuiteInventory.{currentVersion}.json");
        var baselinePath = Path.Combine(contractsRoot, "SpecSuiteBaseline.json");

        if (!File.Exists(inventoryPath))
        {
            return new SpecificationContractReport(
                currentVersion,
                0,
                0,
                [],
                [$"No committed inventory exists for EF Core {currentVersion}: {inventoryPath}"]);
        }

        if (!File.Exists(baselinePath))
        {
            return new SpecificationContractReport(
                currentVersion,
                0,
                0,
                [],
                [$"Specification baseline not found: {baselinePath}"]);
        }

        var inventory = SpecificationInventory.Load(inventoryPath);
        var baseline = SpecificationBaseline.Load(baselinePath);
        var providerAssembly = ProviderAssembly.Load(providerAssemblyPath);
        var errors = new List<string>();

        ValidateExactInventory(inventory, errors);
        var state = ValidateBaseline(inventory, baseline, providerAssembly, errors);

        return new SpecificationContractReport(
            currentVersion,
            baseline.InitialProviderGapCount,
            state.CurrentProviderGapCount,
            state.CurrentProviderGaps,
            errors);
    }

    /// <summary>
    /// Runs the direct official zero-ignore compliance assertions used by the publication gate.
    /// </summary>
    internal static SpecificationContractReport ValidatePublication(
        string repositoryRoot,
        string providerAssemblyPath
    )
    {
        var report = ValidateRepository(repositoryRoot, providerAssemblyPath);
        var errors = report.Errors.ToList();
        // The baseline already identifies every missing provider mapping. Run the
        // independent upstream assertion only after that actionable debt reaches zero.
        if (errors.Count == 0
            && report.CurrentProviderGapCount == 0)
        {
            var providerAssembly = ProviderAssembly.Load(providerAssemblyPath);
            try
            {
                new OfficialComplianceProbe(providerAssembly).Verify();
            }
            catch (Exception exception)
            {
                errors.Add($"Official RelationalComplianceTestBase gate failed: {exception.Message}");
            }
        }

        return EnforceZeroProviderDebt(
            report with
            {
                Errors = errors,
            });
    }

    /// <summary>
    /// Applies the irreversible publication boundary: baseline debt may exist during
    /// implementation, but no provider-owned specification gap may reach a release.
    /// </summary>
    internal static SpecificationContractReport EnforceZeroProviderDebt(
        SpecificationContractReport report
    )
    {
        if (report.CurrentProviderGapCount == 0)
        {
            return report;
        }

        return report with
        {
            Errors =
            [
                .. report.Errors,
                $"Publication requires zero provider suite debt; "
                + $"{report.CurrentProviderGapCount} base contract(s) remain.",
            ],
        };
    }

    /// <summary>
    /// Validates supplied documents and mappings. Tests use this entry point to exercise every
    /// negative ratchet transition without loading synthetic assemblies.
    /// </summary>
    internal static SpecificationBaselineState ValidateBaseline(
        SpecificationInventoryDocument inventory,
        SpecificationBaselineDocument baseline,
        IReadOnlyDictionary<string, IReadOnlyList<string>> actualMappings,
        List<string> errors
    )
    {
        if (inventory.SchemaVersion != SpecificationInventory.SchemaVersion)
        {
            errors.Add(
                $"Inventory schema {inventory.SchemaVersion} is unsupported; "
                + $"expected {SpecificationInventory.SchemaVersion}.");
        }

        if (baseline.SchemaVersion != SpecificationBaseline.SchemaVersion)
        {
            errors.Add(
                $"Baseline schema {baseline.SchemaVersion} is unsupported; "
                + $"expected {SpecificationBaseline.SchemaVersion}.");
        }

        if (!baseline.EfCoreVersions.Contains(inventory.EfCoreVersion, StringComparer.Ordinal))
        {
            errors.Add($"Baseline does not declare EF Core {inventory.EfCoreVersion}.");
        }

        if (baseline.PublicationGate != "publication-zero-gap")
        {
            errors.Add("Baseline publicationGate must be 'publication-zero-gap'.");
        }

        ValidateDistinct(inventory.BaseClasses.Select(descriptor => descriptor.Id), "inventory base ID", errors);
        ValidateDistinct(baseline.Entries.Select(entry => entry.UpstreamBaseId), "baseline base ID", errors);

        var entries = baseline
            .Entries.GroupBy(entry => entry.UpstreamBaseId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var inventoryIds = inventory
            .BaseClasses.Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in inventory.BaseClasses)
        {
            if (!entries.TryGetValue(descriptor.Id, out var entry))
            {
                errors.Add($"Unknown upstream base '{descriptor.Id}' is absent from the baseline.");
                continue;
            }

            if (!entry.EfCoreVersions.Contains(inventory.EfCoreVersion, StringComparer.Ordinal))
            {
                errors.Add($"Baseline entry '{entry.UpstreamBaseId}' omits EF Core " + $"{inventory.EfCoreVersion}.");
            }

            if (entry.SuiteDomain != descriptor.SuiteDomain)
            {
                errors.Add(
                    $"Suite domain changed for '{entry.UpstreamBaseId}': "
                    + $"baseline '{entry.SuiteDomain}', inventory '{descriptor.SuiteDomain}'.");
            }
        }

        foreach (var entry in baseline.Entries.Where(entry =>
                     entry.EfCoreVersions.Contains(inventory.EfCoreVersion, StringComparer.Ordinal)))
        {
            if (!inventoryIds.Contains(entry.UpstreamBaseId))
            {
                errors.Add(
                    $"Baseline entry '{entry.UpstreamBaseId}' is absent from the "
                    + $"{inventory.EfCoreVersion} inventory.");
            }

            ValidateEntry(entry, baseline.SupportedTargets, errors);
        }

        var baselineDebt = baseline
            .Entries.Where(entry => entry.BaselineState == "provider-debt")
            .ToArray();

        if (baseline.InitialProviderGapCount != baselineDebt.Length)
        {
            errors.Add(
                $"initialProviderGapCount is {baseline.InitialProviderGapCount}, "
                + $"but the baseline contains {baselineDebt.Length} provider-debt entries.");
        }

        var expectedFingerprint = SpecificationBaseline.AssignmentFingerprint(baseline.Entries);
        if (baseline.AssignmentFingerprint != expectedFingerprint)
        {
            errors.Add(
                $"Assignment fingerprint mismatch: expected '{expectedFingerprint}', "
                + $"found '{baseline.AssignmentFingerprint}'.");
        }

        var currentGaps = new List<string>();
        foreach (var descriptor in inventory.BaseClasses)
        {
            if (!entries.TryGetValue(descriptor.Id, out var entry))
            {
                continue;
            }

            var mappings = actualMappings.TryGetValue(descriptor.Id, out var values) ? values : [];
            if (mappings.Count > 0)
            {
                if (entry.BaselineState == "implemented")
                {
                    var removedMappings = entry
                        .ProviderTypes.Except(mappings, StringComparer.Ordinal)
                        .ToArray();

                    foreach (var removedMapping in removedMappings)
                    {
                        errors.Add(
                            $"Baseline provider mapping '{removedMapping}' was removed from " + $"'{descriptor.Id}'.");
                    }
                }

                continue;
            }

            if (entry.BaselineState == "compliance-exempt")
            {
                continue;
            }

            currentGaps.Add(descriptor.Id);
            if (entry.BaselineState != "provider-debt")
            {
                errors.Add($"Implemented baseline '{descriptor.Id}' no longer has a provider mapping.");
            }
        }

        if (currentGaps.Count > baseline.InitialProviderGapCount)
        {
            errors.Add(
                $"Provider gap count grew from {baseline.InitialProviderGapCount} " + $"to {currentGaps.Count}.");
        }

        return new SpecificationBaselineState(
            currentGaps.Count,
            currentGaps
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    private static void ValidateExactInventory(
        SpecificationInventoryDocument committed,
        List<string> errors
    )
    {
        if (!DateOnly.TryParseExact(
                committed.RetrievedAt,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var retrievedAt))
        {
            errors.Add($"Inventory retrievedAt '{committed.RetrievedAt}' is invalid.");
            return;
        }

        var actual = SpecificationInventory.Create(retrievedAt);
        if (ContractJson.Serialize(committed) != ContractJson.Serialize(actual))
        {
            errors.Add(
                $"Committed EF Core {committed.EfCoreVersion} inventory does not match "
                + "the exact restored assemblies; regenerate it.");
        }
    }

    private static SpecificationBaselineState ValidateBaseline(
        SpecificationInventoryDocument inventory,
        SpecificationBaselineDocument baseline,
        Assembly providerAssembly,
        List<string> errors
    )
    {
        var baseTypes = SpecificationInventory
            .BaseTestClasses()
            .ToDictionary(SpecificationInventory.TypeId, StringComparer.Ordinal);

        var providerTypes = SpecificationBaseline.ConcretePublicTypes(providerAssembly);
        var actualMappings = inventory.BaseClasses.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => (IReadOnlyList<string>)(baseTypes.TryGetValue(descriptor.Id, out var baseType)
                ?
                [
                    .. providerTypes
                        .Where(type => SpecificationInventory.Implements(type, baseType))
                        .Select(type => type.FullName!)
                        .OrderBy(value => value, StringComparer.Ordinal),
                ]
                : []),
            StringComparer.Ordinal);

        return ValidateBaseline(inventory, baseline, actualMappings, errors);
    }

    private static void ValidateEntry(
        SpecificationBaselineEntry entry,
        IReadOnlyList<string> supportedTargets,
        List<string> errors
    )
    {
        if (entry.Targets.Count == 0
            || !entry
                .Targets.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(
                    supportedTargets.OrderBy(value => value, StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            errors.Add($"Baseline entry '{entry.UpstreamBaseId}' must cover every supported target.");
        }

        if (string.IsNullOrWhiteSpace(entry.OwnerSurface)
            || string.IsNullOrWhiteSpace(entry.Evidence))
        {
            errors.Add($"Baseline entry '{entry.UpstreamBaseId}' has incomplete ownership or evidence.");
        }

        switch (entry.BaselineState)
        {
            case "implemented":
                if (entry.ProviderTypes.Count == 0
                    || entry.ClosurePhase is not null)
                {
                    errors.Add(
                        $"Implemented entry '{entry.UpstreamBaseId}' requires providerTypes " + "and no closurePhase.");
                }

                break;
            case "provider-debt":
                if (entry.ProviderTypes.Count != 0
                    || entry.ClosurePhase is not (4 or 5 or 6)
                    || entry.ExpiresAt != "publication-zero-gap")
                {
                    errors.Add(
                        $"Provider debt '{entry.UpstreamBaseId}' requires no baseline mapping, "
                        + "closurePhase 4, 5, or 6, and publication-zero-gap expiry.");
                }

                break;
            case "compliance-exempt":
                if (entry.UpstreamBaseId
                    != "Microsoft.EntityFrameworkCore.Specification.Tests:"
                    + "Microsoft.EntityFrameworkCore.NonSharedModelTestBase"
                    || entry.ProviderTypes.Count != 0
                    || entry.ClosurePhase is not null)
                {
                    errors.Add($"Invalid official compliance exemption '{entry.UpstreamBaseId}'.");
                }

                break;
            default:
                errors.Add($"Baseline entry '{entry.UpstreamBaseId}' has unknown state " + $"'{entry.BaselineState}'.");
                break;
        }
    }

    private static void ValidateDistinct(
        IEnumerable<string> values,
        string label,
        List<string> errors
    )
    {
        foreach (var duplicate in values
                     .GroupBy(value => value, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            errors.Add($"Duplicate {label} '{duplicate}'.");
        }
    }

    private sealed class OfficialComplianceProbe(Assembly targetAssembly) : RelationalComplianceTestBase
    {
        protected override Assembly TargetAssembly => targetAssembly;

        internal void Verify()
        {
            All_test_bases_must_be_implemented();
            All_query_test_fixtures_must_implement_ITestSqlLoggerFactory();
        }
    }
}

internal sealed record SpecificationContractReport(
    string EfCoreVersion,
    int InitialProviderGapCount,
    int CurrentProviderGapCount,
    IReadOnlyList<string> CurrentProviderGaps,
    IReadOnlyList<string> Errors
)
{
    internal bool IsValid => Errors.Count == 0;
}

internal sealed record SpecificationBaselineState(
    int CurrentProviderGapCount,
    IReadOnlyList<string> CurrentProviderGaps
);
