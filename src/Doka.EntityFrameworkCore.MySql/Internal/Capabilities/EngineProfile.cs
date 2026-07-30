namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Immutable per-engine and per-version fact snapshot. Provider support is kept
/// out of this type and derived by <see cref="ProviderProfile"/>.
/// </summary>
internal sealed record EngineProfile(
    EngineFamily Family,
    Version Version,
    FrozenSet<EngineCapability> Capabilities)
{
    /// <summary>
    /// Returns whether the engine profile advertises the given fact or trait.
    /// </summary>
    public bool Has(
        EngineCapability capability
    ) => Capabilities.Contains(capability);
}
