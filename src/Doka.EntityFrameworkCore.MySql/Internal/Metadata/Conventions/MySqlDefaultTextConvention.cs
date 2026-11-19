namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Applies application text defaults at convention precedence before physical mappings finalize.</summary>
internal sealed class MySqlDefaultTextConvention : IModelFinalizingConvention, IModelAnnotationChangedConvention
{
    private readonly MySqlSingletonOptions _options;

    /// <summary>Uses the provider's immutable singleton snapshot to resolve convention-level fallbacks.</summary>
    public MySqlDefaultTextConvention(
        IEnumerable<ISingletonOptions> singletonOptions
    )
    {
        ArgumentNullException.ThrowIfNull(singletonOptions);

        _options = singletonOptions
            .OfType<MySqlSingletonOptions>()
            .Single();
    }

    /// <summary>Reconciles weak fallbacks after a stronger facet changes, including later restoration.</summary>
    public void ProcessModelAnnotationChanged(
        IConventionModelBuilder modelBuilder,
        string name,
        IConventionAnnotation? annotation,
        IConventionAnnotation? oldAnnotation,
        IConventionContext<IConventionAnnotation> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        if (name is not (MySqlAnnotationNames.CharSet or RelationalAnnotationNames.Collation))
        {
            return;
        }

        ReconcileDefaults(modelBuilder, validateExplicitPair: false);
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        ReconcileDefaults(modelBuilder, validateExplicitPair: true);
    }

    private void ReconcileDefaults(
        IConventionModelBuilder modelBuilder,
        bool validateExplicitPair
    )
    {
        if (_options.DefaultCharSet is null
            && _options.DefaultCollation is null)
        {
            return;
        }

        if (_options.Profile is not { } profile)
        {
            if (validateExplicitPair)
            {
                throw new InvalidOperationException(
                    "A MySQL provider profile must be initialized before model defaults.");
            }

            return;
        }

        var model = modelBuilder.Metadata;
        var charSetAnnotation = model.FindAnnotation(MySqlAnnotationNames.CharSet);
        var collationAnnotation = model.FindAnnotation(RelationalAnnotationNames.Collation);
        var weakCharSet = IsFallback(charSetAnnotation, _options.DefaultCharSet);
        var weakCollation = IsFallback(collationAnnotation, _options.DefaultCollation);
        var strongCharSet = weakCharSet
            ? null
            : charSetAnnotation?.Value as string;

        var strongCollation = weakCollation
            ? null
            : collationAnnotation?.Value as string;

        // WHY: Two explicit setters may temporarily disagree while a caller replaces the pair.
        // Finalizing/DDL validation still rejects an unresolved explicit conflict.
        if (!validateExplicitPair
            && !MySqlTextDefaults.IsCompatible(strongCharSet, strongCollation, profile))
        {
            return;
        }

        var effective = MySqlTextDefaults.Resolve(
            _options.DefaultCharSet,
            _options.DefaultCollation,
            strongCharSet,
            strongCollation,
            profile);

        // WHY: Resolve from stronger intent, not the previous fallback. This both removes a now
        // incompatible fallback and restores it after a later independent convention makes it valid.
        ApplyFacet(modelBuilder, MySqlAnnotationNames.CharSet, charSetAnnotation, weakCharSet, effective.CharSet);
        ApplyFacet(modelBuilder,
            RelationalAnnotationNames.Collation,
            collationAnnotation,
            weakCollation,
            effective.Collation);
    }

    private static bool IsFallback(
        IConventionAnnotation? annotation,
        string? configuredDefault
    ) => annotation?.GetConfigurationSource() == ConfigurationSource.Convention
        && configuredDefault is not null
        && string.Equals(annotation.Value as string, configuredDefault, StringComparison.Ordinal);

    private static void ApplyFacet(
        IConventionModelBuilder modelBuilder,
        string name,
        IConventionAnnotation? annotation,
        bool weakFallback,
        string? effective
    )
    {
        if (annotation is not null
            && !weakFallback)
        {
            return;
        }

        if (effective is null)
        {
            if (annotation is not null)
            {
                modelBuilder.HasNoAnnotation(name);
            }

            return;
        }

        // WHY: Annotation callbacks are reentrant. Equal values do no work, so reconciliation
        // terminates without a shared mutable guard or a per-model state registry.
        if (!string.Equals(annotation?.Value as string, effective, StringComparison.Ordinal))
        {
            modelBuilder.HasAnnotation(name, effective);
        }
    }
}
