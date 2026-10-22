namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
    private static void MarkTemporalDeactivationsDestructive(
        IReadOnlyList<MigrationOperation> operations
    )
    {
        foreach (var operation in operations.OfType<AlterTableOperation>())
        {
            var sourceIsTemporal = operation.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value is true;

            var targetIsTemporal = operation.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value is true;

            if (sourceIsTemporal && !targetIsTemporal)
            {
                // Both native MariaDB and MySQL emulation delete temporal
                // history when a table is converted back to a regular table.
                // Surface that data-loss boundary through EF's migration model.
                operation.IsDestructiveChange = true;
            }
        }
    }

    private static void OrderTemporalTransitionOperations(
        List<MigrationOperation> operations
    )
    {
        foreach (var transition in operations.OfType<AlterTableOperation>().ToArray())
        {
            var sourceIsTemporal = transition.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value is true;

            var targetIsTemporal = transition.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value is true;

            if (!sourceIsTemporal && targetIsTemporal)
            {
                MoveTemporalActivationAfterPeriodColumns(operations, transition, operations.IndexOf(transition));
                continue;
            }

            if (sourceIsTemporal && !targetIsTemporal)
            {
                MoveTemporalDeactivationBeforePeriodColumns(operations, transition, operations.IndexOf(transition));
            }
        }
    }

    private static void MoveTemporalActivationAfterPeriodColumns(
        List<MigrationOperation> operations,
        AlterTableOperation activation,
        int activationIndex
    )
    {
        var periodStartColumn = activation.FindAnnotation(MySqlAnnotationNames.TemporalPeriodStartColumn)
            ?.Value as string;

        var periodEndColumn = activation.FindAnnotation(MySqlAnnotationNames.TemporalPeriodEndColumn)
            ?.Value as string;

        var finalPeriodColumnIndex = operations.FindLastIndex(operation => operation is AddColumnOperation addColumn
            && SameTable(addColumn.Table, activation.Name)
            && string.Equals(addColumn.Schema, activation.Schema, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(addColumn.Name, periodStartColumn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(addColumn.Name, periodEndColumn, StringComparison.OrdinalIgnoreCase)));

        if (finalPeriodColumnIndex <= activationIndex)
        {
            return;
        }

        // EF emits the annotation-changing AlterTableOperation before its shadow
        // period columns. Temporal activation depends on both columns already
        // existing, so the provider must preserve that physical dependency.
        operations.RemoveAt(activationIndex);
        operations.Insert(finalPeriodColumnIndex, activation);
    }

    private static void MoveTemporalDeactivationBeforePeriodColumns(
        List<MigrationOperation> operations,
        AlterTableOperation deactivation,
        int deactivationIndex
    )
    {
        var periodStartColumn = deactivation
            .FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)
            ?.Value as string;

        var periodEndColumn = deactivation
            .FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)
            ?.Value as string;

        var firstPeriodColumnIndex = operations.FindIndex(
            operation => operation is DropColumnOperation dropColumn
                && SameTable(dropColumn.Table, deactivation.Name)
                && string.Equals(dropColumn.Schema, deactivation.Schema, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(dropColumn.Name, periodStartColumn, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dropColumn.Name, periodEndColumn, StringComparison.OrdinalIgnoreCase)));

        if (firstPeriodColumnIndex < 0
            || firstPeriodColumnIndex > deactivationIndex)
        {
            return;
        }

        // EF removes shadow period columns before the annotation-changing table
        // operation. Native MariaDB rejects that structural ALTER while system
        // versioning is active, and MySQL emulation still owns the columns until
        // its triggers and history table have been removed.
        operations.RemoveAt(deactivationIndex);
        operations.Insert(firstPeriodColumnIndex, deactivation);
    }

    private static void ApplyTemporalOperationAnnotations(
        IReadOnlyList<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        foreach (var operation in operations)
        {
            var (sourceName, sourceSchema, targetName, targetSchema) = GetOperationTables(operation);

            if (operation is AlterTableOperation alterTable)
            {
                ApplyTemporalContract(
                    operation,
                    alterTable.OldTable,
                    sourceContract: true);
            }
            else if (sourceName is not null && source is not null)
            {
                ApplyTemporalContract(
                    operation,
                    source.FindTable(sourceName, sourceSchema),
                    sourceContract: true);
            }

            if (targetName is not null && target is not null)
            {
                ApplyTemporalContract(
                    operation,
                    target.FindTable(targetName, targetSchema),
                    sourceContract: false);
            }
        }
    }

    private static (
        string? SourceName,
        string? SourceSchema,
        string? TargetName,
        string? TargetSchema) GetOperationTables(
        MigrationOperation operation
    ) => operation switch
    {
        CreateTableOperation create => (null, null, create.Name, create.Schema),
        DropTableOperation drop => (drop.Name, drop.Schema, null, null),
        RenameTableOperation rename => (
            rename.Name,
            rename.Schema,
            rename.NewName ?? rename.Name,
            rename.NewSchema ?? rename.Schema),
        AlterTableOperation alter => (alter.Name, alter.Schema, alter.Name, alter.Schema),
        DropPrimaryKeyOperation drop => (drop.Table, drop.Schema, drop.Table, drop.Schema),
        AddPrimaryKeyOperation add => (add.Table, add.Schema, add.Table, add.Schema),
        DropColumnOperation drop => (drop.Table, drop.Schema, drop.Table, drop.Schema),
        ColumnOperation column => (column.Table, column.Schema, column.Table, column.Schema),
        RenameColumnOperation rename => (rename.Table, rename.Schema, rename.Table, rename.Schema),
        _ => (null, null, null, null),
    };

    private static void ApplyTemporalContract(
        MigrationOperation operation,
        IAnnotatable? table,
        bool sourceContract
    )
    {
        if (table is ITable relationalTable)
        {
            ApplyTemporalContract(
                operation,
                MySqlTemporalMetadata.FindTableMetadata(relationalTable),
                sourceContract);

            return;
        }

        if (table?.FindAnnotation(MySqlAnnotationNames.IsTemporal)?.Value is not true)
        {
            return;
        }

        var isTemporalName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceIsTemporal
            : MySqlAnnotationNames.IsTemporal;

        var historyTableName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistoryTable
            : MySqlAnnotationNames.TemporalHistoryTable;

        var historySchemaName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistorySchema
            : MySqlAnnotationNames.TemporalHistorySchema;

        var periodStartName = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodStartColumn
            : MySqlAnnotationNames.TemporalPeriodStartColumn;

        var periodEndName = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodEndColumn
            : MySqlAnnotationNames.TemporalPeriodEndColumn;

        operation.SetAnnotation(isTemporalName, true);
        CopyAnnotation(table, operation, MySqlAnnotationNames.TemporalHistoryTable, historyTableName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.TemporalHistorySchema, historySchemaName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.TemporalPeriodStartColumn, periodStartName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.TemporalPeriodEndColumn, periodEndName);
    }

    private static void ApplyTemporalContract(
        MigrationOperation operation,
        MySqlTemporalTableMetadata? metadata,
        bool sourceContract
    )
    {
        if (metadata is null)
        {
            return;
        }

        var isTemporalName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceIsTemporal
            : MySqlAnnotationNames.IsTemporal;

        var historyTableName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistoryTable
            : MySqlAnnotationNames.TemporalHistoryTable;

        var historySchemaName = sourceContract
            ? MySqlAnnotationNames.TemporalSourceHistorySchema
            : MySqlAnnotationNames.TemporalHistorySchema;

        var periodStartName = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodStartColumn
            : MySqlAnnotationNames.TemporalPeriodStartColumn;

        var periodEndName = sourceContract
            ? MySqlAnnotationNames.TemporalSourcePeriodEndColumn
            : MySqlAnnotationNames.TemporalPeriodEndColumn;

        operation.SetAnnotation(isTemporalName, true);
        SetAnnotationIfNotNull(operation, historyTableName, metadata.HistoryTable);
        SetAnnotationIfNotNull(operation, historySchemaName, metadata.HistorySchema);
        operation.SetAnnotation(periodStartName, metadata.PeriodStartColumn);
        operation.SetAnnotation(periodEndName, metadata.PeriodEndColumn);
    }

    private static void SetAnnotationIfNotNull(
        MigrationOperation operation,
        string annotationName,
        object? value
    )
    {
        if (value is not null)
        {
            operation.SetAnnotation(annotationName, value);
        }
    }

    private static void CopyAnnotation(
        IAnnotatable source,
        MigrationOperation target,
        string sourceName,
        string targetName
    )
    {
        var value = source.FindAnnotation(sourceName)?.Value;

        if (value is not null)
        {
            target.SetAnnotation(targetName, value);
        }
    }
}
