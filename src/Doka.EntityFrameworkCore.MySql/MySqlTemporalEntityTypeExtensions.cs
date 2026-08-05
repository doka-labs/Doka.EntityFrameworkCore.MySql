namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides metadata accessors for MySQL-family temporal tables.
/// </summary>
public static class MySqlTemporalEntityTypeExtensions
{
    /// <summary>
    /// Returns whether the entity type is mapped to a temporal table.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns><see langword="true"/> when temporal mapping is enabled.</returns>
    public static bool IsMySqlTemporal(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return (entityType.FindAnnotation(MySqlAnnotationNames.IsTemporal)?.Value as bool?) == true;
    }

    /// <summary>
    /// Enables or disables temporal mapping for the entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="temporal">Whether temporal mapping is enabled.</param>
    public static void SetMySqlTemporal(
        this IMutableEntityType entityType,
        bool temporal
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        entityType.SetAnnotation(MySqlAnnotationNames.IsTemporal, temporal);
    }

    /// <summary>
    /// Returns the history-table name configured for MySQL temporal emulation.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The history-table name, or <see langword="null"/> when no name is configured.</returns>
    public static string? GetMySqlTemporalHistoryTableName(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.TemporalHistoryTableName)?.Value as string;
    }

    /// <summary>
    /// Configures the history-table name used by MySQL temporal emulation.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="historyTableName">The history-table name, or <see langword="null"/> to remove it.</param>
    public static void SetMySqlTemporalHistoryTableName(
        this IMutableEntityType entityType,
        string? historyTableName
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        SetOptionalName(entityType, MySqlAnnotationNames.TemporalHistoryTableName, historyTableName);
    }

    /// <summary>
    /// Returns the database/schema configured for the emulated MySQL history table.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The history-table schema, or <see langword="null"/> to use the mapped table schema.</returns>
    public static string? GetMySqlTemporalHistoryTableSchema(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.TemporalHistoryTableSchema)?.Value as string;
    }

    /// <summary>
    /// Configures the database/schema used by the emulated MySQL history table.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="historyTableSchema">The history-table schema, or <see langword="null"/> to use the mapped table schema.</param>
    public static void SetMySqlTemporalHistoryTableSchema(
        this IMutableEntityType entityType,
        string? historyTableSchema
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        SetOptionalName(entityType, MySqlAnnotationNames.TemporalHistoryTableSchema, historyTableSchema);
    }

    /// <summary>
    /// Returns the property name used for the temporal period start boundary.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The period-start property name, or <see langword="null"/> when no name is configured.</returns>
    public static string? GetMySqlTemporalPeriodStartPropertyName(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartPropertyName)?.Value as string;
    }

    /// <summary>
    /// Configures the property name used for the temporal period start boundary.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="propertyName">The property name, or <see langword="null"/> to remove it.</param>
    public static void SetMySqlTemporalPeriodStartPropertyName(
        this IMutableEntityType entityType,
        string? propertyName
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        SetOptionalName(entityType, MySqlAnnotationNames.TemporalPeriodStartPropertyName, propertyName);
    }

    /// <summary>
    /// Returns the property name used for the temporal period end boundary.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The period-end property name, or <see langword="null"/> when no name is configured.</returns>
    public static string? GetMySqlTemporalPeriodEndPropertyName(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndPropertyName)?.Value as string;
    }

    /// <summary>
    /// Configures the property name used for the temporal period end boundary.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="propertyName">The property name, or <see langword="null"/> to remove it.</param>
    public static void SetMySqlTemporalPeriodEndPropertyName(
        this IMutableEntityType entityType,
        string? propertyName
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        SetOptionalName(entityType, MySqlAnnotationNames.TemporalPeriodEndPropertyName, propertyName);
    }

    private static void SetOptionalName(
        IMutableEntityType entityType,
        string annotationName,
        string? value
    )
    {
        if (value is null)
        {
            entityType.RemoveAnnotation(annotationName);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        entityType.SetAnnotation(annotationName, value);
    }
}
