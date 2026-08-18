namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Builds and persists the exact specification surface exposed by the restored EF Core
/// specification packages. The inventory follows the same base-type enumeration as
/// <see cref="RelationalComplianceTestBase"/>.
/// </summary>
internal static class SpecificationInventory
{
    internal const int SchemaVersion = 1;

    /// <summary>
    /// Creates a deterministic inventory for the EF Core packages loaded by this process.
    /// </summary>
    internal static SpecificationInventoryDocument Create(
        DateOnly retrievedAt
    )
    {
        var probe = new RelationalComplianceProbe();
        var baseTypes = probe
            .BaseTestClasses()
            .Distinct()
            .OrderBy(TypeId, StringComparer.Ordinal)
            .ToArray();

        var baseTypeIds = baseTypes
            .Select(TypeId)
            .ToHashSet(StringComparer.Ordinal);

        var testMethods = baseTypes.ToDictionary(
            type => type,
            type => new TestMethodSets(
                [
                    .. TestMethods(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                        .Select(TestMethodId)
                        .OrderBy(value => value, StringComparer.Ordinal),
                ],
                [
                    .. TestMethods(type, BindingFlags.Instance | BindingFlags.Public)
                        .Select(TestMethodId)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal),
                ]));

        var testMethodDictionary = testMethods
            .Values.SelectMany(methods => methods.Inherited)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var testMethodIndexes = testMethodDictionary
            .Select((
                testId,
                index
            ) => new
            {
                testId,
                index
            })
            .ToDictionary(
                item => item.testId,
                item => item.index,
                StringComparer.Ordinal);

        var relationalAssembly = typeof(RelationalComplianceTestBase).Assembly;
        var coreAssembly = typeof(ComplianceTestBase).Assembly;

        return new SpecificationInventoryDocument(
            SchemaVersion,
            PackageVersion(relationalAssembly),
            retrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AssemblyIdentity.Create(coreAssembly),
            AssemblyIdentity.Create(relationalAssembly),
            testMethodDictionary,
            [
                .. baseTypes.Select(type => CreateDescriptor(type, baseTypeIds, testMethods[type], testMethodIndexes)),
            ]);
    }

    /// <summary>
    /// Returns the exact EF Core specification-package version loaded by this process.
    /// </summary>
    internal static string CurrentEfCoreVersion() => PackageVersion(typeof(RelationalComplianceTestBase).Assembly);

    /// <summary>
    /// Loads an inventory document and rejects malformed JSON at the boundary.
    /// </summary>
    internal static SpecificationInventoryDocument Load(
        string path
    ) => ContractJson.Read<SpecificationInventoryDocument>(path);

    /// <summary>
    /// Writes an inventory with stable property ordering and an LF-terminated final line.
    /// </summary>
    internal static void Write(
        string path,
        SpecificationInventoryDocument inventory
    ) => ContractJson.Write(path, inventory);

    /// <summary>
    /// Returns the exact base types used by the official relational compliance assertion.
    /// </summary>
    internal static IReadOnlyList<Type> BaseTestClasses() =>
    [
        .. new RelationalComplianceProbe()
            .BaseTestClasses()
            .Distinct()
            .OrderBy(TypeId, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Returns a stable assembly-qualified identifier without embedding assembly versions.
    /// </summary>
    internal static string TypeId(
        Type type
    ) => $"{type.Assembly.GetName().Name}:{TypeName(type)}";

    /// <summary>
    /// Formats a type name deterministically, including constructed generic arguments.
    /// </summary>
    internal static string TypeName(
        Type type
    )
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return $"{TypeName(type.GetElementType()!)}[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tick = definitionName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            definitionName = definitionName[..tick];
        }

        return $"{definitionName}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>";
    }

