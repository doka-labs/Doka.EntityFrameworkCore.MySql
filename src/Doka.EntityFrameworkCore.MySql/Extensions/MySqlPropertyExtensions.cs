namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlPropertyExtensions
{
    public static MySqlGuidFormat? GetMySqlGuidFormat(
        this IReadOnlyProperty property
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.FindAnnotation(MySqlAnnotationNames.GuidFormat)
            ?.Value is MySqlGuidFormat format
            ? format
            : null;
    }

    public static void SetMySqlGuidFormat(
        this IMutableProperty property,
        MySqlGuidFormat? format
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        if (format is null)
        {
            property.RemoveAnnotation(MySqlAnnotationNames.GuidFormat);
            return;
        }

        property.SetAnnotation(MySqlAnnotationNames.GuidFormat, format.Value);
    }

    public static MySqlValueGenerationStrategy? GetMySqlValueGenerationStrategy(
        this IReadOnlyProperty property
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
            ?.Value is MySqlValueGenerationStrategy strategy
            ? strategy
            : null;
    }

    public static void SetMySqlValueGenerationStrategy(
        this IMutableProperty property,
        MySqlValueGenerationStrategy? strategy
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        if (strategy is null)
        {
            property.RemoveAnnotation(MySqlAnnotationNames.ValueGenerationStrategy);
            return;
        }

        property.SetAnnotation(MySqlAnnotationNames.ValueGenerationStrategy, strategy.Value);
    }

    public static int? GetMySqlSpatialReferenceSystemId(
        this IReadOnlyProperty property
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
            ?.Value is int srid
            ? srid
            : null;
    }

    public static void SetMySqlSpatialReferenceSystemId(
        this IMutableProperty property,
        int? spatialReferenceSystemId
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        if (spatialReferenceSystemId is null)
        {
            property.RemoveAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId);
            return;
        }

        property.SetAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId, spatialReferenceSystemId.Value);
    }
}
