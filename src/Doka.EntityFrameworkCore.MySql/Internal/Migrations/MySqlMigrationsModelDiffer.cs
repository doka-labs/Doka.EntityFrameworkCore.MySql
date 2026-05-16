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
        ApplySpatialIndexAnnotations(operations, target);

        return operations;
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

    private static void ApplySpatialIndexAnnotations(
        List<MigrationOperation> operations,
        IRelationalModel target
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(target);

        // Pre-build lookup to avoid O(N x M) entity-type enumeration per index operation.
        var spatialIndexLookup = new HashSet<(string? Schema, string Table, string IndexName)>();

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

                if (indexName is not null
                    && index.GetMySqlSpatialIndex())
                {
                    spatialIndexLookup.Add((schema, tableName, indexName));
                }
            }
        }

        foreach (var table in target.Tables)
        {
            foreach (var tableIndex in table.Indexes)
            {
                if ((tableIndex.FindAnnotation(MySqlAnnotationNames.SpatialIndex)
                        ?.Value as bool?)
                    == true)
                {
                    spatialIndexLookup.Add((table.Schema, table.Name, tableIndex.Name));
                }
            }
        }

        foreach (var createIndexOperation in operations.OfType<CreateIndexOperation>())
        {
            if (spatialIndexLookup.Contains(
                    (createIndexOperation.Schema, createIndexOperation.Table, createIndexOperation.Name)))
            {
                createIndexOperation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
            }
        }
    }
}
