namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Resolves the application-time contract from entity metadata to one physical table.
/// </summary>
internal static class MySqlApplicationTimeMetadata
{
    public const string DefaultPeriodName = "ApplicationTime";

    public const string DefaultPeriodStartPropertyName = "ValidFrom";

    public const string DefaultPeriodEndPropertyName = "ValidTo";

    public static MySqlApplicationTimeTableMetadata? FindTableMetadata(
        ITable table
    )
    {
        ArgumentNullException.ThrowIfNull(table);

        var entityType = table
            .EntityTypeMappings.Select(mapping => mapping.TypeBase)
            .OfType<IReadOnlyEntityType>()
            .FirstOrDefault(candidate => candidate.IsMySqlApplicationTime());

        if (entityType is null)
        {
            return null;
        }

        var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);

        return new MySqlApplicationTimeTableMetadata(
            RequireName(entityType.GetMySqlApplicationTimePeriodName(), "period"),
            FindColumnName(entityType, entityType.GetMySqlApplicationTimePeriodStartPropertyName(), storeObject),
            FindColumnName(entityType, entityType.GetMySqlApplicationTimePeriodEndPropertyName(), storeObject),
            entityType.GetMySqlApplicationTimeWithoutOverlaps()
            || (entityType
                .FindPrimaryKey()
                ?.FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                ?.Value as bool?)
            == true);
    }

    public static void ValidatePeriodPropertyType(
        IReadOnlyEntityType entityType,
        string propertyName
    )
    {
        var property = entityType.FindProperty(propertyName);

        if (property is not null
            && (property.ClrType != typeof(DateTime) || property.IsNullable))
        {
            throw new InvalidOperationException(
                $"Application-time period property '{entityType.DisplayName()}.{propertyName}' "
                + "must be a non-nullable DateTime property.");
        }
    }

    private static string FindColumnName(
        IReadOnlyEntityType entityType,
        string? propertyName,
        StoreObjectIdentifier storeObject
    )
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || entityType.FindProperty(propertyName) is not { } property
            || property.GetColumnName(storeObject) is not { } columnName)
        {
            throw new InvalidOperationException(
                $"Application-time entity type '{entityType.DisplayName()}' does not map period property "
                + $"'{propertyName}' to table '{storeObject.Name}'.");
        }

        return columnName;
    }

    private static string RequireName(
        string? value,
        string component
    ) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"The application-time {component} name is not configured.");
}

internal sealed record MySqlApplicationTimeTableMetadata(
    string PeriodName,
    string PeriodStartColumn,
    string PeriodEndColumn,
    bool WithoutOverlaps
);