    /// <summary>
    /// Mirrors EF Core's compliance implementation check for closed and open generic bases.
    /// </summary>
    internal static bool Implements(
        Type type,
        Type interfaceOrBaseType
    )
    {
        if (!(type.IsPublic || type.IsNestedPublic))
        {
            return false;
        }

        if (!interfaceOrBaseType.IsGenericTypeDefinition)
        {
            return interfaceOrBaseType.IsAssignableFrom(type);
        }

        var candidates = interfaceOrBaseType.IsInterface
            ? type
                .GetInterfaces()
                .AsEnumerable()
            : BaseTypes(type);

        return candidates
            .Append(type)
            .Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == interfaceOrBaseType);
    }

    private static SpecificationBaseDescriptor CreateDescriptor(
        Type type,
        HashSet<string> baseTypeIds,
        TestMethodSets testMethods,
        Dictionary<string, int> testMethodIndexes
    )
    {
        var directBaseTypeId = type.BaseType is not null && baseTypeIds.Contains(TypeId(type.BaseType))
            ? TypeId(type.BaseType)
            : null;

        return new SpecificationBaseDescriptor(
            TypeId(type),
            type.Assembly.GetName().Name!,
            TypeName(type),
            directBaseTypeId,
            type.IsAbstract,
            type.IsGenericTypeDefinition
                ? type.GetGenericArguments().Length
                : 0,
            SuiteDomain(type),
            FixtureContracts(type),
            [
                .. testMethods.Declared.Select(testId => testMethodIndexes[testId]),
            ],
            [
                .. testMethods.Inherited.Select(testId => testMethodIndexes[testId]),
            ]);
    }

    private static IReadOnlyList<string> FixtureContracts(
        Type type
    )
    {
        var constructors = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(constructor => string.Join(
                ",",
                constructor
                    .GetParameters()
                    .Select(parameter => TypeName(parameter.ParameterType))))
            .Select(parameters => $"constructor({parameters})");

        var genericContracts = type
            .GetGenericArguments()
            .Where(argument => argument.IsGenericParameter)
            .Select(argument =>
            {
                var constraints = argument
                    .GetGenericParameterConstraints()
                    .Select(TypeName)
                    .OrderBy(value => value, StringComparer.Ordinal);

                return $"generic({argument.Name}:{string.Join("&", constraints)})";
            });

        return
        [
            .. constructors
                .Concat(genericContracts)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<MethodInfo> TestMethods(
        Type type,
        BindingFlags bindingFlags
    ) => type
        .GetMethods(bindingFlags)
        .Where(method => method
            .GetCustomAttributesData()
            .Any(attribute => IsFactAttribute(attribute.AttributeType)));

    private static bool IsFactAttribute(
        Type type
    )
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Xunit.FactAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static string TestMethodId(
        MethodInfo method
    )
    {
        var parameters = string.Join(
            ",",
            method
                .GetParameters()
                .Select(parameter => TypeName(parameter.ParameterType)));

        return $"{TypeName(method.DeclaringType!)}.{method.Name}({parameters})";
    }

    private static string SuiteDomain(
        Type type
    )
    {
        var name = type.FullName ?? type.Name;

        if (ContainsAny(
                name,
                ".Migrations.",
                "Migration",
                ".Update.",
                "UpdateTestBase",
                "BulkUpdate",
                "SaveChanges",
                "StoreGenerated",
                "Transaction",
                "CommandInterception",
                "ConnectionInterception"))
        {
            return "migration-update";
        }

        if (ContainsAny(name, ".Scaffolding.", ".ModelBuilding.", "ModelBuilder", "DesignTime", "CompiledModel"))
        {
            return "design-time-modeling";
        }

        return ContainsAny(name, ".Query.", "Spatial", "TypeTestBase", "DataTypes", "JsonTypes")
            ? "query-storage-spatial"
            : "cross-cutting";
    }

    private static bool ContainsAny(
        string value,
        params string[] fragments
    ) => fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));

    private static string PackageVersion(
        Assembly assembly
    )
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException($"Assembly '{assembly.GetName().Name}' has no informational version.");
        }

        var buildMetadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        return buildMetadata >= 0
            ? informationalVersion[..buildMetadata]
            : informationalVersion;
    }

    private static IEnumerable<Type> BaseTypes(
        Type type
    )
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private sealed class RelationalComplianceProbe : RelationalComplianceTestBase
    {
        protected override Assembly TargetAssembly => typeof(RelationalComplianceProbe).Assembly;

        internal IEnumerable<Type> BaseTestClasses() => GetBaseTestClasses();
    }

    private sealed record TestMethodSets(
        IReadOnlyList<string> Declared,
        IReadOnlyList<string> Inherited
    );
}

internal sealed record SpecificationInventoryDocument(
    int SchemaVersion,
    string EfCoreVersion,
    string RetrievedAt,
    AssemblyIdentity CoreAssembly,
    AssemblyIdentity RelationalAssembly,
    IReadOnlyList<string> TestMethods,
    IReadOnlyList<SpecificationBaseDescriptor> BaseClasses
);

internal sealed record AssemblyIdentity(
    string Name,
    string AssemblyVersion,
    string InformationalVersion
)
{
    internal static AssemblyIdentity Create(
        Assembly assembly
    ) => new(
        assembly.GetName()
            .Name!,
        assembly
            .GetName()
            .Version?.ToString()
        ?? throw new InvalidOperationException($"Assembly '{assembly.GetName().Name}' has no assembly version."),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? throw new InvalidOperationException($"Assembly '{assembly.GetName().Name}' has no informational version."));
}

internal sealed record SpecificationBaseDescriptor(
    string Id,
    string Assembly,
    string Type,
    string? BaseTypeId,
    bool IsAbstract,
    int GenericArity,
    string SuiteDomain,
    IReadOnlyList<string> FixtureContracts,
    IReadOnlyList<int> DeclaredTestIndexes,
    IReadOnlyList<int> InheritedTestIndexes
);

/// <summary>
/// Centralizes the deterministic JSON representation shared by every specification contract.
/// </summary>
internal static class ContractJson
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static T Read<T>(
        string path
    ) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), s_options)
        ?? throw new InvalidDataException($"Contract file '{path}' contains JSON null.");

    internal static void Write<T>(
        string path,
        T value
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, s_options) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static string Serialize<T>(
        T value
    ) => JsonSerializer.Serialize(value, s_options);
}
