namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlColumnAnnotationPolicy
{
    public static bool ShouldEmitValueGeneration(
        IColumn column,
        IAnnotation annotation
    )
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(annotation);

        return annotation.Value is not MySqlValueGenerationStrategy.AutoIncrement || !IsNonPrincipalSplitColumn(column);
    }

    private static bool IsNonPrincipalSplitColumn(
        IColumn column
    )
    {
        var hasPropertyMapping = false;

        foreach (var propertyMapping in column.PropertyMappings)
        {
            hasPropertyMapping = true;

            if (propertyMapping.TableMapping.IsSplitEntityTypePrincipal != false)
            {
                return false;
            }
        }

        return hasPropertyMapping;
    }
}
