namespace Doka.EntityFrameworkCore.MySql.NuGetSymbolReadback;

/// <summary>
/// Describes the exact symbol-server requests derived from candidate binaries.
/// </summary>
internal sealed record SymbolReadbackManifest(
    int SchemaVersion,
    string ReleaseVersion,
    IReadOnlyList<SymbolReadbackEntry> Symbols
);

/// <summary>
/// Binds one candidate assembly to the Portable PDB expected from NuGet.org.
/// </summary>
internal sealed record SymbolReadbackEntry(
    string PackageId,
    string PackageVersion,
    string AssemblyEntry,
    string PdbEntry,
    string PdbName,
    string SymbolKey,
    string SymbolUrl,
    string ChecksumHeader,
    string Sha256
);
