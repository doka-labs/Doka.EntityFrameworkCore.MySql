namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed class SymbolReadbackManifestBuilderTests
{
    private const string Version = "10.0.0-test";

    [Fact]
    public void Candidate_assemblies_produce_checksum_bound_public_symbol_probes()
    {
        var candidateRoot = CreateCandidateRoot();
        try
        {
            WritePackagePair(
                candidateRoot,
                "Doka.EntityFrameworkCore.MySql",
                typeof(MySqlDbContextOptionsBuilderExtensions).Assembly.Location);
            WritePackagePair(
                candidateRoot,
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
                typeof(MySqlNetTopologySuiteDbContextOptionsBuilderExtensions).Assembly.Location);

            var manifest = NuGetSymbolReadbackManifestBuilder.Build(candidateRoot, Version);

            Assert.Equal(2, manifest.Symbols.Count);
            Assert.All(
                manifest.Symbols,
                symbol =>
                {
                    Assert.EndsWith("FFFFFFFF", symbol.SymbolKey, StringComparison.Ordinal);
                    Assert.StartsWith("SHA256:", symbol.ChecksumHeader, StringComparison.Ordinal);
                    Assert.Equal(71, symbol.ChecksumHeader.Length);
                    Assert.StartsWith(
                        "https://symbols.nuget.org/download/symbols/",
                        symbol.SymbolUrl,
                        StringComparison.Ordinal);
                });
        }
        finally
        {
            Directory.Delete(candidateRoot, recursive: true);
        }
    }

    [Fact]
    public void Candidate_symbols_must_match_the_checksum_sealed_into_the_assembly()
    {
        var candidateRoot = CreateCandidateRoot();
        try
        {
            WritePackagePair(
                candidateRoot,
                "Doka.EntityFrameworkCore.MySql",
                typeof(MySqlDbContextOptionsBuilderExtensions).Assembly.Location,
                corruptSymbols: true);
            WritePackagePair(
                candidateRoot,
                "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
                typeof(MySqlNetTopologySuiteDbContextOptionsBuilderExtensions).Assembly.Location);

            var exception =
                Assert.Throws<InvalidDataException>(() =>
                    NuGetSymbolReadbackManifestBuilder.Build(candidateRoot, Version));

            Assert.Contains(
                "does not match the checksum sealed into its assembly",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(candidateRoot, recursive: true);
        }
    }

    private static string CreateCandidateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"doka-symbol-readback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        return root;
    }

    private static void WritePackagePair(
        string candidateRoot,
        string packageId,
        string assemblyPath,
        bool corruptSymbols = false
    )
    {
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        var assemblyEntry = $"lib/net10.0/{packageId}.dll";
        var pdbEntry = $"lib/net10.0/{packageId}.pdb";
        var packagesRoot = Path.Combine(candidateRoot, "packages");

        using (var package = ZipFile.Open(
                   Path.Combine(packagesRoot, $"{packageId}.{Version}.nupkg"),
                   ZipArchiveMode.Create))
        {
            package.CreateEntryFromFile(assemblyPath, assemblyEntry);
        }

        var pdb = File.ReadAllBytes(pdbPath);
        if (corruptSymbols)
        {
            pdb[^1] ^= 0xff;
        }

        using var symbols = ZipFile.Open(
            Path.Combine(packagesRoot, $"{packageId}.{Version}.snupkg"),
            ZipArchiveMode.Create);

        var entry = symbols.CreateEntry(pdbEntry);
        using var stream = entry.Open();
        stream.Write(pdb);
    }
}
