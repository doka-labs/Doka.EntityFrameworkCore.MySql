namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Preserves temporal query roots while EF Core expands navigations and set operations.
/// </summary>
/// <remarks>
/// EF Core creates fresh query roots for expanded navigations. The provider must carry
/// compatible temporal semantics onto those roots or reject the shape before SQL is
/// generated; silently falling back to current rows would return historically invalid data.
/// </remarks>
internal sealed class MySqlNavigationExpansionExtensibilityHelper : NavigationExpansionExtensibilityHelper
{
    public MySqlNavigationExpansionExtensibilityHelper(
        NavigationExpansionExtensibilityHelperDependencies dependencies
    ) : base(dependencies) { }

    public override EntityQueryRootExpression CreateQueryRoot(
        IEntityType entityType,
        EntityQueryRootExpression? source
    )
    {
        if (source is MySqlTemporalQueryRootExpression { Operation: MySqlTemporalQueryOperation.AsOf, } temporalRoot)
        {
            // AsOf describes one consistent database instant and is therefore the only
            // temporal operation that can be propagated across separate table roots.
            return source.QueryProvider is not null
                ? new MySqlTemporalQueryRootExpression(
                    source.QueryProvider,
                    entityType,
                    temporalRoot.Operation,
                    temporalRoot.PointInTime)
                : new MySqlTemporalQueryRootExpression(entityType, temporalRoot.Operation, temporalRoot.PointInTime);
        }

        return base.CreateQueryRoot(entityType, source);
    }

    public override void ValidateQueryRootCreation(
        IEntityType entityType,
        EntityQueryRootExpression? source
    )
    {
        if (source is MySqlTemporalQueryRootExpression temporalRoot
            && !entityType.IsMappedToJson()
            && !OwnedEntityMappedToSameTableAsOwner(entityType))
        {
            if (!entityType
                    .GetRootType()
                    .IsMySqlTemporal())
            {
                throw new InvalidOperationException(
                    $"Temporal navigation expansion reached non-temporal entity "
                    + $"'{entityType.DisplayName()}'. Map every separately stored target "
                    + "as temporal or project it outside the temporal query.");
            }

            if (temporalRoot.Operation != MySqlTemporalQueryOperation.AsOf)
            {
                throw new InvalidOperationException(
                    "Navigation expansion across separate temporal tables is supported "
                    + "only for TemporalAsOf because it is the only operation that "
                    + "defines one consistent database instant.");
            }
        }

        base.ValidateQueryRootCreation(entityType, source);
    }

    public override bool AreQueryRootsCompatible(
        EntityQueryRootExpression? first,
        EntityQueryRootExpression? second
    )
    {
        if (!base.AreQueryRootsCompatible(first, second))
        {
            return false;
        }

        if (first is MySqlTemporalQueryRootExpression firstTemporal
            && second is MySqlTemporalQueryRootExpression secondTemporal
            && HaveEquivalentTemporalSemantics(firstTemporal, secondTemporal))
        {
            return true;
        }

        if (first is MySqlTemporalQueryRootExpression
            || second is MySqlTemporalQueryRootExpression)
        {
            var entityType = first?.EntityType ?? second?.EntityType;

            throw new InvalidOperationException(
                $"Temporal set operations for entity type '{entityType!.DisplayName()}' "
                + "require matching temporal operators and identical UTC boundaries.");
        }

        return true;
    }

    private static bool HaveEquivalentTemporalSemantics(
        MySqlTemporalQueryRootExpression first,
        MySqlTemporalQueryRootExpression second
    ) => first.Operation == second.Operation
        && first.PointInTime == second.PointInTime
        && first.From == second.From
        && first.To == second.To;

    private static bool OwnedEntityMappedToSameTableAsOwner(
        IEntityType entityType
    ) => entityType.IsOwned()
        && entityType.FindOwnership()!
            .PrincipalEntityType.GetTableMappings()
            .FirstOrDefault()
            ?.Table is { } ownerTable
        && entityType
            .GetTableMappings()
            .FirstOrDefault()
            ?.Table is { } ownedTable
        && ownerTable == ownedTable;
}
