namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
    private static void EnsureApplicationTimePrimaryKeyTransitions(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        if (source is null
            || target is null)
        {
            return;
        }

        foreach (var targetTable in target.Tables)
        {
            var sourceTable = source.FindTable(targetTable.Name, targetTable.Schema);

            if (sourceTable is null
                || operations
                    .OfType<CreateTableOperation>()
                    .Any(operation => SameTable(operation.Name, targetTable.Name)
                        && string.Equals(operation.Schema, targetTable.Schema, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var sourceContract = MySqlApplicationTimeMetadata.FindTableMetadata(sourceTable);
            var targetContract = MySqlApplicationTimeMetadata.FindTableMetadata(targetTable);

            if (!ApplicationTimePrimaryKeyContractChanged(sourceContract, targetContract))
            {
                continue;
            }

            if (sourceTable.PrimaryKey is { } sourcePrimaryKey
                && !operations
                    .OfType<DropPrimaryKeyOperation>()
                    .Any(operation => SameTable(operation.Table, sourceTable.Name)
                        && string.Equals(operation.Schema, sourceTable.Schema, StringComparison.OrdinalIgnoreCase)))
            {
                operations.Add(
                    new DropPrimaryKeyOperation
                    {
                        Name = sourcePrimaryKey.Name,
                        Table = sourceTable.Name,
                        Schema = sourceTable.Schema,
                    });
            }

            if (targetTable.PrimaryKey is { } targetPrimaryKey
                && !operations
                    .OfType<AddPrimaryKeyOperation>()
                    .Any(operation => SameTable(operation.Table, targetTable.Name)
                        && string.Equals(operation.Schema, targetTable.Schema, StringComparison.OrdinalIgnoreCase)))
            {
                operations.Add(
                    new AddPrimaryKeyOperation
                    {
                        Name = targetPrimaryKey.Name,
                        Table = targetTable.Name,
                        Schema = targetTable.Schema,
                        Columns = targetPrimaryKey
                            .Columns
                            .Select(column => column.Name)
                            .ToArray(),
                    });
            }
        }
    }

    private static bool ApplicationTimePrimaryKeyContractChanged(
        MySqlApplicationTimeTableMetadata? source,
        MySqlApplicationTimeTableMetadata? target
    )
    {
        var sourceUsesPeriod = source?.WithoutOverlaps is true;
        var targetUsesPeriod = target?.WithoutOverlaps is true;

        return sourceUsesPeriod != targetUsesPeriod
            || (sourceUsesPeriod
                && (!string.Equals(source!.PeriodName, target!.PeriodName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        source.PeriodStartColumn,
                        target.PeriodStartColumn,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        source.PeriodEndColumn,
                        target.PeriodEndColumn,
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static void SplitApplicationTimePeriodChanges(
        List<MigrationOperation> operations
    )
    {
        foreach (var transition in operations.OfType<AlterTableOperation>().ToArray())
        {
            if (!TryGetApplicationTimePeriodIdentity(transition, sourceContract: true, out var sourceIdentity)
                || !TryGetApplicationTimePeriodIdentity(transition, sourceContract: false, out var targetIdentity)
                || sourceIdentity == targetIdentity)
            {
                continue;
            }

            var transitionIndex = operations.IndexOf(transition);
            var deactivation = new AlterTableOperation
            {
                Name = transition.Name,
                Schema = transition.Schema,
            };

            var activation = new AlterTableOperation
            {
                Name = transition.Name,
                Schema = transition.Schema,
            };

            MoveApplicationTimeAnnotations(transition, deactivation, sourceContract: true);
            MoveApplicationTimeAnnotations(transition, activation, sourceContract: false);

            operations.RemoveAt(transitionIndex);
            operations.Insert(transitionIndex, deactivation);

            if (transition.GetAnnotations().Any()
                || !string.Equals(transition.Comment, transition.OldTable.Comment, StringComparison.Ordinal))
            {
                operations.Insert(++transitionIndex, transition);
            }

            operations.Insert(++transitionIndex, activation);
        }
    }

    private static bool TryGetApplicationTimePeriodIdentity(
        MigrationOperation operation,
        bool sourceContract,
        out ApplicationTimePeriodIdentity identity
    )
    {
        if (operation.FindAnnotation(
                    sourceContract
                        ? MySqlAnnotationNames.ApplicationTimeSourcePeriodName
                        : MySqlAnnotationNames.ApplicationTimePeriodName)
                ?.Value is not string periodName
            || operation.FindAnnotation(
                    sourceContract
                        ? MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn
                        : MySqlAnnotationNames.ApplicationTimePeriodStartColumn)
                ?.Value is not string periodStartColumn
            || operation.FindAnnotation(
                    sourceContract
                        ? MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn
                        : MySqlAnnotationNames.ApplicationTimePeriodEndColumn)
                ?.Value is not string periodEndColumn)
        {
            identity = default;
            return false;
        }

        identity = new ApplicationTimePeriodIdentity(
            periodName.ToUpperInvariant(),
            periodStartColumn.ToUpperInvariant(),
            periodEndColumn.ToUpperInvariant());

        return true;
    }

    private static void MoveApplicationTimeAnnotations(
        MigrationOperation source,
        MigrationOperation target,
        bool sourceContract
    )
    {
        string[] annotationNames = sourceContract
            ? [
                MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodName,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn,
                MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps,
            ]
            : [
                MySqlAnnotationNames.IsApplicationTime,
                MySqlAnnotationNames.ApplicationTimePeriodName,
                MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
                MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
                MySqlAnnotationNames.ApplicationTimeWithoutOverlaps,
            ];

        foreach (var annotationName in annotationNames)
        {
            if (source.FindAnnotation(annotationName) is not { } annotation)
            {
                continue;
            }

            target.SetAnnotation(annotationName, annotation.Value);
            source.RemoveAnnotation(annotationName);
        }
    }

    private static void OrderApplicationTimeTransitionOperations(
        List<MigrationOperation> operations
    )
    {
        foreach (var transition in operations.OfType<AlterTableOperation>().ToArray())
        {
            var sourceIsApplicationTime = transition
                .FindAnnotation(MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime)
                ?.Value is true;

            var targetIsApplicationTime = transition.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)
                ?.Value is true;

            if (!sourceIsApplicationTime && targetIsApplicationTime)
            {
                MoveApplicationTimeActivationAfterPeriodColumns(operations, transition, operations.IndexOf(transition));
            }
            else if (sourceIsApplicationTime && !targetIsApplicationTime)
            {
                MoveApplicationTimeDeactivationBeforePeriodColumns(
                    operations,
                    transition,
                    operations.IndexOf(transition));
            }

            var dropPrimaryKey = operations
                .OfType<DropPrimaryKeyOperation>()
                .FirstOrDefault(operation => SameOperationTable(operation.Table, operation.Schema, transition));

            var addPrimaryKey = operations
                .OfType<AddPrimaryKeyOperation>()
                .FirstOrDefault(operation => SameOperationTable(operation.Table, operation.Schema, transition));

            MoveBefore(operations, dropPrimaryKey, transition);
            MoveAfter(operations, addPrimaryKey, transition);
        }
    }

    private static void MoveApplicationTimeActivationAfterPeriodColumns(
        List<MigrationOperation> operations,
        AlterTableOperation activation,
        int activationIndex
    )
    {
        var periodStartColumn = activation.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodStartColumn)
            ?.Value as string;

        var periodEndColumn = activation.FindAnnotation(MySqlAnnotationNames.ApplicationTimePeriodEndColumn)
            ?.Value as string;

        var finalPeriodColumnIndex = operations.FindLastIndex(operation => OperationMaterializesPeriodColumn(
            operation,
            activation,
            periodStartColumn,
            periodEndColumn));

        if (finalPeriodColumnIndex <= activationIndex)
        {
            return;
        }

        operations.RemoveAt(activationIndex);
        operations.Insert(finalPeriodColumnIndex, activation);
    }

    private static void MoveApplicationTimeDeactivationBeforePeriodColumns(
        List<MigrationOperation> operations,
        AlterTableOperation deactivation,
        int deactivationIndex
    )
    {
        var periodStartColumn = deactivation.FindAnnotation(MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn)
            ?.Value as string;

        var periodEndColumn = deactivation.FindAnnotation(MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn)
            ?.Value as string;

        var firstPeriodColumnIndex = operations.FindIndex(operation => OperationChangesPeriodColumn(
            operation,
            deactivation,
            periodStartColumn,
            periodEndColumn));

        if (firstPeriodColumnIndex < 0
            || firstPeriodColumnIndex > deactivationIndex)
        {
            return;
        }

        operations.RemoveAt(deactivationIndex);
        operations.Insert(firstPeriodColumnIndex, deactivation);
    }

    private static bool OperationMaterializesPeriodColumn(
        MigrationOperation operation,
        AlterTableOperation transition,
        string? periodStartColumn,
        string? periodEndColumn
    ) => operation switch
    {
        AddColumnOperation addColumn => SameOperationTable(addColumn.Table, addColumn.Schema, transition)
            && IsPeriodColumn(addColumn.Name, periodStartColumn, periodEndColumn),
        AlterColumnOperation alterColumn => SameOperationTable(alterColumn.Table, alterColumn.Schema, transition)
            && IsPeriodColumn(alterColumn.Name, periodStartColumn, periodEndColumn),
        RenameColumnOperation renameColumn => SameOperationTable(renameColumn.Table, renameColumn.Schema, transition)
            && IsPeriodColumn(renameColumn.NewName, periodStartColumn, periodEndColumn),
        _ => false,
    };

    private static bool OperationChangesPeriodColumn(
        MigrationOperation operation,
        AlterTableOperation transition,
        string? periodStartColumn,
        string? periodEndColumn
    ) => operation switch
    {
        DropColumnOperation dropColumn => SameOperationTable(dropColumn.Table, dropColumn.Schema, transition)
            && IsPeriodColumn(dropColumn.Name, periodStartColumn, periodEndColumn),
        AlterColumnOperation alterColumn => SameOperationTable(alterColumn.Table, alterColumn.Schema, transition)
            && IsPeriodColumn(alterColumn.Name, periodStartColumn, periodEndColumn),
        RenameColumnOperation renameColumn => SameOperationTable(renameColumn.Table, renameColumn.Schema, transition)
            && IsPeriodColumn(renameColumn.Name, periodStartColumn, periodEndColumn),
        _ => false,
    };

    private static bool IsPeriodColumn(
        string? columnName,
        string? periodStartColumn,
        string? periodEndColumn
    ) => string.Equals(columnName, periodStartColumn, StringComparison.OrdinalIgnoreCase)
        || string.Equals(columnName, periodEndColumn, StringComparison.OrdinalIgnoreCase);

    private static bool SameOperationTable(
        string table,
        string? schema,
        AlterTableOperation operation
    ) => SameTable(table, operation.Name)
        && string.Equals(schema, operation.Schema, StringComparison.OrdinalIgnoreCase);

    private static void ApplyApplicationTimeOperationAnnotations(
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
                ApplyApplicationTimeContract(operation, alterTable.OldTable, sourceContract: true);
            }
            else if (sourceName is not null
                     && source is not null)
            {
                ApplyApplicationTimeContract(
                    operation,
                    source.FindTable(sourceName, sourceSchema),
                    sourceContract: true);
            }

            if (targetName is not null
                && target is not null)
            {
                ApplyApplicationTimeContract(
                    operation,
                    target.FindTable(targetName, targetSchema),
                    sourceContract: false);
            }

            if (operation is CreateTableOperation { PrimaryKey: { } primaryKey })
            {
                ApplyApplicationTimeContract(
                    primaryKey,
                    targetName is null ? null : target?.FindTable(targetName, targetSchema),
                    sourceContract: false);
            }
        }
    }

    private static void ApplyApplicationTimeContract(
        MigrationOperation operation,
        IAnnotatable? table,
        bool sourceContract
    )
    {
        if (table is ITable relationalTable)
        {
            ApplyApplicationTimeContract(
                operation,
                MySqlApplicationTimeMetadata.FindTableMetadata(relationalTable),
                sourceContract);

            return;
        }

        if (table?.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)
                ?.Value is not true)
        {
            return;
        }

        var isApplicationTimeName = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime
            : MySqlAnnotationNames.IsApplicationTime;

        var periodName = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodName
            : MySqlAnnotationNames.ApplicationTimePeriodName;

        var periodStartName = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn
            : MySqlAnnotationNames.ApplicationTimePeriodStartColumn;

        var periodEndName = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn
            : MySqlAnnotationNames.ApplicationTimePeriodEndColumn;

        var withoutOverlapsName = sourceContract
            ? MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps
            : MySqlAnnotationNames.ApplicationTimeWithoutOverlaps;

        operation.SetAnnotation(isApplicationTimeName, true);
        CopyAnnotation(table, operation, MySqlAnnotationNames.ApplicationTimePeriodName, periodName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.ApplicationTimePeriodStartColumn, periodStartName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.ApplicationTimePeriodEndColumn, periodEndName);
        CopyAnnotation(table, operation, MySqlAnnotationNames.ApplicationTimeWithoutOverlaps, withoutOverlapsName);
    }

    private static void ApplyApplicationTimeContract(
        MigrationOperation operation,
        MySqlApplicationTimeTableMetadata? metadata,
        bool sourceContract
    )
    {
        if (metadata is null)
        {
            return;
        }

        operation.SetAnnotation(
            sourceContract
                ? MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime
                : MySqlAnnotationNames.IsApplicationTime,
            true);

        operation.SetAnnotation(
            sourceContract
                ? MySqlAnnotationNames.ApplicationTimeSourcePeriodName
                : MySqlAnnotationNames.ApplicationTimePeriodName,
            metadata.PeriodName);

        operation.SetAnnotation(
            sourceContract
                ? MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn
                : MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
            metadata.PeriodStartColumn);

        operation.SetAnnotation(
            sourceContract
                ? MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn
                : MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
            metadata.PeriodEndColumn);

        operation.SetAnnotation(
            sourceContract
                ? MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps
                : MySqlAnnotationNames.ApplicationTimeWithoutOverlaps,
            metadata.WithoutOverlaps);
    }

    private readonly record struct ApplicationTimePeriodIdentity(
        string PeriodName,
        string PeriodStartColumn,
        string PeriodEndColumn
    );
}
