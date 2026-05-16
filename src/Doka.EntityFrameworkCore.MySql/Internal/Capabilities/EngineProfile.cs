namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Per-engine + per-version capability snapshot. Replaces the flat 16-field
/// <c>ServerCapabilities</c> record with a sparse keyed shape so adding a new
/// capability becomes a single static-table entry append rather than a record-
/// field add plus a per-engine boolean computation. Capabilities that no provider
/// code branches on stay absent from the dictionary (the previous record had three
/// such dead-knobs that always returned true).
/// </summary>
internal sealed record EngineProfile(
    EngineFamily Family,
    Version Version,
    FrozenSet<Capability> Capabilities)
{
    /// <summary>
    /// Returns whether the profile advertises the given capability.
    /// </summary>
    public bool Has(
        Capability capability
    ) => Capabilities.Contains(capability);
}
