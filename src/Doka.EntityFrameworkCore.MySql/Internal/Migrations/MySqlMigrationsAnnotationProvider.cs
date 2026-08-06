namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationsAnnotationProvider : IMigrationsAnnotationProvider
{
    public IEnumerable<IAnnotation> ForRemove(
        IRelationalModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        return GetSupportedModelAnnotations(model);
    }

    public IEnumerable<IAnnotation> ForRemove(
        ITable table
    )
    {
        ArgumentNullException.ThrowIfNull(table);

        return GetSupportedTableAnnotations(table);
    }

    public IEnumerable<IAnnotation> ForRemove(
        IColumn column
    )
    {
        ArgumentNullException.ThrowIfNull(column);

        return GetSupportedColumnAnnotations(column);
    }

    public IEnumerable<IAnnotation> ForRemove(
        IView view
    )
    {
        ArgumentNullException.ThrowIfNull(view);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRemove(
        IViewColumn column
    )
    {
        ArgumentNullException.ThrowIfNull(column);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRemove(
        IUniqueConstraint constraint
    )
    {
        ArgumentNullException.ThrowIfNull(constraint);

        return GetSupportedAnnotations(constraint)
            .Concat(GetSupportedTableAnnotations(constraint.Table))
            .DistinctBy(annotation => annotation.Name);
    }

    public IEnumerable<IAnnotation> ForRemove(
        ITableIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return GetSupportedAnnotations(index);
    }

    public IEnumerable<IAnnotation> ForRemove(
        IForeignKeyConstraint foreignKey
    )
    {
        ArgumentNullException.ThrowIfNull(foreignKey);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRemove(
        ISequence sequence
    )
    {
        ArgumentNullException.ThrowIfNull(sequence);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRemove(
        ICheckConstraint checkConstraint
    )
    {
        ArgumentNullException.ThrowIfNull(checkConstraint);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRename(
        ITable table
    ) => GetSupportedTableAnnotations(table);

    public IEnumerable<IAnnotation> ForRename(
        IColumn column
    ) => GetSupportedColumnAnnotations(column);

    public IEnumerable<IAnnotation> ForRename(
        ITableIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return Array.Empty<IAnnotation>();
    }

    public IEnumerable<IAnnotation> ForRename(
        ISequence sequence
    )
    {
        ArgumentNullException.ThrowIfNull(sequence);

        return Array.Empty<IAnnotation>();
    }

    private static IEnumerable<IAnnotation> GetSupportedAnnotations(
        IAnnotatable annotatable
    )
    {
        ArgumentNullException.ThrowIfNull(annotatable);

        return annotatable
            .GetAnnotations()
            .Where(annotation => annotation.Name is MySqlAnnotationNames.CharSet
                or MySqlAnnotationNames.StorageEngine
                or MySqlAnnotationNames.GuidFormat
                or MySqlAnnotationNames.ValueGenerationStrategy
                or MySqlAnnotationNames.SpatialReferenceSystemId
                or MySqlAnnotationNames.SpatialIndex
                or MySqlAnnotationNames.FullTextIndex
                or MySqlAnnotationNames.IndexPrefixLength
                or MySqlAnnotationNames.IsTemporal
                or MySqlAnnotationNames.TemporalHistoryTable
                or MySqlAnnotationNames.TemporalHistorySchema
                or MySqlAnnotationNames.TemporalPeriodStartColumn
                or MySqlAnnotationNames.TemporalPeriodEndColumn
                or MySqlAnnotationNames.IsApplicationTime
                or MySqlAnnotationNames.ApplicationTimePeriodName
                or MySqlAnnotationNames.ApplicationTimePeriodStartColumn
                or MySqlAnnotationNames.ApplicationTimePeriodEndColumn
                or MySqlAnnotationNames.ApplicationTimeWithoutOverlaps
                or MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps
                or MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps
                or MySqlAnnotationNames.ApplicationTimeConstraintPeriodName
                or MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime
                or MySqlAnnotationNames.ApplicationTimeSourcePeriodName
                or MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn
                or MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn
                or MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps);
    }

    private static List<IAnnotation> GetSupportedModelAnnotations(
        IRelationalModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        var relationalAnnotations = GetSupportedAnnotations(model)
            .ToList();

        if (relationalAnnotations.Any(annotation => annotation.Name == MySqlAnnotationNames.CharSet))
        {
            return relationalAnnotations;
        }

        if (model.Model.FindAnnotation(MySqlAnnotationNames.CharSet) is { } modelCharSetAnnotation)
        {
            relationalAnnotations.Add(modelCharSetAnnotation);
        }

        return relationalAnnotations;
    }

    private static List<IAnnotation> GetSupportedTableAnnotations(
        ITable table
    )
    {
        ArgumentNullException.ThrowIfNull(table);

        var tableAnnotations = GetSupportedAnnotations(table)
            .ToList();

        AddMappedTypeBaseAnnotationIfMissing(tableAnnotations, table, MySqlAnnotationNames.CharSet);
        AddMappedTypeBaseAnnotationIfMissing(tableAnnotations, table, MySqlAnnotationNames.StorageEngine);

        return tableAnnotations;
    }

    private static List<IAnnotation> GetSupportedColumnAnnotations(
        IColumn column
    )
    {
        ArgumentNullException.ThrowIfNull(column);

        var columnAnnotations = GetSupportedAnnotations(column)
            .ToList();

        AddMappedPropertyAnnotationIfMissing(columnAnnotations, column, MySqlAnnotationNames.GuidFormat);
        AddMappedPropertyAnnotationIfMissing(columnAnnotations, column, MySqlAnnotationNames.ValueGenerationStrategy);
        AddMappedPropertyAnnotationIfMissing(columnAnnotations, column, MySqlAnnotationNames.SpatialReferenceSystemId);

        return columnAnnotations;
    }

    private static void AddMappedPropertyAnnotationIfMissing(
        List<IAnnotation> annotations,
        IColumn column,
        string annotationName
    )
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationName);

        if (annotations.Any(annotation => annotation.Name == annotationName))
        {
            return;
        }

        var mappedAnnotation = column
            .PropertyMappings.Select(mapping => mapping.Property.FindAnnotation(annotationName))
            .FirstOrDefault(annotation => annotation is not null);

        if (mappedAnnotation is not null)
        {
            annotations.Add(mappedAnnotation);
        }
    }

    private static void AddMappedTypeBaseAnnotationIfMissing(
        List<IAnnotation> annotations,
        ITable table,
        string annotationName
    )
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationName);

        if (annotations.Any(annotation => annotation.Name == annotationName))
        {
            return;
        }

        var mappedAnnotation = table
            .EntityTypeMappings.Select(mapping => mapping.TypeBase.FindAnnotation(annotationName))
            .FirstOrDefault(annotation => annotation is not null);

        if (mappedAnnotation is not null)
        {
            annotations.Add(mappedAnnotation);
        }
    }
}
