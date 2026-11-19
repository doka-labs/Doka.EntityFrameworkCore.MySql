namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>Qualifies native ordinary-CTE writes independently from read syntax or a writable alias.</summary>
    private void ValidateCteMutation(
        Expression queryExpression,
        MaterializedSourceInventory inventory
    )
    {
        if (inventory.Definitions.Any(definition => definition.Kind == MySqlSourceBindingKind.Materialized))
        {
            throw new InvalidOperationException("Materialized sources compose read queries only.");
        }

        // WHY: The SELECT reader's native-limit control cannot protect a nonquery that has already
        // written rows. Recursive writes need their own atomic, fail-closed qualification first.
        if (inventory.Definitions.Any(definition => definition.Kind == MySqlSourceBindingKind.Recursive))
        {
            throw new InvalidOperationException(
                "Recursive CTE writes require a separately qualified native-limit contract.");
        }

        var target = queryExpression switch
        {
            UpdateExpression update => update.Table,
            DeleteExpression delete => delete.Table,
            _ => throw new InvalidOperationException("This statement does not support ordinary CTE sources."),
        };

        if (!Profile.Engine.Has(EngineCapability.CteUpdateDelete))
        {
            throw new InvalidOperationException(
                "The configured engine version does not support CTE UPDATE or DELETE statements.");
        }

        if (Profile.GetSupport(ProviderCapability.SelfReferencingMutations) == ProviderSupportStatus.Emulated)
        {
            var finder = new TargetTableFindingExpressionVisitor(target);

            foreach (var definition in inventory.Definitions)
            {
                finder.Visit(definition.Source);
            }

            if (finder.Found)
            {
                throw new InvalidOperationException(
                    "Self-referencing CTE writes require an independently qualified target-isolation shape.");
            }
        }
    }

    /// <summary>Emits the complete ordinary relation without adding materialization or duplicate-removal semantics.</summary>
    private void GenerateOrdinaryCteSource(
        SelectExpression source,
        string[] columns
    )
    {
        var alias = source.Alias
            ?? throw new InvalidOperationException("An ordinary CTE definition requires its own source alias scope.");

        Sql.Append("SELECT ");

        if (source.Projection.Count == 0)
        {
            Sql
                .Append("1 AS ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columns[0]));
        }
        else
        {
            // WHY: The completed SELECT retains its own ORDER BY/LIMIT and projection scope.
            // Enumerating its actual columns avoids a wildcard contract while preserving EF's aliases.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < columns.Length; index++)
            {
                if (index != 0)
                {
                    Sql.Append(", ");
                }

                Sql
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(alias))
                    .Append(".")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columns[index]));
            }
        }

        Sql.AppendLine();
        Sql.Append("FROM ");
        Visit(source);
        Sql.AppendLine();
    }
}
