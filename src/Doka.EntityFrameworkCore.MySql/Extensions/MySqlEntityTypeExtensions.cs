namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlEntityTypeExtensions
{
    public static string? GetMySqlCharSet(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.CharSet)
            ?.Value as string;
    }

    public static void SetMySqlCharSet(
        this IMutableEntityType entityType,
        string? charSet
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (string.IsNullOrWhiteSpace(charSet))
        {
            entityType.RemoveAnnotation(MySqlAnnotationNames.CharSet);
            return;
        }

        entityType.SetAnnotation(MySqlAnnotationNames.CharSet, charSet);
    }

    public static string? GetMySqlStorageEngine(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.StorageEngine)
            ?.Value as string;
    }

    public static void SetMySqlStorageEngine(
        this IMutableEntityType entityType,
        string? storageEngine
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (string.IsNullOrWhiteSpace(storageEngine))
        {
            entityType.RemoveAnnotation(MySqlAnnotationNames.StorageEngine);
            return;
        }

        entityType.SetAnnotation(MySqlAnnotationNames.StorageEngine, storageEngine);
    }
}
