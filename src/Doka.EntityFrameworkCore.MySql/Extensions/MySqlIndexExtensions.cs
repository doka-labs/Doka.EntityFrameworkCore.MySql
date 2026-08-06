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

    public static bool GetMySqlFullTextIndex(
        this IReadOnlyIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return (index.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?) == true;
    }

    public static void SetMySqlFullTextIndex(
        this IMutableIndex index,
        bool fullText
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!fullText)
        {
            index.RemoveAnnotation(MySqlAnnotationNames.FullTextIndex);
            return;
        }

        index.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
    }

    public static IReadOnlyList<int>? GetMySqlIndexPrefixLengths(
        this IReadOnlyIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
            ?.Value as IReadOnlyList<int>;
    }

    public static void SetMySqlIndexPrefixLengths(
        this IMutableIndex index,
        IReadOnlyList<int>? prefixLengths
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        if (prefixLengths is null)
        {
            index.RemoveAnnotation(MySqlAnnotationNames.IndexPrefixLength);
            return;
        }

        index.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, prefixLengths.ToArray());
    }

    public static bool GetMySqlApplicationTimeWithoutOverlaps(
        this IReadOnlyIndex index
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        return (index.FindAnnotation(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps)?.Value as bool?) == true;
    }

    public static void SetMySqlApplicationTimeWithoutOverlaps(
        this IMutableIndex index,
        bool enabled
    )
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!enabled)
        {
            index.RemoveAnnotation(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps);
            return;
        }

        index.SetAnnotation(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps, true);
    }
}
