namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides metadata accessors for MariaDB application-time periods.
/// </summary>
public static class MySqlApplicationTimeEntityTypeExtensions
{
    /// <summary>
    /// Returns whether the entity type is mapped with an application-time period.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the entity type has an application-time period;
    /// otherwise, <see langword="false" />.
    /// </returns>
    public static bool IsMySqlApplicationTime(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return (entityType.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)?.Value as bool?) == true;
    }

    /// <summary>
    /// Enables or disables application-time mapping for the entity type.
    /// </summary>
    /// <param name="entityType">The entity type being configured.</param>
    /// <param name="applicationTime">
    /// <see langword="true" /> to enable application-time mapping;
    /// otherwise, <see langword="false" />.
    /// </param>
    public static void SetMySqlApplicationTime(
        this IMutableEntityType entityType,
        bool applicationTime
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        entityType.SetAnnotation(MySqlAnnotationNames.IsApplicationTime, applicationTime);
    }

    /// <summary>
    /// Returns the SQL period identifier.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>The configured SQL period identifier, or <see langword="null" /> when none is configured.</returns>
    public static string? GetMySqlApplicationTimePeriodName(
        this IReadOnlyEntityType entityType
    ) => GetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodName);

    /// <summary>
    /// Configures the SQL period identifier.
    /// </summary>
    /// <param name="entityType">The entity type being configured.</param>
    /// <param name="periodName">The SQL period identifier, or <see langword="null" /> to remove it.</param>
    public static void SetMySqlApplicationTimePeriodName(
        this IMutableEntityType entityType,
        string? periodName
    ) => SetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodName, periodName);

    /// <summary>
    /// Returns the period-start property name.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>The period-start property name, or <see langword="null" /> when none is configured.</returns>
    public static string? GetMySqlApplicationTimePeriodStartPropertyName(
        this IReadOnlyEntityType entityType
    ) => GetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodStartPropertyName);

    /// <summary>
    /// Configures the period-start property name.
    /// </summary>
    /// <param name="entityType">The entity type being configured.</param>
    /// <param name="propertyName">The period-start property name, or <see langword="null" /> to remove it.</param>
    public static void SetMySqlApplicationTimePeriodStartPropertyName(
        this IMutableEntityType entityType,
        string? propertyName
    ) => SetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodStartPropertyName, propertyName);

    /// <summary>
    /// Returns the period-end property name.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>The period-end property name, or <see langword="null" /> when none is configured.</returns>
    public static string? GetMySqlApplicationTimePeriodEndPropertyName(
        this IReadOnlyEntityType entityType
    ) => GetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodEndPropertyName);

    /// <summary>
    /// Configures the period-end property name.
    /// </summary>
    /// <param name="entityType">The entity type being configured.</param>
    /// <param name="propertyName">The period-end property name, or <see langword="null" /> to remove it.</param>
    public static void SetMySqlApplicationTimePeriodEndPropertyName(
        this IMutableEntityType entityType,
        string? propertyName
    ) => SetName(entityType, MySqlAnnotationNames.ApplicationTimePeriodEndPropertyName, propertyName);

    /// <summary>
    /// Returns whether the primary key includes the period with <c>WITHOUT OVERLAPS</c>.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the primary key requires non-overlapping periods;
    /// otherwise, <see langword="false" />.
    /// </returns>
    public static bool GetMySqlApplicationTimeWithoutOverlaps(
        this IReadOnlyEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return (entityType.FindAnnotation(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps)?.Value as bool?) == true;
    }

    /// <summary>
    /// Configures whether the primary key includes the period with <c>WITHOUT OVERLAPS</c>.
    /// </summary>
    /// <param name="entityType">The entity type being configured.</param>
    /// <param name="withoutOverlaps">
    /// <see langword="true" /> to require non-overlapping application-time ranges;
    /// otherwise, <see langword="false" />.
    /// </param>
    public static void SetMySqlApplicationTimeWithoutOverlaps(
        this IMutableEntityType entityType,
        bool withoutOverlaps
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        entityType.SetAnnotation(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps, withoutOverlaps);
    }

    private static string? GetName(
        IReadOnlyEntityType entityType,
        string annotationName
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return entityType.FindAnnotation(annotationName)
            ?.Value as string;
    }

    private static void SetName(
        IMutableEntityType entityType,
        string annotationName,
        string? value
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (value is null)
        {
            entityType.RemoveAnnotation(annotationName);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        entityType.SetAnnotation(annotationName, value);
    }
}
