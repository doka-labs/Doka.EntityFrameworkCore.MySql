namespace Doka.EntityFrameworkCore.MySql.SpecificationContract;

/// <summary>
/// Provides deterministic generation and validation commands for the specification contract.
/// </summary>
internal static class Program
{
    internal static int Main(
        string[] args
    )
    {
        try
        {
            if (args.Length == 0
                || args[0] is "--help" or "-h")
            {
                WriteUsage();
                return args.Length == 0 ? 2 : 0;
            }

            var command = args[0];
            var options = CommandOptions.Parse(args[1..]);
            return command switch
            {
                "inventory" => WriteInventory(options),
                "baseline" => WriteBaseline(options),
                "debt" => WriteDebt(options),
                "validate" => Validate(options, publication: false),
                "publication" => Validate(options, publication: true),
                "discovery-update" => UpdateDiscovery(options),
                "discovery-validate" => ValidateDiscovery(options),
                "trx" => ValidateTrx(options),
                _ => throw new ArgumentException($"Unknown command '{command}'.", nameof(args)),
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WriteUsage();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Specification contract command failed unexpectedly: {exception.Message}");
            return 2;
        }
    }

    private static int WriteInventory(
        CommandOptions options
    )
    {
        var retrievedAtText = options.RequiredSingle("--retrieved-at");
        if (!DateOnly.TryParseExact(
                retrievedAtText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var retrievedAt))
        {
            throw new ArgumentException($"--retrieved-at must use yyyy-MM-dd, found '{retrievedAtText}'.");
        }

        var output = options.RequiredSingle("--output");
        var inventory = SpecificationInventory.Create(retrievedAt);
        SpecificationInventory.Write(output, inventory);
        Console.WriteLine(
            $"Wrote {inventory.BaseClasses.Count} EF Core {inventory.EfCoreVersion} "
            + $"specification bases to {output}.");
        return 0;
    }

    private static int WriteBaseline(
        CommandOptions options
    )
    {
        var inventories = options
            .RequiredMany("--inventory")
            .Select(SpecificationInventory.Load)
            .ToArray();
        var output = options.RequiredSingle("--output");
        var provider = options.RequiredSingle("--provider");
        var baseline = SpecificationBaseline.Create(inventories, provider);
        SpecificationBaseline.Write(output, baseline);
        Console.WriteLine(
            $"Wrote specification baseline with {baseline.Entries.Count} bases and "
            + $"{baseline.InitialProviderGapCount} provider-debt entries to {output}.");
        return 0;
    }

    private static int WriteDebt(
        CommandOptions options
    )
    {
        var root = Path.GetFullPath(options.RequiredSingle("--root"));
        var provider = options.RequiredSingle("--provider");
        var report = SpecificationContractValidator.ValidateRepository(root, provider);

        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine(error);
        }

        if (!report.IsValid)
        {
            return 1;
        }

        var output = options.RequiredSingle("--output");
        SpecificationDebtReport.Write(output, report);
        Console.WriteLine(
            $"Wrote {report.CurrentProviderGapCount} current EF Core {report.EfCoreVersion} "
            + $"provider-debt IDs to {output}.");

        return 0;
    }

    private static int Validate(
        CommandOptions options,
        bool publication
    )
    {
        var root = Path.GetFullPath(options.RequiredSingle("--root"));
        var provider = options.RequiredSingle("--provider");
        var report = publication
            ? SpecificationContractValidator.ValidatePublication(root, provider)
            : SpecificationContractValidator.ValidateRepository(root, provider);

        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.WriteLine(
            $"EF Core {report.EfCoreVersion}: provider suite debt "
            + $"{report.CurrentProviderGapCount}/{report.InitialProviderGapCount}.");
        return report.IsValid ? 0 : 1;
    }

    private static int UpdateDiscovery(
        CommandOptions options
    )
    {
        var contractPath = options.RequiredSingle("--contract");
        var output = File.ReadAllText(options.RequiredSingle("--actual"));
        var testIds = DiscoveryContract.ParseListOutput(output);
        var providerPath = options.RequiredSingle("--provider");
        var providerAssembly = AssemblyName.GetAssemblyName(providerPath)
            .Name!;
        var efCoreVersion = SpecificationInventory.CurrentEfCoreVersion();
        var existing = File.Exists(contractPath) ? DiscoveryContract.Load(contractPath) : null;
        var updated = DiscoveryContract.Update(
            existing,
            efCoreVersion,
            providerAssembly,
            options.RequiredSingle("--target"),
            testIds);

        DiscoveryContract.Write(contractPath, updated);
        Console.WriteLine($"Wrote {testIds.Count} exact discovery IDs to {contractPath}.");

        return 0;
    }

