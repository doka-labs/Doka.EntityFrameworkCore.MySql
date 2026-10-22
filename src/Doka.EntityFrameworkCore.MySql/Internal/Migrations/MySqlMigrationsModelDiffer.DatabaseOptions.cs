namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
    private static void ApplyDatabaseOptions(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        var sourceCharSet = source is null ? null : GetDatabaseCharSet(source);
        var targetCharSet = GetDatabaseCharSet(target);
        var sourceCollation = source is null ? null : GetDatabaseCollation(source);
        var targetCollation = GetDatabaseCollation(target);

        if (string.Equals(sourceCharSet, targetCharSet, StringComparison.Ordinal)
            && string.Equals(sourceCollation, targetCollation, StringComparison.Ordinal))
        {
            return;
        }

        var alterDatabaseOperation = operations
            .OfType<AlterDatabaseOperation>()
            .FirstOrDefault();

        if (alterDatabaseOperation is null)
        {
            alterDatabaseOperation = new AlterDatabaseOperation();
            operations.Insert(0, alterDatabaseOperation);
        }

        SetOptionalAnnotation(alterDatabaseOperation, MySqlAnnotationNames.CharSet, targetCharSet);
        SetOptionalAnnotation(alterDatabaseOperation.OldDatabase, MySqlAnnotationNames.CharSet, sourceCharSet);
        alterDatabaseOperation.Collation = targetCollation;
        alterDatabaseOperation.OldDatabase.Collation = sourceCollation;
    }

    private static void SetOptionalAnnotation(
        MigrationOperation operation,
        string annotationName,
        string? value
    )
    {
        if (value is null)
        {
            operation.RemoveAnnotation(annotationName);
            return;
        }

        operation.SetAnnotation(annotationName, value);
    }

    private static void ApplyInheritedTableDefaults(
        IEnumerable<MigrationOperation> operations,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var operation in operations.OfType<AlterTableOperation>())
        {
            if (GetOptionalStringAnnotation(operation, MySqlAnnotationNames.CharSet) is null
                && GetOptionalStringAnnotation(operation.OldTable, MySqlAnnotationNames.CharSet) is not null)
            {
                SetRequiredInheritedTableDefault(operation, MySqlAnnotationNames.CharSet, GetDatabaseCharSet(target));
            }

            if (GetTableCollation(operation) is null
                && GetTableCollation(operation.OldTable) is not null)
            {
                SetRequiredInheritedTableDefault(
                    operation,
                    RelationalAnnotationNames.Collation,
                    GetDatabaseCollation(target));
            }
        }
    }

    private static void SetRequiredInheritedTableDefault(
        AlterTableOperation operation,
        string annotationName,
        string? value
    )
    {
        if (value is null)
        {
            throw new InvalidOperationException(
                $"Removing an explicit table '{annotationName}' requires a configured target database default.");
        }

        operation.SetAnnotation(annotationName, value);
    }

    private static string? GetTableCollation(
        IReadOnlyAnnotatable annotatable
    )
    {
        var relationalValue = GetOptionalStringAnnotation(annotatable, RelationalAnnotationNames.Collation);
        var providerValue = GetOptionalStringAnnotation(annotatable, MySqlAnnotationNames.Collation);

        if (relationalValue is not null
            && providerValue is not null
            && !string.Equals(relationalValue, providerValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Table collation metadata conflicts between '{RelationalAnnotationNames.Collation}' and "
                + $"'{MySqlAnnotationNames.Collation}'.");
        }

        return relationalValue ?? providerValue;
    }

    private static string? GetDatabaseCharSet(
        IRelationalModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        var relationalValue = GetOptionalStringAnnotation(model, MySqlAnnotationNames.CharSet);
        var modelValue = GetOptionalStringAnnotation(model.Model, MySqlAnnotationNames.CharSet);

        if (relationalValue is not null
            && modelValue is not null
            && !string.Equals(relationalValue, modelValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Relational and conceptual model metadata disagree for '{MySqlAnnotationNames.CharSet}'.");
        }

        return relationalValue ?? modelValue;
    }

    private static string? GetDatabaseCollation(
        IRelationalModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        var relationalValue = model.Collation;
        var modelValue = model.Model.GetCollation();

        ValidateOptionalString(relationalValue, RelationalAnnotationNames.Collation);
        ValidateOptionalString(modelValue, RelationalAnnotationNames.Collation);

        if (relationalValue is not null
            && modelValue is not null
            && !string.Equals(relationalValue, modelValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Relational and conceptual model metadata disagree for '{RelationalAnnotationNames.Collation}'.");
        }

        return relationalValue ?? modelValue;
    }

    private static string? GetOptionalStringAnnotation(
        IReadOnlyAnnotatable annotatable,
        string annotationName
    )
    {
        var annotation = annotatable.FindAnnotation(annotationName);
        if (annotation is null)
        {
            return null;
        }

        if (annotation.Value is not string value)
        {
            throw new InvalidOperationException($"The '{annotationName}' annotation must contain a non-empty string.");
        }

        ValidateOptionalString(value, annotationName);
        return value;
    }

    private static void ValidateOptionalString(
        string? value,
        string metadataName
    )
    {
        if (value is not null
            && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The '{metadataName}' metadata value must be a non-empty string when configured.");
        }
    }
}
