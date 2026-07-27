namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Persists and reconciles exact xUnit display names emitted by filtered specification
/// discovery. Exact IDs make inherited-test disappearance and duplicate discovery observable.
/// </summary>
internal static class DiscoveryContract
{
    internal const int SchemaVersion = 1;
    internal const string SpecificationTestPrefix = "Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.";

    /// <summary>
    /// Parses the stable test-ID portion of <c>dotnet test --list-tests</c> output.
    /// </summary>
    internal static IReadOnlyList<string> ParseListOutput(
        string output
    ) =>
    [
        .. output
            .Split('\n')
            .Select(line => line
                .Trim()
                .TrimEnd('\r'))
            .Where(line => line.StartsWith(SpecificationTestPrefix, StringComparison.Ordinal))
            .OrderBy(value => value, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Adds or replaces one target in a version-bound discovery contract.
    /// </summary>
    internal static DiscoveryContractDocument Update(
        DiscoveryContractDocument? existing,
        string efCoreVersion,
        string providerAssembly,
        string target,
        IReadOnlyList<string> testIds
    )
    {
        ValidateTarget(target);
        if (testIds.Count == 0)
        {
            throw new InvalidDataException($"Discovery for target '{target}' did not contain specification tests.");
        }

        var duplicate = testIds
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException($"Discovery for target '{target}' contains duplicate ID '{duplicate.Key}'.");
        }

        if (existing is not null
            && (existing.EfCoreVersion != efCoreVersion || existing.ProviderAssembly != providerAssembly))
        {
            throw new InvalidDataException(
                "Existing discovery contract belongs to a different EF Core version " + "or provider assembly.");
        }

        var sortedIds = testIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expectation = new DiscoveryTargetExpectation(
            target,
            [
                .. sortedIds
                    .Select(FixtureType)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal),
            ],
            sortedIds.Length,
            Fingerprint(sortedIds),
            sortedIds);

        var targets = (existing?.Targets ?? [])
            .Where(item => item.Target != target)
            .Append(expectation)
            .OrderBy(item => item.Target, StringComparer.Ordinal)
            .ToArray();

        return new DiscoveryContractDocument(SchemaVersion, efCoreVersion, providerAssembly, targets);
    }

    internal static DiscoveryContractDocument Load(
        string path
    ) => ContractJson.Read<DiscoveryContractDocument>(path);

    internal static void Write(
        string path,
        DiscoveryContractDocument document
    ) => ContractJson.Write(path, document);

    /// <summary>
    /// Reconciles actual discovery against the exact committed fixture and test-ID set.
    /// </summary>
    internal static IReadOnlyList<string> Validate(
        DiscoveryContractDocument document,
        string target,
        IReadOnlyList<string> actualTestIds,
        bool requireAllTargets
    )
    {
        var errors = new List<string>();
        if (document.SchemaVersion != SchemaVersion)
        {
            errors.Add($"Discovery schema {document.SchemaVersion} is unsupported; " + $"expected {SchemaVersion}.");
        }

        var duplicateTargets = document
            .Targets.GroupBy(item => item.Target, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicateTarget in duplicateTargets)
        {
            errors.Add($"Duplicate discovery target '{duplicateTarget}'.");
        }

        if (requireAllTargets)
        {
            var actualTargets = document
                .Targets.Select(item => item.Target)
                .OrderBy(value => value, StringComparer.Ordinal);
            var expectedTargets =
                SpecificationBaseline.SupportedTargets.OrderBy(value => value, StringComparer.Ordinal);

            if (!actualTargets.SequenceEqual(expectedTargets, StringComparer.Ordinal))
            {
                errors.Add("Discovery contract must contain every supported target exactly once.");
            }
        }

        var expectation = document.Targets.FirstOrDefault(item => item.Target == target);
        if (expectation is null)
        {
            errors.Add($"Discovery contract has no target '{target}'.");
            return errors;
        }

        var duplicateIds = actualTestIds
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicateId in duplicateIds)
        {
            errors.Add($"Actual discovery contains duplicate test ID '{duplicateId}'.");
        }

        var uniqueActual = actualTestIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expected = expectation
            .TestIds.OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        foreach (var missing in expected.Except(uniqueActual, StringComparer.Ordinal))
        {
            errors.Add($"Discovery is missing expected test ID '{missing}'.");
        }

        foreach (var unexpected in uniqueActual.Except(expected, StringComparer.Ordinal))
        {
            errors.Add($"Discovery contains unexpected test ID '{unexpected}'.");
        }

        var actualFixtures = uniqueActual
            .Select(FixtureType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);

        if (!actualFixtures.SequenceEqual(
                expectation.FixtureTypes.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            errors.Add($"Discovery fixture set changed for target '{target}'.");
        }

        if (expectation.MinimumTestCount != expectation.TestIds.Count
            || uniqueActual.Length < expectation.MinimumTestCount)
        {
            errors.Add(
                $"Discovery count for '{target}' is {uniqueActual.Length}; "
                + $"minimum is {expectation.MinimumTestCount}.");
        }

        var expectedFingerprint = Fingerprint(expectation.TestIds);
        if (expectation.TestIdsSha256 != expectedFingerprint)
        {
            errors.Add($"Committed discovery fingerprint mismatch for target '{target}'.");
        }

        return errors;
    }

    internal static string Fingerprint(
        IEnumerable<string> testIds
    )
    {
        var content = string.Join("\n", testIds.OrderBy(value => value, StringComparer.Ordinal));
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
    }

    private static string FixtureType(
        string testId
    )
    {
        var parameters = testId.IndexOf('(', StringComparison.Ordinal);
        var searchEnd = parameters >= 0 ? parameters - 1 : testId.Length - 1;
        var separator = testId.LastIndexOf('.', searchEnd);

        return separator <= 0
            ? throw new InvalidDataException($"Test ID '{testId}' has no fixture separator.")
            : testId[..separator];
    }

    private static void ValidateTarget(
        string target
    )
    {
        if (!SpecificationBaseline.SupportedTargets.Contains(target, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown specification target '{target}'.", nameof(target));
        }
    }
}

internal sealed record DiscoveryContractDocument(
    int SchemaVersion,
    string EfCoreVersion,
    string ProviderAssembly,
    IReadOnlyList<DiscoveryTargetExpectation> Targets
);

internal sealed record DiscoveryTargetExpectation(
    string Target,
    IReadOnlyList<string> FixtureTypes,
    int MinimumTestCount,
    string TestIdsSha256,
    IReadOnlyList<string> TestIds
);
