namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Restores temporal and application-time period metadata from scaffolded tables.
/// </summary>
internal sealed partial class MySqlScaffoldingModelFactory
{
    private static void ApplyTemporalConfiguration(
        DatabaseTable table,
        EntityTypeBuilder entityBuilder,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> propertyBuilders
    )
    {
        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)?.Value is not true)
        {
            return;
        }

        var periodStartColumnName = table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)
            ?.Value as string;

        var periodEndColumnName = table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)
            ?.Value as string;

        if (string.IsNullOrWhiteSpace(periodStartColumnName)
            || string.IsNullOrWhiteSpace(periodEndColumnName))
        {
            throw new InvalidOperationException(
                $"Temporal table '{table.Name}' does not expose both period-column names.");
        }

        var periodStartColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodStartColumnName,
            StringComparison.Ordinal));

        var periodEndColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodEndColumnName,
            StringComparison.Ordinal));

        var periodStartProperty = propertyBuilders[(table, periodStartColumn)];
        var periodEndProperty = propertyBuilders[(table, periodEndColumn)];

        entityBuilder.Metadata.SetMySqlTemporal(true);
        entityBuilder.Metadata.SetMySqlTemporalPeriodStartPropertyName(periodStartProperty.Metadata.Name);
        entityBuilder.Metadata.SetMySqlTemporalPeriodEndPropertyName(periodEndProperty.Metadata.Name);
        periodStartProperty.ValueGeneratedOnAddOrUpdate();
        periodEndProperty.ValueGeneratedOnAddOrUpdate();

        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)?.Value is string historyTableName)
        {
            entityBuilder.Metadata.SetMySqlTemporalHistoryTableName(historyTableName);
        }

        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistorySchema)?.Value is string historyTableSchema)
        {
            entityBuilder.Metadata.SetMySqlTemporalHistoryTableSchema(historyTableSchema);
        }
    }

    private static void ApplyApplicationTimeConfiguration(
        DatabaseTable table,
        EntityTypeBuilder entityBuilder,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> propertyBuilders
    )
    {
        if (table.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)?.Value is not true)
        {
            return;
        }

        var periodName = RequireApplicationTimeAnnotation(table, MySqlAnnotationNames.ApplicationTimePeriodName);

        var periodStartColumnName = RequireApplicationTimeAnnotation(
            table,
            MySqlAnnotationNames.ApplicationTimePeriodStartColumn);

        var periodEndColumnName = RequireApplicationTimeAnnotation(
            table,
            MySqlAnnotationNames.ApplicationTimePeriodEndColumn);

        var periodStartColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodStartColumnName,
            StringComparison.Ordinal));

        var periodEndColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodEndColumnName,
            StringComparison.Ordinal));

        var periodStartProperty = propertyBuilders[(table, periodStartColumn)];
        var periodEndProperty = propertyBuilders[(table, periodEndColumn)];

        entityBuilder.Metadata.SetMySqlApplicationTime(true);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodName(periodName);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodStartPropertyName(periodStartProperty.Metadata.Name);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodEndPropertyName(periodEndProperty.Metadata.Name);
        periodStartProperty.ValueGeneratedNever();
        periodEndProperty.ValueGeneratedNever();
    }

    private static string RequireApplicationTimeAnnotation(
        DatabaseTable table,
        string annotationName
    ) => table.FindAnnotation(annotationName)?.Value as string is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Application-time table '{table.Name}' does not define required annotation '{annotationName}'.");
}
