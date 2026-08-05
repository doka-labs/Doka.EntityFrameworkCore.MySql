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

        if (MySqlTemporalMetadata.FindTableMetadata(table) is not { } temporalMetadata)
        {
            yield break;
        }

        yield return CreateAnnotation(table, MySqlAnnotationNames.IsTemporal, value: true);

        if (temporalMetadata.HistoryTable is { } historyTableName)
        {
            yield return CreateAnnotation(table, MySqlAnnotationNames.TemporalHistoryTable, historyTableName);
        }

        if (temporalMetadata.HistorySchema is { } historyTableSchema)
        {
            yield return CreateAnnotation(table, MySqlAnnotationNames.TemporalHistorySchema, historyTableSchema);
        }

        yield return CreateAnnotation(
            table,
            MySqlAnnotationNames.TemporalPeriodStartColumn,
            temporalMetadata.PeriodStartColumn);
        yield return CreateAnnotation(
            table,
            MySqlAnnotationNames.TemporalPeriodEndColumn,
            temporalMetadata.PeriodEndColumn);
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

        foreach (var propertyMapping in column.PropertyMappings)
        {
            if (propertyMapping.Property.DeclaringType is not IReadOnlyEntityType entityType
                || !entityType.IsMySqlTemporal())
            {
                continue;
            }

            if (string.Equals(
                    propertyMapping.Property.Name,
                    entityType.GetMySqlTemporalPeriodStartPropertyName(),
                    StringComparison.Ordinal))
            {
                yield return CreateAnnotation(column, MySqlAnnotationNames.TemporalPeriodStartColumn, value: true);
                yield break;
            }

            if (string.Equals(
                    propertyMapping.Property.Name,
                    entityType.GetMySqlTemporalPeriodEndPropertyName(),
                    StringComparison.Ordinal))
            {
                yield return CreateAnnotation(column, MySqlAnnotationNames.TemporalPeriodEndColumn, value: true);
                yield break;
            }
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

        if (index.FindAnnotation(MySqlAnnotationNames.FullTextIndex) is { } fullTextIndexAnnotation)
        {
            yield return fullTextIndexAnnotation;
        }

        if (index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength) is { } prefixLengthAnnotation)
        {
            yield return prefixLengthAnnotation;
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

    private static IAnnotation CreateAnnotation(
        IAnnotatable annotatable,
        string name,
        object value
    ) => annotatable.FindAnnotation(name)
        ?? new Annotation(name, value);

}
