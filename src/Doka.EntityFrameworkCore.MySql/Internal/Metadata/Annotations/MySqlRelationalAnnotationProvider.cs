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

        if (MySqlTemporalMetadata.FindTableMetadata(table) is { } temporalMetadata)
        {
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

        if (MySqlApplicationTimeMetadata.FindTableMetadata(table) is { } applicationTimeMetadata)
        {
            yield return CreateAnnotation(table, MySqlAnnotationNames.IsApplicationTime, value: true);
            yield return CreateAnnotation(
                table,
                MySqlAnnotationNames.ApplicationTimePeriodName,
                applicationTimeMetadata.PeriodName);
            yield return CreateAnnotation(
                table,
                MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
                applicationTimeMetadata.PeriodStartColumn);
            yield return CreateAnnotation(
                table,
                MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
                applicationTimeMetadata.PeriodEndColumn);
            yield return CreateAnnotation(
                table,
                MySqlAnnotationNames.ApplicationTimeWithoutOverlaps,
                applicationTimeMetadata.WithoutOverlaps);
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

        if (FindColumnAnnotation(column, MySqlAnnotationNames.Invisible) is { } invisibleAnnotation)
        {
            yield return invisibleAnnotation;
        }

        foreach (var propertyMapping in column.PropertyMappings)
        {
            if (propertyMapping.Property.DeclaringType is not IReadOnlyEntityType entityType)
            {
                continue;
            }

            if (entityType.IsMySqlTemporal())
            {
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

            if (!entityType.IsMySqlApplicationTime())
            {
                continue;
            }

            if (string.Equals(
                    propertyMapping.Property.Name,
                    entityType.GetMySqlApplicationTimePeriodStartPropertyName(),
                    StringComparison.Ordinal))
            {
                yield return CreateAnnotation(
                    column,
                    MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
                    value: true);
                yield break;
            }

            if (string.Equals(
                    propertyMapping.Property.Name,
                    entityType.GetMySqlApplicationTimePeriodEndPropertyName(),
                    StringComparison.Ordinal))
            {
                yield return CreateAnnotation(
                    column,
                    MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
                    value: true);
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

        if (FindApplicationTimePeriodName(index.MappedIndexes) is { } periodName)
        {
            yield return CreateAnnotation(
                index,
                MySqlAnnotationNames.ApplicationTimeConstraintPeriodName,
                periodName);
        }
    }

    public override IEnumerable<IAnnotation> For(
        IUniqueConstraint constraint,
        bool designTime
    )
    {
        foreach (var annotation in base.For(constraint, designTime))
        {
            yield return annotation;
        }

        if (FindApplicationTimePeriodName(constraint.MappedKeys) is { } periodName)
        {
            yield return CreateAnnotation(
                constraint,
                MySqlAnnotationNames.ApplicationTimeConstraintPeriodName,
                periodName);
        }
    }

    private static string? FindApplicationTimePeriodName(
        IEnumerable<IReadOnlyIndex> indexes
    ) => indexes
        .Where(index => index.GetMySqlApplicationTimeWithoutOverlaps())
        .Select(index => index.DeclaringEntityType.GetMySqlApplicationTimePeriodName())
        .FirstOrDefault(periodName => !string.IsNullOrWhiteSpace(periodName));

    private static string? FindApplicationTimePeriodName(
        IEnumerable<IReadOnlyKey> keys
    ) => keys
        .Where(key => key.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)?.Value is true)
        .Select(key => key.DeclaringEntityType.GetMySqlApplicationTimePeriodName())
        .FirstOrDefault(periodName => !string.IsNullOrWhiteSpace(periodName));

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
