namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Decorates EF Core model differencing with provider annotations and engine-safe operation ordering.
/// </summary>
internal sealed class MySqlMigrationsModelDiffer : IMigrationsModelDiffer
{
    private readonly IMigrationsModelDiffer _innerDiffer;

    public MySqlMigrationsModelDiffer(
        IMigrationsModelDiffer innerDiffer
    )
    {
        _innerDiffer = innerDiffer ?? throw new ArgumentNullException(nameof(innerDiffer));
    }

    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        if (source is null
            || target is null)
        {
            return _innerDiffer.HasDifferences(source, target);
        }

        return _innerDiffer.HasDifferences(source, target)
            || !string.Equals(GetDatabaseCharSet(source), GetDatabaseCharSet(target), StringComparison.Ordinal);
    }

    public IReadOnlyList<MigrationOperation> GetDifferences(
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        var operations = _innerDiffer
            .GetDifferences(source, target)
            .ToList();

        ApplyTemporalOperationAnnotations(operations, source, target);
        ApplyApplicationTimeOperationAnnotations(operations, source, target);
        EnsureApplicationTimePrimaryKeyTransitions(operations, source, target);
        ApplyApplicationTimeOperationAnnotations(operations, source, target);
        SplitApplicationTimePeriodChanges(operations);
        MarkTemporalDeactivationsDestructive(operations);
        OrderTemporalTransitionOperations(operations);
        OrderApplicationTimeTransitionOperations(operations);

        if (target is null)
        {
            return operations;
        }

        ApplyDatabaseCharSetAnnotations(operations, source, target);
        ApplyIndexAnnotations(operations, target);
        RemoveDuplicateAlterColumnOperations(operations);
        EnsureForeignKeysAroundStoreTypeChanges(operations, source, target);
        NormalizeAutoIncrementPrimaryKeyOperations(operations, source, target);

        return operations;
    }

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
        foreach (var transition in operations
                     .OfType<AlterTableOperation>()
                     .ToArray())
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

    private static void EnsureApplicationTimePrimaryKeyTransitions(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        if (source is null || target is null)
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
                        Columns = targetPrimaryKey.Columns.Select(column => column.Name).ToArray(),
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
        var annotationNames = sourceContract
            ? new[]
            {
                MySqlAnnotationNames.ApplicationTimeSourceIsApplicationTime,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodName,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodStartColumn,
                MySqlAnnotationNames.ApplicationTimeSourcePeriodEndColumn,
                MySqlAnnotationNames.ApplicationTimeSourceWithoutOverlaps,
            }
            : new[]
            {
                MySqlAnnotationNames.IsApplicationTime,
                MySqlAnnotationNames.ApplicationTimePeriodName,
                MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
                MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
                MySqlAnnotationNames.ApplicationTimeWithoutOverlaps,
            };

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
        foreach (var transition in operations
                     .OfType<AlterTableOperation>()
                     .ToArray())
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
            else if (sourceName is not null && source is not null)
            {
                ApplyApplicationTimeContract(
                    operation,
                    source.FindTable(sourceName, sourceSchema),
                    sourceContract: true);
            }

            if (targetName is not null && target is not null)
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

        if (table?.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)?.Value is not true)
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

    private static void RemoveDuplicateAlterColumnOperations(
        List<MigrationOperation> operations
    )
    {
        var uniqueAlterColumns = new List<AlterColumnOperation>();

        foreach (var alterColumn in operations.OfType<AlterColumnOperation>().ToArray())
        {
            // Table-sharing JSON mappings can independently report the same physical
            // column transition. Executing identical ALTER TABLE statements adds locking
            // and rebuild work without changing the resulting schema.
            if (uniqueAlterColumns.Any(existing => AlterColumnsGenerateEquivalentSql(existing, alterColumn)))
            {
                operations.Remove(alterColumn);
                continue;
            }

            uniqueAlterColumns.Add(alterColumn);
        }
    }

    private static bool AlterColumnsGenerateEquivalentSql(
        AlterColumnOperation left,
        AlterColumnOperation right
    ) => SameTable(left.Name, right.Name)
        && SameTable(left.Table, right.Table)
        && string.Equals(left.Schema, right.Schema, StringComparison.OrdinalIgnoreCase)
        && StoreTypesGenerateEquivalentSql(left, right)
        && left.IsRowVersion == right.IsRowVersion
        && left.IsNullable == right.IsNullable
        && ValuesAreEquivalent(left.DefaultValue, right.DefaultValue)
        && string.Equals(left.DefaultValueSql, right.DefaultValueSql, StringComparison.Ordinal)
        && string.Equals(left.ComputedColumnSql, right.ComputedColumnSql, StringComparison.Ordinal)
        && left.IsStored == right.IsStored
        && string.Equals(left.Comment, right.Comment, StringComparison.Ordinal)
        && string.Equals(left.Collation, right.Collation, StringComparison.OrdinalIgnoreCase)
        && left.OldColumn.IsNullable == right.OldColumn.IsNullable
        && HasComputedExpression(left.OldColumn) == HasComputedExpression(right.OldColumn)
        && left.OldColumn.IsStored == right.OldColumn.IsStored
        && (left.OldColumn.Comment is null) == (right.OldColumn.Comment is null)
        && (!RequiresNullValueUpdate(left) || left.DefaultValue is not null || left.ClrType == right.ClrType)
        && ProviderAnnotationsGenerateEquivalentSql(left, right);

    private static bool StoreTypesGenerateEquivalentSql(
        ColumnOperation left,
        ColumnOperation right
    )
    {
        if (left.ColumnType is not null
            || right.ColumnType is not null)
        {
            return string.Equals(left.ColumnType, right.ColumnType, StringComparison.OrdinalIgnoreCase);
        }

        return left.ClrType == right.ClrType
            && left.IsUnicode == right.IsUnicode
            && left.IsFixedLength == right.IsFixedLength
            && left.MaxLength == right.MaxLength
            && left.Precision == right.Precision
            && left.Scale == right.Scale;
    }

    private static bool ProviderAnnotationsGenerateEquivalentSql(
        ColumnOperation left,
        ColumnOperation right
    ) => AnnotationValuesAreEquivalent(left, right, MySqlAnnotationNames.ValueGenerationStrategy)
        && AnnotationValuesAreEquivalent(left, right, MySqlAnnotationNames.Invisible)
        && AnnotationValuesAreEquivalent(left, right, MySqlAnnotationNames.SpatialReferenceSystemId);

    private static bool AnnotationValuesAreEquivalent(
        ColumnOperation left,
        ColumnOperation right,
        string annotationName
    ) => ValuesAreEquivalent(
        left.FindAnnotation(annotationName)?.Value,
        right.FindAnnotation(annotationName)?.Value);

    private static bool HasComputedExpression(
        ColumnOperation operation
    ) => !string.IsNullOrWhiteSpace(operation.ComputedColumnSql);

    private static bool RequiresNullValueUpdate(
        AlterColumnOperation operation
    ) => operation.OldColumn.IsNullable && !operation.IsNullable && !HasComputedExpression(operation);

    private static bool ValuesAreEquivalent(
        object? left,
        object? right
    ) => left is Array || right is Array
        ? System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(left, right)
        : Equals(left, right);

    private static void EnsureForeignKeysAroundStoreTypeChanges(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        if (source is null)
        {
            return;
        }

        var storeTypeChanges = operations
            .OfType<AlterColumnOperation>()
            .Where(operation => !StoreTypesGenerateEquivalentSql(operation, operation.OldColumn))
            .ToArray();

        if (storeTypeChanges.Length == 0)
        {
            return;
        }

        var targetForeignKeys = target
            .Tables.SelectMany(table => table.ForeignKeyConstraints)
            .ToArray();

        var transitions = new List<ForeignKeyStoreTypeTransition>();

        foreach (var sourceForeignKey in source
                     .Tables.SelectMany(table => table.ForeignKeyConstraints)
                     .OrderBy(foreignKey => foreignKey.Table.Schema, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(foreignKey => foreignKey.Table.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(foreignKey => foreignKey.Name, StringComparer.OrdinalIgnoreCase))
        {
            var targetForeignKey =
                targetForeignKeys.SingleOrDefault(candidate => ForeignKeysHaveSameShape(sourceForeignKey, candidate));

            if (targetForeignKey is null
                || HasForeignKeyLifecycleOperation(operations, sourceForeignKey, targetForeignKey))
            {
                continue;
            }

            var affectedColumns = storeTypeChanges
                .Where(operation => ForeignKeyUsesColumn(sourceForeignKey, operation))
                .ToArray();

            if (affectedColumns.Length == 0)
            {
                continue;
            }

            var drop = new DropForeignKeyOperation
            {
                Schema = sourceForeignKey.Table.Schema,
                Table = sourceForeignKey.Table.Name,
                Name = sourceForeignKey.Name,
            };

            drop.AddAnnotations(sourceForeignKey.GetAnnotations());

            transitions.Add(
                new ForeignKeyStoreTypeTransition(
                    drop,
                    AddForeignKeyOperation.CreateFrom(targetForeignKey),
                    affectedColumns));
        }

        foreach (var transition in transitions)
        {
            var firstAlterIndex = transition.AffectedColumns.Min(operations.IndexOf);

            operations.Insert(firstAlterIndex, transition.Drop);
        }

        foreach (var transition in transitions)
        {
            var restoreIndex = transition.AffectedColumns.Max(operations.IndexOf) + 1;

            var requiredIndex = operations
                .OfType<CreateIndexOperation>()
                .Where(operation => CreatesRequiredForeignKeyIndex(operation, transition.Add))
                .Select(operation => operations.IndexOf(operation))
                .DefaultIfEmpty(-1)
                .Max();

            operations.Insert(Math.Max(restoreIndex, requiredIndex + 1), transition.Add);
        }
    }

    private static bool ForeignKeysHaveSameShape(
        IForeignKeyConstraint source,
        IForeignKeyConstraint target
    ) => string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
        && SameStoreObject(source.Table, target.Table)
        && SameStoreObject(source.PrincipalTable, target.PrincipalTable)
        && source
            .Columns
            .Select(column => column.Name)
            .SequenceEqual(target.Columns.Select(column => column.Name), StringComparer.OrdinalIgnoreCase)
        && source
            .PrincipalColumns
            .Select(column => column.Name)
            .SequenceEqual(target.PrincipalColumns.Select(column => column.Name), StringComparer.OrdinalIgnoreCase)
        && source.OnDeleteAction == target.OnDeleteAction;

    private static bool CreatesRequiredForeignKeyIndex(
        CreateIndexOperation index,
        AddForeignKeyOperation foreignKey
    ) => SameTable(index.Table, foreignKey.Table)
        && string.Equals(index.Schema, foreignKey.Schema, StringComparison.OrdinalIgnoreCase)
        && index
            .Columns
            .Take(foreignKey.Columns.Length)
            .SequenceEqual(foreignKey.Columns, StringComparer.OrdinalIgnoreCase);

    private static bool SameStoreObject(
        ITable source,
        ITable target
    ) => SameTable(source.Name, target.Name)
        && string.Equals(source.Schema, target.Schema, StringComparison.OrdinalIgnoreCase);

    private static bool HasForeignKeyLifecycleOperation(
        IEnumerable<MigrationOperation> operations,
        IForeignKeyConstraint source,
        IForeignKeyConstraint target
    ) => operations.Any(operation => operation switch
    {
        DropForeignKeyOperation drop => SameTable(drop.Table, source.Table.Name)
            && string.Equals(drop.Schema, source.Table.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(drop.Name, source.Name, StringComparison.OrdinalIgnoreCase),
        AddForeignKeyOperation add => SameTable(add.Table, target.Table.Name)
            && string.Equals(add.Schema, target.Table.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(add.Name, target.Name, StringComparison.OrdinalIgnoreCase),
        _ => false,
    });

    private static bool ForeignKeyUsesColumn(
        IForeignKeyConstraint foreignKey,
        AlterColumnOperation operation
    ) => UsesColumn(foreignKey.Table, foreignKey.Columns, operation)
        || UsesColumn(foreignKey.PrincipalTable, foreignKey.PrincipalColumns, operation);

    private static bool UsesColumn(
        ITable table,
        IEnumerable<IColumn> columns,
        AlterColumnOperation operation
    ) => SameTable(table.Name, operation.Table)
        && string.Equals(table.Schema, operation.Schema, StringComparison.OrdinalIgnoreCase)
        && columns.Any(column => SameTable(column.Name, operation.Name));

    private static void NormalizeAutoIncrementPrimaryKeyOperations(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        RemoveRenamePrimaryKeyChurn(operations, source, target);
        RemovePrimaryKeyDropsCoveredByColumnDrops(operations, source);
        MovePrimaryKeyAddsBeforeAutoIncrementEnabling(operations);
        MoveAutoIncrementDisablingBeforePrimaryKeyDrops(operations);
    }

    private static void RemoveRenamePrimaryKeyChurn(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        if (source is null)
        {
            return;
        }

        foreach (var rename in operations.OfType<RenameTableOperation>().ToArray())
        {
            var sourceTable = FindTable(source, rename.Name);
            var targetTable = FindTable(target, rename.NewName ?? rename.Name);

            if (!PrimaryKeysHaveSameColumns(sourceTable?.PrimaryKey, targetTable?.PrimaryKey))
            {
                continue;
            }

            operations.RemoveAll(operation =>
                (operation is DropPrimaryKeyOperation drop && SameTable(drop.Table, rename.Name))
                || (operation is AddPrimaryKeyOperation add
                    && SameTable(add.Table, rename.NewName ?? rename.Name)
                    && add.Columns.SequenceEqual(
                        targetTable!.PrimaryKey!.Columns.Select(column => column.Name),
                        StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static void RemovePrimaryKeyDropsCoveredByColumnDrops(
        List<MigrationOperation> operations,
        IRelationalModel? source
    )
    {
        if (source is null)
        {
            return;
        }

        foreach (var dropPrimaryKey in operations.OfType<DropPrimaryKeyOperation>().ToArray())
        {
            var primaryKey = FindTable(source, dropPrimaryKey.Table)
                ?.PrimaryKey;

            if (primaryKey is null)
            {
                continue;
            }

            var droppedColumns = operations
                .OfType<DropColumnOperation>()
                .Where(drop => SameTable(drop.Table, dropPrimaryKey.Table))
                .Select(drop => drop.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (primaryKey.Columns.All(column => droppedColumns.Contains(column.Name)))
            {
                operations.Remove(dropPrimaryKey);
            }
        }
    }

    private static void MovePrimaryKeyAddsBeforeAutoIncrementEnabling(
        List<MigrationOperation> operations
    )
    {
        foreach (var addPrimaryKey in operations.OfType<AddPrimaryKeyOperation>().ToArray())
        {
            var alterColumn = operations
                .OfType<AlterColumnOperation>()
                .FirstOrDefault(alter =>
                    SameTable(alter.Table, addPrimaryKey.Table)
                    && addPrimaryKey.Columns.Contains(alter.Name, StringComparer.OrdinalIgnoreCase)
                    && IsAutoIncrement(alter)
                    && !IsAutoIncrement(alter.OldColumn));

            MoveBefore(operations, addPrimaryKey, alterColumn);
        }
    }

    private static void MoveAutoIncrementDisablingBeforePrimaryKeyDrops(
        List<MigrationOperation> operations
    )
    {
        foreach (var dropPrimaryKey in operations.OfType<DropPrimaryKeyOperation>().ToArray())
        {
            var alterColumns = operations
                .OfType<AlterColumnOperation>()
                .Where(alter =>
                    SameTable(alter.Table, dropPrimaryKey.Table)
                    && !IsAutoIncrement(alter)
                    && IsAutoIncrement(alter.OldColumn))
                .ToArray();

            foreach (var alterColumn in alterColumns)
            {
                // Preserve the transition explicitly. A missing annotation and None both
                // mean "not generated" in the target model, but the migration operation
                // must record that AUTO_INCREMENT is being removed before the key drop.
                if (alterColumn.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy) is null)
                {
                    alterColumn[MySqlAnnotationNames.ValueGenerationStrategy] =
                        MySqlValueGenerationStrategy.None;
                }

                MoveBefore(operations, alterColumn, dropPrimaryKey);
            }
        }
    }

    private static ITable? FindTable(
        IRelationalModel model,
        string name
    ) => model.Tables.FirstOrDefault(table => SameTable(table.Name, name));

    private static bool PrimaryKeysHaveSameColumns(
        IPrimaryKeyConstraint? source,
        IPrimaryKeyConstraint? target
    ) => source is not null
        && target is not null
        && source
            .Columns
            .Select(column => column.Name)
            .SequenceEqual(
                target.Columns.Select(column => column.Name),
                StringComparer.OrdinalIgnoreCase);

    private static bool SameTable(
        string left,
        string right
    ) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ApplicationTimePeriodIdentity(
        string PeriodName,
        string PeriodStartColumn,
        string PeriodEndColumn);

    private readonly record struct ForeignKeyStoreTypeTransition(
        DropForeignKeyOperation Drop,
        AddForeignKeyOperation Add,
        IReadOnlyList<AlterColumnOperation> AffectedColumns);

    private static bool IsAutoIncrement(
        ColumnOperation operation
    ) => operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
        ?.Value is MySqlValueGenerationStrategy.AutoIncrement;

    private static void MoveBefore(
        List<MigrationOperation> operations,
        MigrationOperation? operation,
        MigrationOperation? before
    )
    {
        if (operation is null || before is null)
        {
            return;
        }

        var operationIndex = operations.IndexOf(operation);
        var beforeIndex = operations.IndexOf(before);

        if (operationIndex < beforeIndex)
        {
            return;
        }

        operations.RemoveAt(operationIndex);
        operations.Insert(beforeIndex, operation);
    }

    private static void MoveAfter(
        List<MigrationOperation> operations,
        MigrationOperation? operation,
        MigrationOperation? after
    )
    {
        if (operation is null || after is null)
        {
            return;
        }

        var operationIndex = operations.IndexOf(operation);
        var afterIndex = operations.IndexOf(after);

        if (operationIndex > afterIndex)
        {
            return;
        }

        operations.RemoveAt(operationIndex);
        afterIndex = operations.IndexOf(after);
        operations.Insert(afterIndex + 1, operation);
    }

    private static void ApplyDatabaseCharSetAnnotations(
        List<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        var sourceCharSet = source is null ? null : GetDatabaseCharSet(source);
        var targetCharSet = GetDatabaseCharSet(target);

        if (string.Equals(sourceCharSet, targetCharSet, StringComparison.Ordinal))
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

        if (string.IsNullOrWhiteSpace(targetCharSet))
        {
            alterDatabaseOperation.RemoveAnnotation(MySqlAnnotationNames.CharSet);
            return;
        }

        alterDatabaseOperation.SetAnnotation(MySqlAnnotationNames.CharSet, targetCharSet);
    }

    private static string? GetDatabaseCharSet(
        IRelationalModel model
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value as string
            ?? model.Model.GetMySqlCharSet();
    }

    private static void ApplyIndexAnnotations(
        List<MigrationOperation> operations,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        // The model is authoritative here. Relational index mappings can be incomplete
        // while EF constructs them, so using a first mapped annotation can leak metadata
        // from a neighboring index into the operation.
        var indexMetadata = new Dictionary<
            (string? Schema, string Table, string IndexName),
            (bool Spatial, bool FullText, int[]? PrefixLengths)>();

        foreach (var entityType in target.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();

            if (tableName is null)
            {
                continue;
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();

                if (indexName is null)
                {
                    continue;
                }

                var key = (schema, tableName, indexName);
                var metadata = (
                    Spatial: index.GetMySqlSpatialIndex(),
                    FullText: index.GetMySqlFullTextIndex(),
                    PrefixLengths: index
                        .GetMySqlIndexPrefixLengths()
                        ?.ToArray());

                if (indexMetadata.TryGetValue(key, out var existingMetadata)
                    && (existingMetadata.Spatial != metadata.Spatial
                        || existingMetadata.FullText != metadata.FullText
                        || !ValuesAreEquivalent(existingMetadata.PrefixLengths, metadata.PrefixLengths)))
                {
                    throw new InvalidOperationException(
                        $"Mapped indexes for '{tableName}.{indexName}' have conflicting MySQL metadata.");
                }

                indexMetadata[key] = metadata;
            }
        }

        foreach (var createIndexOperation in operations.OfType<CreateIndexOperation>())
        {
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.SpatialIndex);
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.FullTextIndex);
            createIndexOperation.RemoveAnnotation(MySqlAnnotationNames.IndexPrefixLength);

            if (!indexMetadata.TryGetValue(
                    (createIndexOperation.Schema, createIndexOperation.Table, createIndexOperation.Name),
                    out var metadata))
            {
                continue;
            }

            if (metadata.Spatial)
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
            }

            if (metadata.FullText)
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
            }

            if (metadata.PrefixLengths is not null)
            {
                createIndexOperation.SetAnnotation(
                    MySqlAnnotationNames.IndexPrefixLength,
                    metadata.PrefixLengths);
            }
        }
    }
}
