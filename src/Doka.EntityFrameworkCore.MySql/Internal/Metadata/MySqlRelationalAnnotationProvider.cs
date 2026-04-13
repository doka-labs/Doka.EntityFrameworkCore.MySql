namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlRelationalAnnotationProvider : RelationalAnnotationProvider
{
    public MySqlRelationalAnnotationProvider(
        RelationalAnnotationProviderDependencies dependencies
    ) : base(dependencies) { }

    public override IEnumerable<IAnnotation> For(
        IRelationalModel model,
        bool designTime
    )
    {
        foreach (var annotation in base.For(model, designTime))
        {
            yield return annotation;
        }

        if (model.FindAnnotation(MySqlAnnotationNames.CharSet) is { } charSetAnnotation)
        {
            yield return charSetAnnotation;
            yield break;
        }

        if (model.Model.FindAnnotation(MySqlAnnotationNames.CharSet) is { } modelCharSetAnnotation)
        {
            yield return modelCharSetAnnotation;
        }
    }

    public override IEnumerable<IAnnotation> For(
        ITable table,
        bool designTime
    )
    {
        foreach (var annotation in base.For(table, designTime))
        {
            yield return annotation;
        }

        if (FindTableAnnotation(table, MySqlAnnotationNames.CharSet) is { } charSetAnnotation)
        {
            yield return charSetAnnotation;
        }

        if (FindTableAnnotation(table, MySqlAnnotationNames.StorageEngine) is { } storageEngineAnnotation)
        {
            yield return storageEngineAnnotation;
        }
    }

    public override IEnumerable<IAnnotation> For(
        IColumn column,
        bool designTime
    )
    {
        foreach (var annotation in base.For(column, designTime))
        {
            yield return annotation;
        }

        if (FindColumnAnnotation(column, MySqlAnnotationNames.GuidFormat) is { } guidFormatAnnotation)
        {
            yield return guidFormatAnnotation;
        }

        if (FindColumnAnnotation(column, MySqlAnnotationNames.ValueGenerationStrategy) is { } valueGenerationAnnotation)
        {
            yield return valueGenerationAnnotation;
        }

        if (FindColumnAnnotation(column, MySqlAnnotationNames.SpatialReferenceSystemId) is
            { } spatialReferenceSystemIdAnnotation)
        {
            yield return spatialReferenceSystemIdAnnotation;
        }
    }

    public override IEnumerable<IAnnotation> For(
        ITableIndex index,
        bool designTime
    )
    {
        foreach (var annotation in base.For(index, designTime))
        {
            yield return annotation;
        }

        if (index.FindAnnotation(MySqlAnnotationNames.SpatialIndex) is { } spatialIndexAnnotation)
        {
            yield return spatialIndexAnnotation;
        }
    }

    private static IAnnotation? FindTableAnnotation(
        ITable table,
        string annotationName
    )
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationName);

        return table.FindAnnotation(annotationName)
            ?? table
                .EntityTypeMappings.Select(mapping => mapping.TypeBase.FindAnnotation(annotationName))
                .FirstOrDefault(annotation => annotation is not null);
    }

    private static IAnnotation? FindColumnAnnotation(
        IColumn column,
        string annotationName
    )
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationName);

        return column.FindAnnotation(annotationName)
            ?? column
                .PropertyMappings.Select(mapping => mapping.Property.FindAnnotation(annotationName))
                .FirstOrDefault(annotation => annotation is not null);
    }
}
