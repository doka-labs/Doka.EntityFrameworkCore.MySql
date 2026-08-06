namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    private static bool TryGetApplicationTimeMigrationContract(
        MigrationOperation operation,
        string tableName,
        bool sourceContract,
        out ApplicationTimeMigrationContract? contract
    )
    {
        var isApplicationTimeAnnotation = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime
            : MySqlAnnotationNames.IsApplicationTime;

        if (operation.FindAnnotation(isApplicationTimeAnnotation)?.Value is not true)
        {
            contract = null;
            return false;
        }

        var periodNameAnnotation = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodName
            : MySqlAnnotationNames.ApplicationTimePeriodName;

        var periodStartAnnotation = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn
            : MySqlAnnotationNames.ApplicationTimePeriodStartColumn;

        var periodEndAnnotation = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn
            : MySqlAnnotationNames.ApplicationTimePeriodEndColumn;

        var withoutOverlapsAnnotation = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps
            : MySqlAnnotationNames.ApplicationTimeWithoutOverlaps;

        contract = new ApplicationTimeMigrationContract(
            GetRequiredApplicationTimeAnnotation(operation, tableName, periodNameAnnotation),
            GetRequiredApplicationTimeAnnotation(operation, tableName, periodStartAnnotation),
            GetRequiredApplicationTimeAnnotation(operation, tableName, periodEndAnnotation),
            operation.FindAnnotation(withoutOverlapsAnnotation)?.Value is true);

        return true;
    }

    private void AppendApplicationTimePeriod(
        ApplicationTimeMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .AppendLine(",")
            .Append("PERIOD FOR ")
            .Append(DelimitMigrationIdentifier(contract.PeriodName))
            .Append(" (")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(", ")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .AppendLine(")");
    }

    private bool AppendApplicationTimeTransition(
        AlterTableOperation operation,
        MigrationCommandListBuilder builder
    )
    {
        TryGetApplicationTimeMigrationContract(operation, operation.Name, sourceContract: true, out var sourceContract);
        TryGetApplicationTimeMigrationContract(
            operation,
            operation.Name,
            sourceContract: false,
            out var targetContract);

        if (sourceContract is null && targetContract is null)
        {
            return false;
        }

        if (sourceContract is not null
            && targetContract is not null
            && sourceContract.HasSamePeriodIdentity(targetContract))
        {
            return false;
        }

        if (sourceContract is not null)
        {
            AppendDropApplicationTimePeriod(operation.Name, operation.Schema, sourceContract.PeriodName, builder);
        }

        if (targetContract is not null)
        {
            AppendAddApplicationTimePeriod(operation.Name, operation.Schema, targetContract, builder);
        }

        return true;
    }

    private void AppendAddApplicationTimePeriod(
        string tableName,
        string? schema,
        ApplicationTimeMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(tableName, schema))
            .Append(" ADD PERIOD FOR ")
            .Append(DelimitMigrationIdentifier(contract.PeriodName))
            .Append(" (")
            .Append(DelimitMigrationIdentifier(contract.PeriodStartColumn))
            .Append(", ")
            .Append(DelimitMigrationIdentifier(contract.PeriodEndColumn))
            .Append(")")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendDropApplicationTimePeriod(
        string tableName,
        string? schema,
        string periodName,
        MigrationCommandListBuilder builder
    )
    {
        builder
            .Append("ALTER TABLE ")
            .Append(DelimitMigrationIdentifier(tableName, schema))
            .Append(" DROP PERIOD ")
            .Append(DelimitMigrationIdentifier(periodName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private void AppendApplicationTimePrimaryKey(
        AddPrimaryKeyOperation operation,
        ApplicationTimeMigrationContract contract,
        MigrationCommandListBuilder builder
    )
    {
        if (operation.Name is not null)
        {
            builder
                .Append("CONSTRAINT ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" ");
        }

        builder
            .Append("PRIMARY KEY (")
            .Append(ColumnList(operation.Columns));

        var periodName = GetApplicationTimeConstraintPeriodName(operation);

        // The table-level flag is retained for migrations created before the key-level
        // annotation existed. New migrations bind the period to the exact constraint.
        if (periodName is not null || contract.WithoutOverlaps)
        {
            builder
                .Append(", ")
                .Append(DelimitMigrationIdentifier(periodName ?? contract.PeriodName))
                .Append(" WITHOUT OVERLAPS");
        }

        builder.Append(")");
    }

    private static string? GetApplicationTimeConstraintPeriodName(
        MigrationOperation operation
    ) => operation.FindAnnotation(MySqlAnnotationNames.ApplicationTimeConstraintPeriodName)?.Value switch
    {
        null => null,
        string periodName when !string.IsNullOrWhiteSpace(periodName) => periodName,
        _ => throw new InvalidOperationException(
            $"Migration operation '{operation.GetType().Name}' contains an invalid application-time constraint period annotation."),
    };

    private static string GetRequiredApplicationTimeAnnotation(
        MigrationOperation operation,
        string tableName,
        string annotationName
    ) => operation.FindAnnotation(annotationName)?.Value as string is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Application-time table '{tableName}' does not define required annotation '{annotationName}'.");

    private sealed record ApplicationTimeMigrationContract(
        string PeriodName,
        string PeriodStartColumn,
        string PeriodEndColumn,
        bool WithoutOverlaps
    )
    {
        public bool HasSamePeriodIdentity(
            ApplicationTimeMigrationContract other
        ) => string.Equals(PeriodName, other.PeriodName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PeriodStartColumn, other.PeriodStartColumn, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PeriodEndColumn, other.PeriodEndColumn, StringComparison.OrdinalIgnoreCase);
    }
}
