namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies candidate package symbols remain bound to their assembly checksums.</summary>
public sealed class SymbolReadbackManifestBuilderTests
{
    private const string Version = "10.0.0-test";

    /// <summary>
    /// Verifies candidate assemblies produce checksum bound public symbol probes.
    /// </summary>
    [Fact]
    public void Candidate_assemblies_produce_checksum_bound_public_symbol_probes()
    {
        // Arrange
        var candidateRoot = CreateCandidateRoot();

        try
        {
            WriteCandidatePackages(candidateRoot);

            // Act
            var manifest = NuGetSymbolReadbackManifestBuilder.Build(candidateRoot, Version);

            // Assert
            Assert.Equal(3, manifest.Symbols.Count);
            Assert.Contains(manifest.Symbols, symbol => symbol.PackageId == "Doka.Caching.MySql");
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

    /// <summary>
    /// Verifies each candidate symbol package must match its own assembly checksum.
    /// </summary>
    /// <param name="packageId">The package whose PDB bytes are deliberately corrupted.</param>
    [Theory]
    [InlineData("Doka.Caching.MySql")]
    [InlineData("Doka.EntityFrameworkCore.MySql")]
    [InlineData("Doka.EntityFrameworkCore.MySql.NetTopologySuite")]
    public void Candidate_symbols_must_match_the_checksum_sealed_into_the_assembly(
        string packageId
    )
    {
        // Arrange
        var candidateRoot = CreateCandidateRoot();

        try
        {
            // WHY: Every other package stays valid, so this case cannot pass by
            // rejecting an incomplete candidate before checking the corrupted PDB.
            WriteCandidatePackages(candidateRoot, corruptPackageId: packageId);

            // Act
            var caught = Record.Exception(() =>
                NuGetSymbolReadbackManifestBuilder.Build(candidateRoot, Version));

            // Assert
            var exception = Assert.IsType<InvalidDataException>(caught);

            Assert.Contains(packageId + ".pdb", exception.Message, StringComparison.Ordinal);
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

    private static void WriteCandidatePackages(
        string candidateRoot,
        string? corruptPackageId = null
    )
    {
        WritePackagePair(
            candidateRoot,
            "Doka.EntityFrameworkCore.MySql",
            typeof(MySqlDbContextOptionsBuilderExtensions).Assembly.Location,
            corruptSymbols: corruptPackageId == "Doka.EntityFrameworkCore.MySql");
        WritePackagePair(
            candidateRoot,
            "Doka.EntityFrameworkCore.MySql.NetTopologySuite",
            typeof(MySqlNetTopologySuiteDbContextOptionsBuilderExtensions).Assembly.Location,
            corruptSymbols: corruptPackageId == "Doka.EntityFrameworkCore.MySql.NetTopologySuite");
        WritePackagePair(
            candidateRoot,
            "Doka.Caching.MySql",
            typeof(Caching.MySql.MySqlCacheSchema).Assembly.Location,
            corruptSymbols: corruptPackageId == "Doka.Caching.MySql");
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
        var assemblyEntry = $"lib/net11.0/{packageId}.dll";
        var pdbEntry = $"lib/net11.0/{packageId}.pdb";
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
