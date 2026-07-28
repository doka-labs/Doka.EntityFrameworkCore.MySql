namespace Doka.EntityFrameworkCore.MySql;

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

        if (target is null)
        {
            return operations;
        }

        ApplyDatabaseCharSetAnnotations(operations, source, target);
        ApplyIndexAnnotations(operations, target);
        RemoveDuplicateAlterColumnOperations(operations);
        NormalizeAutoIncrementPrimaryKeyOperations(operations, source, target);

        return operations;
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

    private static bool IsAutoIncrement(
        ColumnOperation operation
    ) => operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy)
        ?.Value is MySqlValueGenerationStrategy.AutoIncrement;

    private static void MoveBefore(
        List<MigrationOperation> operations,
        MigrationOperation operation,
        MigrationOperation? before
    )
    {
        if (before is null)
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
