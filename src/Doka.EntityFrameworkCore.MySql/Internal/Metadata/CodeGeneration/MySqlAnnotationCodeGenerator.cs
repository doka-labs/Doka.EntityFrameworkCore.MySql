namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlAnnotationCodeGenerator : AnnotationCodeGenerator
{
    public MySqlAnnotationCodeGenerator(
        AnnotationCodeGeneratorDependencies dependencies
    ) : base(dependencies) { }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IModel model,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(model, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.CharSet, out var charSetAnnotation)
            && charSetAnnotation.Value is string charSet
            && !string.IsNullOrWhiteSpace(charSet))
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlModelBuilderExtensions.HasCharSet), charSet));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IEntityType entityType,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(annotations);

        // The model-code generator emits both temporal contracts through the strongly
        // typed table-builder APIs. Removing these annotations here prevents a second,
        // provider-internal HasAnnotation representation from leaking into user code.
        annotations.Remove(MySqlAnnotationNames.IsTemporal);
        annotations.Remove(MySqlAnnotationNames.TemporalHistoryTableName);
        annotations.Remove(MySqlAnnotationNames.TemporalHistoryTableSchema);
        annotations.Remove(MySqlAnnotationNames.TemporalPeriodStartPropertyName);
        annotations.Remove(MySqlAnnotationNames.TemporalPeriodEndPropertyName);
        annotations.Remove(MySqlAnnotationNames.IsApplicationTime);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodStartPropertyName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodEndPropertyName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps);

        var fragments = base
            .GenerateFluentApiCalls(entityType, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.CharSet, out var charSetAnnotation)
            && charSetAnnotation.Value is string charSet
            && !string.IsNullOrWhiteSpace(charSet))
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlEntityTypeBuilderExtensions.HasCharSet), charSet));
        }

        if (annotations.Remove(MySqlAnnotationNames.StorageEngine, out var storageEngineAnnotation)
            && storageEngineAnnotation.Value is string storageEngine
            && !string.IsNullOrWhiteSpace(storageEngine))
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlEntityTypeBuilderExtensions.UseStorageEngine), storageEngine));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IKey key,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(key, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, out var annotation)
            && annotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlKeyBuilderExtensions.UseWithoutOverlaps)));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IProperty property,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(property, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.GuidFormat, out var guidFormatAnnotation)
            && guidFormatAnnotation.Value is MySqlGuidFormat guidFormat)
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlPropertyBuilderExtensions.HasMySqlGuidFormat), guidFormat));
        }

        if (annotations.Remove(MySqlAnnotationNames.ValueGenerationStrategy, out var valueGenerationAnnotation)
            && valueGenerationAnnotation.Value is MySqlValueGenerationStrategy valueGenerationStrategy)
        {
            fragments.Add(
                new MethodCallCodeFragment(
                    nameof(MySqlPropertyBuilderExtensions.HasMySqlValueGenerationStrategy),
                    valueGenerationStrategy));
        }

        if (annotations.Remove(
                MySqlAnnotationNames.SpatialReferenceSystemId,
                out var spatialReferenceSystemIdAnnotation)
            && spatialReferenceSystemIdAnnotation.Value is int spatialReferenceSystemId)
        {
            fragments.Add(new MethodCallCodeFragment("HasSrid", spatialReferenceSystemId));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IIndex index,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(index, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.SpatialIndex, out var spatialIndexAnnotation)
            && spatialIndexAnnotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment("IsSpatial"));
        }

        if (annotations.Remove(MySqlAnnotationNames.FullTextIndex, out var fullTextIndexAnnotation)
            && fullTextIndexAnnotation.Value is true)
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlIndexBuilderExtensions.IsFullText)));
        }

        if (annotations.Remove(MySqlAnnotationNames.IndexPrefixLength, out var prefixLengthAnnotation)
            && prefixLengthAnnotation.Value is int[] prefixLengths)
        {
            fragments.Add(
                new MethodCallCodeFragment(
                    nameof(MySqlIndexBuilderExtensions.HasPrefixLength),
                    prefixLengths.Cast<object>().ToArray()));
        }

        if (annotations.Remove(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps, out var annotation)
            && annotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlIndexBuilderExtensions.UseWithoutOverlaps)));
        }

        return fragments;
    }
}
