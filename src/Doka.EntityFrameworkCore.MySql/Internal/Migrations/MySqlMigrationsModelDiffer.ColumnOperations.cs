namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsModelDiffer
{
    private static void RemoveEfCoreSyntheticBackfillDefaults(
        IEnumerable<MigrationOperation> operations,
        IRelationalModel? target
    )
    {
        if (target is null)
        {
            return;
        }

        foreach (var operation in operations)
        {
            if (operation is AlterColumnOperation { IsNullable: false, OldColumn.IsNullable: true, } alterColumn
                && string.IsNullOrWhiteSpace(alterColumn.ComputedColumnSql)
                && !TargetDeclaresDefault(target, alterColumn))
            {
                alterColumn.DefaultValue = null;
                alterColumn.SetAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill, true);
                continue;
            }

            if (operation is AddColumnOperation { IsNullable: false, } addColumn
                && string.IsNullOrWhiteSpace(addColumn.ComputedColumnSql)
                && !TargetDeclaresDefault(target, addColumn))
            {
                addColumn.DefaultValue = null;
                addColumn.SetAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill, true);
            }
        }
    }

    private static bool TargetDeclaresDefault(
        IRelationalModel target,
        ColumnOperation operation
    )
    {
        var column = target
            .FindTable(operation.Table, operation.Schema)
            ?.FindColumn(operation.Name);

        if (column is null)
        {
            return true;
        }

        return (column.TryGetDefaultValue(out var defaultValue) && defaultValue is not null)
            || !string.IsNullOrWhiteSpace(column.DefaultValueSql);
    }

    private static void NormalizeProviderColumnOperations(
        IReadOnlyList<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        // Most diffs contain no renames. Avoid rescanning every operation for
        // each expression-bearing old column in that common migration path.
        var renameOperations =
            operations.Any(static operation => operation is RenameTableOperation or RenameColumnOperation)
                ? operations
                : null;

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateTableOperation createTable:
                    foreach (var column in createTable.Columns)
                    {
                        RestoreExpressionColumnGenerationStrategy(column, target);
                        NormalizeProviderGuidColumnOperation(column);
                        NormalizeProviderOwnedConverterColumnOperation(column, target);
                    }

                    break;
                case AlterColumnOperation alterColumn:
                    RestoreExpressionColumnGenerationStrategy(alterColumn, target);
                    NormalizeProviderGuidColumnOperation(alterColumn);
                    NormalizeProviderOwnedConverterColumnOperation(alterColumn, target);
                    RestoreExpressionColumnGenerationStrategy(
                        alterColumn.OldColumn,
                        source,
                        alterColumn,
                        renameOperations);
                    NormalizeProviderGuidColumnOperation(alterColumn.OldColumn);
                    NormalizeProviderOwnedConverterColumnOperation(alterColumn.OldColumn, source);
                    break;
                case ColumnOperation column:
                    RestoreExpressionColumnGenerationStrategy(column, target);
                    NormalizeProviderGuidColumnOperation(column);
                    NormalizeProviderOwnedConverterColumnOperation(column, target);
                    break;
            }
        }
    }

    private static void RestoreExpressionColumnGenerationStrategy(
        ColumnOperation operation,
        IRelationalModel? relationalModel,
        ColumnOperation? identity = null,
        IReadOnlyList<MigrationOperation>? renameOperations = null
    )
    {
        if (operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy) is not null
            || (operation.DefaultValueSql is null && operation.ComputedColumnSql is null))
        {
            return;
        }

        // Runtime-only annotations are excluded from relational comparison, not
        // from the public migration-operation contract. OldColumn can omit its
        // identity, while a combined rename and alteration uses target names.
        // Invert EF's explicit rename operations before reading source metadata.
        var columnIdentity = identity ?? operation;
        var column = renameOperations is null
            ? relationalModel
                ?.FindTable(columnIdentity.Table, columnIdentity.Schema)
                ?.FindColumn(columnIdentity.Name)
            : FindSourceExpressionColumn(relationalModel, columnIdentity, renameOperations);

        if (column is null)
        {
            return;
        }

        foreach (var mapping in column.PropertyMappings)
        {
            var strategy = mapping.Property.GetMySqlValueGenerationStrategy();

            if (strategy is MySqlValueGenerationStrategy.ClientGuid or MySqlValueGenerationStrategy.None)
            {
                operation.SetAnnotation(MySqlAnnotationNames.ValueGenerationStrategy, strategy.Value);
                return;
            }
        }
    }

    private static IColumn? FindSourceExpressionColumn(
        IRelationalModel? source,
        ColumnOperation targetIdentity,
        IReadOnlyList<MigrationOperation> operations
    )
    {
        if (source is null)
        {
            return null;
        }

        var sourceTable = targetIdentity.Table;
        var sourceSchema = targetIdentity.Schema;
        var sourceColumn = targetIdentity.Name;

        foreach (var operation in operations)
        {
            if (operation is RenameTableOperation rename
                && string.Equals(rename.NewName ?? rename.Name, targetIdentity.Table, StringComparison.Ordinal)
                && string.Equals(rename.NewSchema, targetIdentity.Schema, StringComparison.Ordinal))
            {
                sourceTable = rename.Name;
                sourceSchema = rename.Schema;
                break;
            }
        }

        foreach (var operation in operations)
        {
            if (operation is RenameColumnOperation rename
                && string.Equals(rename.NewName, targetIdentity.Name, StringComparison.Ordinal)
                && string.Equals(rename.Table, targetIdentity.Table, StringComparison.Ordinal)
                && string.Equals(rename.Schema, targetIdentity.Schema, StringComparison.Ordinal))
            {
                sourceColumn = rename.Name;
                break;
            }
        }

        return source
            .FindTable(sourceTable, sourceSchema)
            ?.FindColumn(sourceColumn);
    }

    private static void NormalizeProviderOwnedConverterColumnOperation(
        ColumnOperation operation,
        IRelationalModel? relationalModel
    )
    {
        var mappedProperties = relationalModel
            ?.FindTable(operation.Table, operation.Schema)
            ?.FindColumn(operation.Name)
            ?.PropertyMappings
            .Select(mapping => mapping.Property)
            .ToArray();

        if (mappedProperties is not { Length: > 0 })
        {
            return;
        }

        Type? modelClrType = null;

        foreach (var property in mappedProperties)
        {
            if (property.GetRelationalTypeMapping() is not IMySqlProviderOwnedModelTypeMapping)
            {
                return;
            }

            var propertyClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

            if (modelClrType is not null
                && modelClrType != propertyClrType)
            {
                return;
            }

            modelClrType = propertyClrType;
        }

        operation.ClrType = modelClrType!;
    }

    private static void NormalizeProviderGuidColumnOperation(
        ColumnOperation operation
    )
    {
        if (operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value is not MySqlGuidFormat format)
        {
            return;
        }

        operation.ClrType = typeof(Guid);
        operation.DefaultValue = format switch
        {
            MySqlGuidFormat.Char36 => NormalizeTextDefault(operation, MySqlGuidFormat.Char36),
            MySqlGuidFormat.Binary16 => NormalizeBinary16Default(operation),
            MySqlGuidFormat.Uuid => NormalizeTextDefault(operation, MySqlGuidFormat.Uuid),
            _ => throw new InvalidOperationException($"Unsupported provider-owned GUID format '{format}'."),
        };
    }

    private static object? NormalizeTextDefault(
        ColumnOperation operation,
        MySqlGuidFormat format
    ) => operation.DefaultValue switch
    {
        null or Guid => operation.DefaultValue,
        "" => Guid.Empty,
        string value when Guid.TryParseExact(value, "D", out var guid) => guid,
        _ => ThrowInvalidProviderGuidDefault(operation, format),
    };

    private static object? NormalizeBinary16Default(
        ColumnOperation operation
    ) => operation.DefaultValue switch
    {
        null or Guid => operation.DefaultValue,
        byte[] { Length: 16 } value => new Guid(value, bigEndian: true),
        _ => ThrowInvalidProviderGuidDefault(operation, MySqlGuidFormat.Binary16),
    };

    private static object ThrowInvalidProviderGuidDefault(
        ColumnOperation operation,
        MySqlGuidFormat format
    ) => throw new InvalidOperationException(
        $"Provider-owned {format} column '{operation.Table}.{operation.Name}' has an invalid "
        + $"migration default value of type '{operation.DefaultValue?.GetType()}'.");

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
        && (!RequiresNullValueUpdate(left)
            || left.DefaultValue is not null
            || left.DefaultValueSql is not null
            || left.ClrType == right.ClrType)
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
}
