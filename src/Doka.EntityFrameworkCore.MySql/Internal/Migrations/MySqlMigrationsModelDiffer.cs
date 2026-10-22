namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Decorates EF Core model differencing with provider annotations and engine-safe operation ordering.
/// </summary>
internal sealed partial class MySqlMigrationsModelDiffer : IMigrationsModelDiffer
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
        if (target is null)
        {
            return _innerDiffer.HasDifferences(source, target);
        }

        return _innerDiffer.HasDifferences(source, target)
            || !string.Equals(
                source is null ? null : GetDatabaseCharSet(source),
                GetDatabaseCharSet(target),
                StringComparison.Ordinal)
            || !string.Equals(
                source is null ? null : GetDatabaseCollation(source),
                GetDatabaseCollation(target),
                StringComparison.Ordinal)
            || (source is not null && HasProviderIndexMetadataDifferences(source, target));
    }

    public IReadOnlyList<MigrationOperation> GetDifferences(
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        var operations = _innerDiffer
            .GetDifferences(source, target)
            .ToList();

        RemoveEfCoreSyntheticBackfillDefaults(operations, target);
        NormalizeProviderColumnOperations(operations, source, target);
        ApplyTemporalOperationAnnotations(operations, source, target);
        ApplyApplicationTimeOperationAnnotations(operations, source, target);
        EnsureApplicationTimePrimaryKeyTransitions(operations, source, target);

        // PK synthesis adds operations after the first annotation pass; the
        // second pass gives those new operations their period contracts.
        ApplyApplicationTimeOperationAnnotations(operations, source, target);
        SplitApplicationTimePeriodChanges(operations);
        MarkTemporalDeactivationsDestructive(operations);
        OrderTemporalTransitionOperations(operations);
        OrderApplicationTimeTransitionOperations(operations);

        if (target is null)
        {
            return operations;
        }

        ApplyDatabaseOptions(operations, source, target);
        ApplyInheritedTableDefaults(operations, target);
        EnsureProviderIndexMetadataTransitions(operations, source, target);
        ApplyIndexAnnotations(operations, target);
        RemoveDuplicateAlterColumnOperations(operations);
        EnsureForeignKeysAroundStoreTypeChanges(operations, source, target);
        NormalizeAutoIncrementPrimaryKeyOperations(operations, source, target);

        return operations;
    }
}