    private static int ValidateDiscovery(
        CommandOptions options
    )
    {
        var root = Path.GetFullPath(options.RequiredSingle("--root"));
        var version = SpecificationInventory.CurrentEfCoreVersion();
        var contractPath = DiscoveryPath(root, version);
        var document = DiscoveryContract.Load(contractPath);
        var actual = DiscoveryContract.ParseListOutput(File.ReadAllText(options.RequiredSingle("--actual")));
        var target = options.RequiredSingle("--target");
        var errors = DiscoveryContract.Validate(document, target, actual, requireAllTargets: true);

        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.WriteLine($"EF Core {version} target {target}: discovered {actual.Count} " + "specification test IDs.");

        return errors.Count == 0 ? 0 : 1;
    }

    private static int ValidateTrx(
        CommandOptions options
    )
    {
        var root = Path.GetFullPath(options.RequiredSingle("--root"));
        var version = SpecificationInventory.CurrentEfCoreVersion();
        var discovery = DiscoveryContract.Load(DiscoveryPath(root, version));
        var target = options.RequiredSingle("--target");
        var trxPaths = ExpandTrxPaths(options.RequiredMany("--trx"));
        if (trxPaths.Count == 0)
        {
            throw new ArgumentException("No TRX files were found for --trx.");
        }

        var dispositions = Path.Combine(
            root,
            "tests",
            "Doka.EntityFrameworkCore.MySql.FunctionalTests",
            "Specification",
            "SpecDispositions.json");

        var report = TrxContract.Validate(discovery, target, trxPaths, dispositions);

        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.WriteLine(
            $"EF Core {version} target {target}: TRX total={report.Total}, "
            + $"passed={report.Passed}, notExecuted={report.NotExecuted}, "
            + $"failed={report.Failed}.");

        return report.IsValid ? 0 : 1;
    }

    private static IReadOnlyList<string> ExpandTrxPaths(
        IReadOnlyList<string> paths
    ) =>
    [
        .. paths
            .SelectMany(path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.trx", SearchOption.AllDirectories)
                : [path])
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal),
    ];

    private static string DiscoveryPath(
        string root,
        string version
    ) => Path.Combine(
        root,
        "tests",
        "Doka.EntityFrameworkCore.MySql.FunctionalTests",
        "Specification",
        "Contracts",
        $"SpecDiscovery.{version}.json");

    private static void WriteUsage() => Console.WriteLine(
        """
        Usage: SpecificationContract <command> [options]

          inventory          --retrieved-at yyyy-MM-dd --output <json>
          baseline           --inventory <json> [--inventory <json>] --provider <dll> --output <json>
          debt               --root <repository> --provider <dll> --output <json>
          validate           --root <repository> --provider <dll>
          publication        --root <repository> --provider <dll>
          discovery-update   --contract <json> --actual <list-output> --provider <dll> --target <target>
          discovery-validate --root <repository> --actual <list-output> --target <target>
          trx                --root <repository> --trx <file-or-directory> [--trx <path>] --target <target>
        """);
}

internal sealed class CommandOptions(IReadOnlyDictionary<string, IReadOnlyList<string>> values)
{
    internal static CommandOptions Parse(
        string[] args
    )
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index]
                    .StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{args[index]}' requires a --name value pair.", nameof(args));
            }

            if (!values.TryGetValue(args[index], out var optionValues))
            {
                optionValues = [];
                values.Add(args[index], optionValues);
            }

            optionValues.Add(args[index + 1]);
        }

        return new CommandOptions(
            values.ToDictionary(pair => pair.Key, IReadOnlyList<string> (pair) => pair.Value, StringComparer.Ordinal));
    }

    internal string RequiredSingle(
        string name
    )
    {
        var optionValues = RequiredMany(name);

        return optionValues.Count != 1
            ? throw new ArgumentException($"Option '{name}' must occur exactly once; found {optionValues.Count}.")
            : optionValues[0];
    }

    internal IReadOnlyList<string> RequiredMany(
        string name
    )
    {
        if (!values.TryGetValue(name, out var optionValues)
            || optionValues.Count == 0)
        {
            throw new ArgumentException($"Required option '{name}' is missing.");
        }

        return optionValues;
    }
}
