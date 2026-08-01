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
    /// Compares capability membership by value so cache eviction cannot change
    /// the identity of an otherwise identical engine profile.
    /// </summary>
    public bool Equals(
        EngineProfile? other
    ) => ReferenceEquals(this, other)
        || (other is not null
            && Family == other.Family
            && Version.Equals(other.Version)
            && Capabilities.SetEquals(other.Capabilities));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Family, Version);

    /// <summary>
    /// Returns whether the engine profile advertises the given fact or trait.
    /// </summary>
    public bool Has(
        EngineCapability capability
    ) => Capabilities.Contains(capability);
}
