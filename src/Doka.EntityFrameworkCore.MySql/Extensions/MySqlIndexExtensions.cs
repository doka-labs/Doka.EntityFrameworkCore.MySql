namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlIndexExtensions
{
    public static bool GetMySqlSpatialIndex(
        this IReadOnlyIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return (index.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?) == true;
    }

    public static void SetMySqlSpatialIndex(
        this IMutableIndex index,
        bool spatial
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!spatial)
        {
            index.RemoveAnnotation(MySqlAnnotationNames.SpatialIndex);
            return;
        }

        index.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
    }
}
