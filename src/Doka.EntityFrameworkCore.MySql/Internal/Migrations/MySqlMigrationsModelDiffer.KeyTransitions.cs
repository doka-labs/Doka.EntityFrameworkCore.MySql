namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
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
            .Tables
            .SelectMany(table => table.ForeignKeyConstraints)
            .ToArray();

        var transitions = new List<ForeignKeyStoreTypeTransition>();

        foreach (var sourceForeignKey in source
                     .Tables
                     .SelectMany(table => table.ForeignKeyConstraints)
                     .OrderBy(foreignKey => foreignKey.Table.Schema, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(foreignKey => foreignKey.Table.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(foreignKey => foreignKey.Name, StringComparer.OrdinalIgnoreCase))
        {
            var targetForeignKey = targetForeignKeys.SingleOrDefault(
                candidate => ForeignKeysHaveSameShape(sourceForeignKey, candidate));

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
                    alterColumn[MySqlAnnotationNames.ValueGenerationStrategy] = MySqlValueGenerationStrategy.None;
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
            .SequenceEqual(target.Columns.Select(column => column.Name), StringComparer.OrdinalIgnoreCase);

    private static bool SameTable(
        string left,
        string right
    ) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ForeignKeyStoreTypeTransition(
        DropForeignKeyOperation Drop,
        AddForeignKeyOperation Add,
        IReadOnlyList<AlterColumnOperation> AffectedColumns
    );

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
        if (operation is null
            || before is null)
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
        if (operation is null
            || after is null)
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
}
