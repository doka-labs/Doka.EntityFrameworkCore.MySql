namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Applies MySQL-family relational rewrites after projection pruning and before
/// EF Core finalizes SQL aliases.
/// </summary>
internal sealed class MySqlQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
{
    private readonly MySqlSingletonOptions _singletonOptions;

    public MySqlQueryTranslationPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext,
        MySqlSingletonOptions singletonOptions
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _singletonOptions = singletonOptions ?? throw new ArgumentNullException(nameof(singletonOptions));
    }

    /// <inheritdoc />
    public override Expression Process(
        Expression query
    )
    {
        var processed = base.Process(query);

        MySqlCollectionIdentityValidatingExpressionVisitor.Validate(processed);

        return processed;
    }

    /// <summary>
    /// Normalizes only unambiguous fallback-versus-model string mappings before EF Core
    /// applies its standard relational type-inference and validation pass.
    /// </summary>
    protected override Expression ProcessTypeMappings(
        Expression expression
    )
    {
        var normalized = MySqlValuesTypeMappingNormalizingExpressionVisitor.Normalize(
            expression,
            RelationalDependencies.TypeMappingSource);

        return new MySqlTypeMappingPostprocessor(
                Dependencies,
                RelationalDependencies,
                RelationalQueryCompilationContext)
            .Process(normalized);
    }

    /// <summary>
    /// Flattens semantics-preserving APPLY shapes on both engines. MySQL then
    /// receives its additional projection decorrelation pass for the LATERAL
    /// shapes that cannot be flattened.
    /// </summary>
    protected override Expression Prune(
        Expression query
    )
    {
        var pruned = base.Prune(query);
        var dynamicOffsets = new MySqlDynamicOffsetRewritingExpressionVisitor(
                RelationalDependencies.SqlExpressionFactory,
                RelationalDependencies.TypeMappingSource,
                RelationalQueryCompilationContext.SqlAliasManager)
            .Visit(pruned);

        var profile = _singletonOptions.Profile
            ?? throw new InvalidOperationException(
                "The provider profile must be initialized before query postprocessing.");

        var supportsLateralDerivedTables = profile.Supports(ProviderCapability.LateralDerivedTables);

        var flattened = new MySqlApplyRewritingExpressionVisitor(
                RelationalDependencies.SqlExpressionFactory,
                flattenJsonTablesOnly: supportsLateralDerivedTables)
            .Visit(dynamicOffsets);

        return supportsLateralDerivedTables
            ? new MySqlLateralProjectionDecorrelationExpressionVisitor().Visit(flattened)
            : flattened;
    }

    /// <summary>
    /// Preserves EF Core's collection-identity safety check when <c>Concat</c>
    /// branches can return the same physical row more than once.
    /// </summary>
    private sealed class
        MySqlCollectionIdentityValidatingExpressionVisitor : MySqlShapedQueryTraversingExpressionVisitor
    {
        public static void Validate(
            Expression query
        ) => new MySqlCollectionIdentityValidatingExpressionVisitor().Visit(query);

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not ShapedQueryExpression shapedQueryExpression)
            {
                return base.VisitExtension(node);
            }

            if (shapedQueryExpression.QueryExpression is SelectExpression selectExpression
                && CollectionShaperFindingExpressionVisitor.ContainsCollection(shapedQueryExpression.ShaperExpression)
                && selectExpression.Tables.Any(ContainsUnsafeApply))
            {
                throw new InvalidOperationException(
                    RelationalStrings.InsufficientInformationToIdentifyElementOfCollectionJoin);
            }

            return base.VisitExtension(node);
        }

        private static bool ContainsUnsafeApply(
            TableExpressionBase table
        )
        {
            var applySelect = MySqlTableExpressionHelper.GetApplySelect(table);

            return applySelect?.Tables.Any(ContainsUnsafeUnionAll) == true;
        }

        private static bool ContainsUnsafeUnionAll(
            TableExpressionBase table
        )
        {
            while (table is JoinExpressionBase joinExpression)
            {
                table = joinExpression.Table;
            }

            return table switch
            {
                UnionExpression { IsDistinct: false } unionExpression => BranchesSharePhysicalTable(unionExpression),
                SelectExpression selectExpression => selectExpression.Tables.Any(ContainsUnsafeUnionAll),
                _ => false,
            };
        }

        /// <summary>
        /// Distinguishes a potentially overlapping <c>Concat</c> from EF Core's TPC
        /// union. TPC branches read different physical tables; a same-table
        /// <c>Concat</c> can emit one row twice while the collection shaper retains
        /// only the row key as its identity.
        /// </summary>
        private static bool BranchesSharePhysicalTable(
            UnionExpression unionExpression
        )
        {
            var firstBranchTables = new HashSet<(string? Schema, string Name)>();
            AddPhysicalTables(unionExpression.Source1, firstBranchTables);

            return ContainsPhysicalTable(unionExpression.Source2, firstBranchTables);
        }

        private static void AddPhysicalTables(
            TableExpressionBase table,
            ISet<(string? Schema, string Name)> physicalTables
        )
        {
            while (table is JoinExpressionBase joinExpression)
            {
                table = joinExpression.Table;
            }

            switch (table)
            {
                case TableExpression tableExpression:
                    physicalTables.Add((tableExpression.Schema, tableExpression.Name));
                    break;

                case SelectExpression selectExpression:
                    foreach (var nestedTable in selectExpression.Tables)
                    {
                        AddPhysicalTables(nestedTable, physicalTables);
                    }

                    break;

                case SetOperationBase setOperation:
                    AddPhysicalTables(setOperation.Source1, physicalTables);
                    AddPhysicalTables(setOperation.Source2, physicalTables);
                    break;
            }
        }

        private static bool ContainsPhysicalTable(
            TableExpressionBase table,
            ISet<(string? Schema, string Name)> physicalTables
        )
        {
            while (table is JoinExpressionBase joinExpression)
            {
                table = joinExpression.Table;
            }

            return table switch
            {
                TableExpression tableExpression =>
                    physicalTables.Contains((tableExpression.Schema, tableExpression.Name)),
                SelectExpression selectExpression => selectExpression.Tables.Any(nestedTable =>
                    ContainsPhysicalTable(nestedTable, physicalTables)),
                SetOperationBase setOperation => ContainsPhysicalTable(setOperation.Source1, physicalTables)
                    || ContainsPhysicalTable(setOperation.Source2, physicalTables),
                _ => false,
            };
        }

        private sealed class CollectionShaperFindingExpressionVisitor : ExpressionVisitor
        {
            public bool Found { get; private set; }

            public static bool ContainsCollection(
                Expression shaper
            )
            {
                var visitor = new CollectionShaperFindingExpressionVisitor();
                visitor.Visit(shaper);

                return visitor.Found;
            }

            protected override Expression VisitExtension(
                Expression node
            )
            {
                if (node is RelationalCollectionShaperExpression or RelationalSplitCollectionShaperExpression)
                {
                    Found = true;
                    return node;
                }

                return base.VisitExtension(node);
            }
        }
    }
}
