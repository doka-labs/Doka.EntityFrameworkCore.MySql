namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides an immutable migration-capability view for the configured server
/// profile.
/// </summary>
/// <remarks>
/// The view is projected from the provider's canonical engine and provider
/// capability contracts. It intentionally contains no independent version
/// table that could drift from runtime behavior.
/// </remarks>
public sealed class MySqlMigrationFeatureSet
{
    private readonly ProviderProfile _profile;

    internal MySqlMigrationFeatureSet(
        ProviderProfile profile
    )
    {
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;
    }

    /// <summary>
    /// Gets the support route for one migration-facing capability.
    /// </summary>
    /// <param name="feature">The feature to inspect.</param>
    /// <returns>The support route for the configured server profile.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="feature"/> is not a defined value.
    /// </exception>
    public MySqlMigrationFeatureSupport GetSupport(
        MySqlMigrationFeature feature
    )
    {
        var providerCapability = feature switch
        {
            MySqlMigrationFeature.SchemaOperations => ProviderCapability.SchemaOperations,
            MySqlMigrationFeature.JsonColumns => ProviderCapability.JsonColumns,
            MySqlMigrationFeature.CheckConstraints => ProviderCapability.CheckConstraints,
            MySqlMigrationFeature.DescendingIndexes => ProviderCapability.DescendingIndexes,
            MySqlMigrationFeature.FilteredIndexes => ProviderCapability.FilteredIndexes,
            MySqlMigrationFeature.FunctionalIndexes => ProviderCapability.FunctionalIndexes,
            MySqlMigrationFeature.IndexPrefixLengths => ProviderCapability.IndexPrefixLengths,
            MySqlMigrationFeature.RenameColumn => ProviderCapability.RenameColumn,
            MySqlMigrationFeature.RenameIndex => ProviderCapability.RenameIndex,
            MySqlMigrationFeature.GeneratedColumnNullabilityClause =>
                ProviderCapability.GeneratedColumnNullabilityClause,
            MySqlMigrationFeature.VirtualGeneratedColumns => ProviderCapability.VirtualGeneratedColumns,
            MySqlMigrationFeature.StoredGeneratedColumns => ProviderCapability.StoredGeneratedColumns,
            MySqlMigrationFeature.SpatialColumnSridAttribute => ProviderCapability.SpatialColumnSridAttribute,
            MySqlMigrationFeature.ExpressionDefaults => ProviderCapability.ExpressionDefaults,
            MySqlMigrationFeature.TemporalTables => ProviderCapability.TemporalTables,
            MySqlMigrationFeature.ApplicationTimePeriods => ProviderCapability.ApplicationTimePeriods,
            MySqlMigrationFeature.BitemporalTables => ProviderCapability.BitemporalTables,
            MySqlMigrationFeature.Sequences => ProviderCapability.Sequences,
            MySqlMigrationFeature.PreparedDdl => ProviderCapability.PreparedDdl,
            MySqlMigrationFeature.AtomicDdl => ProviderCapability.AtomicDdl,
            MySqlMigrationFeature.TransactionalDdl => ProviderCapability.TransactionalDdl,
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };

        return _profile.GetSupport(providerCapability) switch
        {
            ProviderSupportStatus.Native => MySqlMigrationFeatureSupport.Native,
            ProviderSupportStatus.Emulated => MySqlMigrationFeatureSupport.Emulated,
            ProviderSupportStatus.UnsupportedByEngine => MySqlMigrationFeatureSupport.Unsupported,
            _ => throw new UnreachableException(),
        };
    }
}
