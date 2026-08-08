using System.Threading;

namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Captures the immutable prepublication provider-suite baseline. Implemented mappings must
/// remain present, while debt entries may only transition from missing to implemented.
/// </summary>
internal static class SpecificationBaseline
{
    internal const int SchemaVersion = 1;

    internal static readonly string[] SupportedTargets =
    [
        "mysql84",
        "mariadb114",
        "mariadb118",
    ];

    /// <summary>
    /// Creates the initial baseline from reviewed version inventories and the current provider
    /// test assembly.
    /// </summary>
    internal static SpecificationBaselineDocument Create(
        IReadOnlyList<SpecificationInventoryDocument> inventories,
        string providerAssemblyPath
    )
    {
        if (inventories.Count == 0)
        {
            throw new ArgumentException("At least one inventory is required.", nameof(inventories));
        }

        var providerAssembly = ProviderAssembly.Load(providerAssemblyPath);
        var currentBaseTypes = SpecificationInventory
            .BaseTestClasses()
            .ToDictionary(SpecificationInventory.TypeId, StringComparer.Ordinal);

        var providerTypes = ConcretePublicTypes(providerAssembly);
        var descriptors = inventories
            .SelectMany(inventory => inventory.BaseClasses)
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToArray();

        var entries = descriptors
            .Select(descriptor => CreateEntry(descriptor, currentBaseTypes, providerTypes, inventories))
            .ToArray();

        var initialProviderGapCount = entries.Count(entry => entry.BaselineState == "provider-debt");

        return new SpecificationBaselineDocument(
            SchemaVersion,
            [
                .. inventories
                    .Select(inventory => inventory.EfCoreVersion)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal),
            ],
            SupportedTargets,
            "publication-zero-gap",
            initialProviderGapCount,
            AssignmentFingerprint(entries),
            entries);
    }

    internal static SpecificationBaselineDocument Load(
        string path
    ) => ContractJson.Read<SpecificationBaselineDocument>(path);

    internal static void Write(
        string path,
        SpecificationBaselineDocument baseline
    ) => ContractJson.Write(path, baseline);

    internal static string AssignmentFingerprint(
        IEnumerable<SpecificationBaselineEntry> entries
    )
    {
        var contract = string.Join(
            "\n",
            entries
                .Where(entry => entry.BaselineState == "provider-debt")
                .OrderBy(entry => entry.UpstreamBaseId, StringComparer.Ordinal)
                .Select(entry =>
                    $"{entry.UpstreamBaseId}|{entry.ClosurePhase}|{entry.OwnerSurface}|{entry.ExpiresAt}"));

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract)))
            .ToLowerInvariant();
    }

    internal static IReadOnlyList<Type> ConcretePublicTypes(
        Assembly providerAssembly
    ) =>
    [
        .. providerAssembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && (type.IsPublic || type.IsNestedPublic))
            .OrderBy(type => type.FullName, StringComparer.Ordinal),
    ];

    private static SpecificationBaselineEntry CreateEntry(
        SpecificationBaseDescriptor descriptor,
        Dictionary<string, Type> currentBaseTypes,
        IReadOnlyList<Type> providerTypes,
        IReadOnlyList<SpecificationInventoryDocument> inventories
    )
    {
        string[] mappings = currentBaseTypes.TryGetValue(descriptor.Id, out var baseType)
            ?
            [
                .. providerTypes
                    .Where(type => SpecificationInventory.Implements(type, baseType))
                    .Select(type => type.FullName!)
                    .OrderBy(value => value, StringComparer.Ordinal),
            ]
            : [];

        var isImplemented = mappings.Length > 0;
        var isOfficiallyExempt = descriptor.Type == "Microsoft.EntityFrameworkCore.NonSharedModelTestBase";
        int? closurePhase = isImplemented || isOfficiallyExempt ? null : ClosurePhase(descriptor.SuiteDomain);

        return new SpecificationBaselineEntry(
            descriptor.Id,
            [
                .. inventories
                    .Where(inventory => inventory.BaseClasses.Any(item => item.Id == descriptor.Id))
                    .Select(inventory => inventory.EfCoreVersion)
                    .OrderBy(value => value, StringComparer.Ordinal),
            ],
            descriptor.SuiteDomain,
            isImplemented ? "implemented" : isOfficiallyExempt ? "compliance-exempt" : "provider-debt",
            mappings,
            closurePhase,
            OwnerSurface(descriptor.SuiteDomain),
            SupportedTargets,
            isImplemented ? "Concrete public provider specification type present at baseline." :
            isOfficiallyExempt ? "The official ComplianceTestBase excludes NonSharedModelTestBase." :
            "No concrete public provider type implemented this official base at baseline.",
            "publication-zero-gap",
            null);
    }

    private static int ClosurePhase(
        string suiteDomain
    ) => suiteDomain switch
    {
        "migration-update" => 4,
        "design-time-modeling" => 5,
        "query-storage-spatial" or "cross-cutting" => 6,
        _ => throw new InvalidOperationException($"Unknown suite domain '{suiteDomain}'."),
    };

    private static string OwnerSurface(
        string suiteDomain
    ) => suiteDomain switch
    {
        "migration-update" => "migrations-updates-transactions",
        "design-time-modeling" => "design-time-scaffolding-modeling",
        "query-storage-spatial" => "query-storage-spatial",
        "cross-cutting" => "provider-cross-cutting",
        _ => throw new InvalidOperationException($"Unknown suite domain '{suiteDomain}'."),
    };
}

internal sealed record SpecificationBaselineDocument(
    int SchemaVersion,
    IReadOnlyList<string> EfCoreVersions,
    IReadOnlyList<string> SupportedTargets,
    string PublicationGate,
    int InitialProviderGapCount,
    string AssignmentFingerprint,
    IReadOnlyList<SpecificationBaselineEntry> Entries
);

internal sealed record SpecificationBaselineEntry(
    string UpstreamBaseId,
    IReadOnlyList<string> EfCoreVersions,
    string SuiteDomain,
    string BaselineState,
    IReadOnlyList<string> ProviderTypes,
    int? ClosurePhase,
    string OwnerSurface,
    IReadOnlyList<string> Targets,
    string Evidence,
    string ExpiresAt,
    string? ClosureEvidence
);

/// <summary>
/// Loads a provider test assembly while resolving its private dependencies from the same output
/// directory. EF Core assemblies already loaded by the contract tool retain one type identity.
/// </summary>
internal static class ProviderAssembly
{
    private static readonly Lock s_sync = new();
    private static readonly HashSet<string> s_probeDirectories = new(StringComparer.Ordinal);

    static ProviderAssembly()
    {
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    internal static Assembly Load(
        string path
    )
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Provider test assembly not found.", fullPath);
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        lock (s_sync)
        {
            s_probeDirectories.Add(directory);
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static Assembly? Resolve(
        AssemblyLoadContext context,
        AssemblyName name
    )
    {
        lock (s_sync)
        {
            foreach (var directory in s_probeDirectories)
            {
                var candidate = Path.Combine(directory, $"{name.Name}.dll");
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }
        }

        return null;
    }
}
