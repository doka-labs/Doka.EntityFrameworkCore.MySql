namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Qualifies the completed direct SELECT and attaches an immutable execution policy to its copy.</summary>
internal static class MySqlLockingReadQueryValidator
{
    /// <summary>Preserves ordinary queries and rejects locking shapes outside the qualified final query block.</summary>
    public static Expression Validate(
        Expression query,
        MySqlQueryCompilationContext compilation
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(compilation);

        var mode = compilation.LockingReadMode;

        if (mode == MySqlLockingReadMode.None)
        {
            return query;
        }

        ValidateVersion(compilation, mode);

        if (compilation.QuerySplittingBehavior == QuerySplittingBehavior.SplitQuery
            || query is not ShapedQueryExpression
            {
                QueryExpression: SelectExpression select,
            } shaped)
        {
            throw new InvalidOperationException(
                "Locking reads require one direct SELECT without split queries, sets, grouping or distinct.");
        }

        new ShaperValidator().Visit(shaped.ShaperExpression);

        return shaped.UpdateQueryExpression(ValidateProcessedSelect(select, mode));
    }

    /// <summary>Revalidates the final scope after parameter rewrites and copies the policy onto its supported carrier.</summary>
    internal static SelectExpression ValidateProcessedSelect(
        SelectExpression select,
        MySqlLockingReadMode mode
    )
    {
        if (select.Alias is not null
            || select.IsDistinct
            || select.GroupBy.Count != 0
            || select.Having is not null
            || select.Tables.Count == 0)
        {
            throw new InvalidOperationException(
                "Locking reads require a direct SELECT without sets, grouping or distinct after query processing.");
        }

        foreach (var table in select.Tables)
        {
            ValidateTable(table);
        }

        var projections = new RootProjectionValidator();

        foreach (var projection in select.Projection)
        {
            projections.Visit(projection.Expression);
        }

        // WHY: EF11 Select.WithAnnotations deliberately throws. A copied direct table is
        // only the supported metadata carrier; the policy still governs the entire root block.
        return MySqlLockingReadMetadata.ApplyMode(select, mode);
    }

    /// <summary>Rejects syntax not supplied by an explicitly allowed older engine.</summary>
    private static void ValidateVersion(
        MySqlQueryCompilationContext compilation,
        MySqlLockingReadMode mode
    )
    {
        var engine = compilation.ContextOptions.FindExtension<MySqlOptionsExtension>()
                ?.ServerVersion?.Profile.Engine
            ?? throw new InvalidOperationException("A locking read requires a configured MySQL-family server version.");

        // WHY: The same named syntax facts drive validation and emission. AllowUnsupported
        // changes support policy, never the grammar available on the configured server.
        var supported = mode switch
        {
            MySqlLockingReadMode.Update => true,
            MySqlLockingReadMode.Share => engine.Has(EngineCapability.ForShareClause)
                || engine.Has(EngineCapability.SharedLockingReadUsesLockInShareMode),
            MySqlLockingReadMode.NoWait => engine.Has(EngineCapability.ForUpdateNoWaitClause),
            MySqlLockingReadMode.SkipLocked => engine.Has(EngineCapability.ForUpdateSkipLockedClause),
            _ => false,
        };

        if (!supported)
        {
            throw new InvalidOperationException(
                "The configured server version does not support this locking read mode.");
        }
    }

    /// <summary>Checks only directly locked sources, leaving scalar subqueries outside this lock scope.</summary>
    private static void ValidateTable(
        TableExpressionBase source
    )
    {
        while (source is InnerJoinExpression or LeftJoinExpression or CrossJoinExpression)
        {
            source = ((JoinExpressionBase)source).Table;
        }

        if (source is not TableExpression { Table: ITable table }
            || table.PrimaryKey is null
            || source.FindAnnotation(MySqlAnnotationNames.TemporalOperation) is not null
            || source.FindAnnotation(MySqlAnnotationNames.ApplicationTimeOperation) is not null
            || table.FindAnnotation(MySqlAnnotationNames.IsTemporal)
                ?.Value is true)
        {
            throw new InvalidOperationException(
                "Locking reads require direct keyed physical tables; derived, temporal, raw SQL and bound sources are unsupported.");
        }

        ValidateStorageEngine(table.FindAnnotation(MySqlAnnotationNames.StorageEngine)
            ?.Value as string);

        foreach (var mapping in table.EntityTypeMappings)
        {
            ValidateStorageEngine(mapping.TypeBase.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string);
        }
    }

    /// <summary>Preserves the explicit InnoDB contract without querying catalogs for unknown physical state.</summary>
    private static void ValidateStorageEngine(
        string? engine
    )
    {
        if (engine is not null
            && !engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Locking reads require InnoDB tables; the model specifies another storage engine.");
        }
    }

    /// <summary>Rejects built-in row aggregates while preserving aggregates in independent scalar subqueries.</summary>
    private sealed class RootProjectionValidator : ExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        )
        {
            // WHY: A scalar subquery is a separate SQL scope. Its COUNT must not turn
            // a direct outer locking read into an unsupported aggregate query.
            if (node is SelectExpression)
            {
                return node;
            }

            if (node is SqlFunctionExpression { IsBuiltIn: true } function
                && (function.Name.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
                    || function.Name.Equals("SUM", StringComparison.OrdinalIgnoreCase)
                    || function.Name.Equals("AVG", StringComparison.OrdinalIgnoreCase)
                    || function.Name.Equals("MIN", StringComparison.OrdinalIgnoreCase)
                    || function.Name.Equals("MAX", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Locking reads do not support aggregates in the executed SELECT block.");
            }

            return base.VisitExtension(node);
        }
    }

    /// <summary>Rejects multirow include shapers without rejecting reference navigation shaping.</summary>
    private sealed class ShaperValidator : ExpressionVisitor
    {
        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is RelationalCollectionShaperExpression
                or RelationalSplitCollectionShaperExpression
                or IncludeExpression { Navigation.IsCollection: true })
            {
                throw new InvalidOperationException(
                    "Locking reads do not support collection Includes or split queries.");
            }

            if (node is StructuralTypeShaperExpression { StructuralType: IEntityType entityType }
                && (entityType.FindPrimaryKey() is null || entityType.GetViewName() is not null))
            {
                throw new InvalidOperationException("Locking reads do not support view or keyless entity sources.");
            }

            return base.VisitExtension(node);
        }
    }
}
