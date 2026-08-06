namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>
    /// Emits MariaDB's mutation-only FOR PORTION OF table clause. The annotation may reach SQL
    /// generation only through ExecuteUpdate or ExecuteDelete; rejecting a plain SELECT avoids
    /// silently assigning query semantics to a clause the engine defines exclusively for DML.
    /// </summary>
    private bool TryGenerateApplicationTimeTable(
        TableExpression tableExpression
    )
    {
        if (tableExpression.FindAnnotation(MySqlAnnotationNames.ApplicationTimeOperation)
                ?.Value is not true)
        {
            return false;
        }

        if (_mutationTargetTable is null)
        {
            throw new InvalidOperationException(
                "FOR PORTION OF is supported only by ExecuteUpdate and ExecuteDelete. "
                + "Application-time ranges cannot be enumerated as a SELECT query.");
        }

        if (Profile.GetSupport(ProviderCapability.ApplicationTimePeriods) != ProviderSupportStatus.Native)
        {
            throw new InvalidOperationException(
                "The configured engine does not provide native application-time periods.");
        }

        AppendDelimitedTable(tableExpression.Name, tableExpression.Schema);
        Sql.Append(" FOR PORTION OF ");
        Sql.Append(
            Dependencies.SqlGenerationHelper.DelimitIdentifier(
                GetRequiredApplicationTimeStringAnnotation(
                    tableExpression,
                    MySqlAnnotationNames.ApplicationTimePeriodName)));
        Sql.Append(" FROM ");
        AppendTemporalLiteral(
            GetRequiredApplicationTimeDateTimeAnnotation(
                tableExpression,
                MySqlAnnotationNames.ApplicationTimeRangeStart));
        Sql.Append(" TO ");
        AppendTemporalLiteral(
            GetRequiredApplicationTimeDateTimeAnnotation(
                tableExpression,
                MySqlAnnotationNames.ApplicationTimeRangeEnd));

        // MariaDB's single-table FOR PORTION OF grammar does not accept EF's
        // table alias after the temporal clause. The mutation visitor therefore
        // renders target columns without qualification for this strict shape.

        return true;
    }

    private static DateTime GetRequiredApplicationTimeDateTimeAnnotation(
        TableExpression tableExpression,
        string annotationName
    ) => tableExpression.FindAnnotation(annotationName)
        ?.Value is DateTime value
        ? value
        : throw new InvalidOperationException(
            $"Application-time table '{tableExpression.Name}' does not define required "
            + $"annotation '{annotationName}'.");

    private static string GetRequiredApplicationTimeStringAnnotation(
        TableExpression tableExpression,
        string annotationName
    ) => tableExpression.FindAnnotation(annotationName)?.Value as string is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Application-time table '{tableExpression.Name}' does not define required "
                + $"annotation '{annotationName}'.");
}
