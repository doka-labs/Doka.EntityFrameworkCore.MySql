namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provider support view derived from one immutable <see cref="EngineProfile"/>.
/// The switch is exhaustive so adding a provider capability requires an explicit
/// implementation route for every engine profile.
/// </summary>
internal sealed record ProviderProfile(EngineProfile Engine)
{
    /// <summary>
    /// Returns how the provider supplies the requested capability.
    /// </summary>
    public ProviderSupportStatus GetSupport(
        ProviderCapability capability
    ) => capability switch
    {
        ProviderCapability.JsonColumns =>
            Engine.Has(EngineCapability.NativeJsonType) ? ProviderSupportStatus.Native :
            Engine.Has(EngineCapability.JsonValidationFunction) ? ProviderSupportStatus.Emulated :
            ProviderSupportStatus.UnsupportedByEngine,
        ProviderCapability.ReturningClause => NativeWhen(EngineCapability.ReturningClause),
        ProviderCapability.Savepoints => NativeWhen(EngineCapability.Savepoints),
        ProviderCapability.GeneratedColumnNullabilityClause =>
            NativeWhen(EngineCapability.GeneratedColumnNullabilityClause),
        ProviderCapability.VirtualGeneratedColumns => NativeWhen(EngineCapability.VirtualGeneratedColumns),
        ProviderCapability.StoredGeneratedColumns => NativeWhen(EngineCapability.StoredGeneratedColumns),
        ProviderCapability.SpatialColumnSridAttribute => NativeWhen(EngineCapability.SpatialColumnSridAttribute),
        ProviderCapability.CommonTableExpressions => NativeWhen(EngineCapability.CommonTableExpressions),
        ProviderCapability.TemporalTables => Engine.Has(EngineCapability.SystemVersionedTables)
            ? ProviderSupportStatus.Native
            : Engine.Family == EngineFamily.MySql
                ? ProviderSupportStatus.Emulated
                : ProviderSupportStatus.UnsupportedByEngine,
        ProviderCapability.Sequences =>
            Engine.Has(EngineCapability.NativeSequences)
                ? ProviderSupportStatus.Native
                : ProviderSupportStatus.Emulated,
        ProviderCapability.RenameColumn =>
            Engine.Has(EngineCapability.RenameColumnSyntax)
                ? ProviderSupportStatus.Native
                : ProviderSupportStatus.Emulated,
        ProviderCapability.LateralDerivedTables => NativeWhen(EngineCapability.LateralDerivedTables),
        ProviderCapability.SelfReferencingMutations =>
            Engine.Has(EngineCapability.SelfReferencingMutationRequiresIsolation)
                ? ProviderSupportStatus.Emulated
                : ProviderSupportStatus.Native,
        ProviderCapability.FunctionalIndexScaffolding => NativeWhen(EngineCapability.FunctionalIndexExpressionMetadata),
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };

    /// <summary>
    /// Returns whether the provider can supply the requested capability either
    /// natively or through its own emulation.
    /// </summary>
    public bool Supports(
        ProviderCapability capability
    ) => GetSupport(capability) != ProviderSupportStatus.UnsupportedByEngine;

    private ProviderSupportStatus NativeWhen(
        EngineCapability capability
    ) => Engine.Has(capability) ? ProviderSupportStatus.Native : ProviderSupportStatus.UnsupportedByEngine;
}
