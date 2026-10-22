namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// MySQL / MariaDB queryable-method translator. Overrides the two hook points that turn a JSON
/// source into a relational rowset (<see cref="TranslatePrimitiveCollection"/> for raw / column
/// primitive collections, <see cref="TransformJsonQueryToTable"/> for <c>ToJson()</c>-owned
/// entity collections) so the cross-engine JSON_TABLE grammar gets emitted in place of the
/// "no LINQ translation" fallback the base provider would otherwise hit.
/// </summary>
internal sealed partial class
    MySqlQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{

    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalQueryCompilationContext _queryCompilationContext;
    private bool _requiresNativeJoinedUpdate;

    public MySqlQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext
    ) : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _typeMappingSource = relationalDependencies.TypeMappingSource;
        _sqlExpressionFactory = relationalDependencies.SqlExpressionFactory;
        _queryCompilationContext = queryCompilationContext;
    }

    private MySqlQueryableMethodTranslatingExpressionVisitor(
        MySqlQueryableMethodTranslatingExpressionVisitor parentVisitor
    ) : base(parentVisitor)
    {
        _typeMappingSource = parentVisitor._typeMappingSource;
        _sqlExpressionFactory = parentVisitor._sqlExpressionFactory;
        _queryCompilationContext = parentVisitor._queryCompilationContext;
        _straightJoinOwner = parentVisitor._straightJoinOwner ?? parentVisitor;
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() =>
        new MySqlQueryableMethodTranslatingExpressionVisitor(this);

    /// <summary>
    /// Emulates a full join as a left join plus the unmatched rows from a right join. The
    /// second branch retains rows only when its original join predicate is not true, which
    /// also preserves composite-key null compensation. <c>UNION ALL</c> keeps LINQ duplicate
    /// semantics intact.
    /// </summary>
    protected override ShapedQueryExpression? TranslateFullJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector
    )
    {
        var translated = base.TranslateFullJoin(
            outer,
            inner,
            outerKeySelector,
            innerKeySelector,
            resultSelector);

        if (translated?.QueryExpression is not SelectExpression selectExpression)
        {
            return translated;
        }

        var fullJoinIndex = -1;
        for (var index = selectExpression.Tables.Count - 1; index >= 0; index--)
        {
            if (selectExpression.Tables[index] is FullJoinExpression)
            {
                fullJoinIndex = index;
                break;
            }
        }

        if (fullJoinIndex < 0)
        {
            throw new UnreachableException("A translated full join must contain a FullJoinExpression.");
        }

#pragma warning disable EF1001 // The relational SQL tree must be cloned to build equivalent set-operation branches.
        var leftBranch = selectExpression.Clone();
        var rightBranch = selectExpression.Clone();
#pragma warning restore EF1001

        var leftJoin = (FullJoinExpression)leftBranch.Tables[fullJoinIndex];
        var leftTables = leftBranch.Tables.ToArray();
        leftTables[fullJoinIndex] = new LeftJoinExpression(leftJoin.Table, leftJoin.JoinPredicate);
#pragma warning disable EF1001 // Mutable cloned translation state can replace tables only through SelectExpression.SetTables.
        leftBranch.SetTables(leftTables);
#pragma warning restore EF1001

        var rightJoin = (FullJoinExpression)rightBranch.Tables[fullJoinIndex];
        var rightTables = rightBranch.Tables.ToArray();
        rightTables[fullJoinIndex] = new RightJoinExpression(rightJoin.Table, rightJoin.JoinPredicate);
#pragma warning disable EF1001 // Mutable cloned translation state can replace tables only through SelectExpression.SetTables.
        rightBranch.SetTables(rightTables);
#pragma warning restore EF1001

        rightBranch.ApplyPredicate(
            _sqlExpressionFactory.Function(
                MySqlSentinelContract.GetName(MySqlSentinelKind.IsNotTrue),
                [rightJoin.JoinPredicate],
                nullable: false,
                argumentsPropagateNullability: [false],
                typeof(bool),
                rightJoin.JoinPredicate.TypeMapping));

        leftBranch.ApplyUnion(rightBranch, distinct: false);

#pragma warning disable EF1001 // Projection bindings must follow the cloned SelectExpression that owns the set operation.
        var shaper = new Microsoft.EntityFrameworkCore.Query.Internal.QueryExpressionReplacingExpressionVisitor(
                selectExpression,
                leftBranch)
            .Visit(translated.ShaperExpression);
#pragma warning restore EF1001

        return translated
            .UpdateQueryExpression(leftBranch)
            .UpdateShaperExpression(shaper);
    }

    /// <summary>
    /// Converts provider query roots into ordinary relational selects whose table expressions
    /// carry the engine-specific operation. Deferring syntax to SQL generation keeps query-root
    /// translation independent of the configured server family.
    /// </summary>
    protected override Expression VisitExtension(
        Expression extensionExpression
    )
    {
        if (extensionExpression is IMySqlMaterializedQueryRoot materializedSource)
        {
            return TranslateMaterializedSource(materializedSource);
        }

#pragma warning disable EF1001 // The provider must translate EF Core's relational FromSql query root.
        if (extensionExpression is Microsoft.EntityFrameworkCore.Query.Internal.FromSqlQueryRootExpression fromSqlQueryRoot
            && HasJsonComplexProperties(fromSqlQueryRoot.EntityType))
        {
            return TranslateFromSqlWithJsonComplexProperties(fromSqlQueryRoot);
        }
#pragma warning restore EF1001

        if (extensionExpression is MySqlApplicationTimeQueryRootExpression applicationTimeQueryRoot)
        {
            return TranslateApplicationTimeQueryRoot(applicationTimeQueryRoot);
        }

        if (extensionExpression is not MySqlTemporalQueryRootExpression temporalQueryRoot)
        {
            return base.VisitExtension(extensionExpression);
        }

        var entityType = temporalQueryRoot.EntityType;
        var hierarchyIsTemporal = entityType
            .GetRootType()
            .GetDerivedTypesInclusive()
            .Any(candidate => candidate.IsMySqlTemporal());

        if (!hierarchyIsTemporal)
        {
            throw new InvalidOperationException(
                $"Temporal query operation '{temporalQueryRoot.Operation}' cannot be applied "
                + $"to non-temporal entity type '{entityType.DisplayName()}'.");
        }

        var selectExpression = CreateSelect(entityType);
        var annotationVisitor = new TemporalAnnotationApplyingExpressionVisitor(
            temporalQueryRoot,
            _queryCompilationContext.Model.GetRelationalModel());

        selectExpression = (SelectExpression)annotationVisitor.Visit(selectExpression);

        if (annotationVisitor.AnnotatedTableCount == 0)
        {
            throw new InvalidOperationException(
                $"Temporal entity type '{entityType.DisplayName()}' has no physical temporal-table mapping.");
        }

        return new ShapedQueryExpression(
            selectExpression,
            new RelationalStructuralTypeShaperExpression(
                entityType,
                new ProjectionBindingExpression(selectExpression, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

#pragma warning disable EF1001 // The provider must translate EF Core's relational FromSql query root.
    private ShapedQueryExpression TranslateFromSqlWithJsonComplexProperties(
        Microsoft.EntityFrameworkCore.Query.Internal.FromSqlQueryRootExpression queryRoot
    )
    {
        var selectExpression = CreateSelect(queryRoot.EntityType);

        if (selectExpression.Tables is not [TableExpression tableExpression])
        {
            throw new InvalidOperationException(
                $"FromSql over JSON complex properties requires one physical table for "
                + $"entity type '{queryRoot.EntityType.DisplayName()}'.");
        }

        var fromSqlExpression = new FromSqlExpression(
            tableExpression.Alias!,
            tableExpression.Table,
            queryRoot.Sql,
            queryRoot.Argument);

#pragma warning disable EF1001 // Mutable translation state can replace the physical table only through SelectExpression.SetTables.
        selectExpression.SetTables([fromSqlExpression]);
#pragma warning restore EF1001

        return new ShapedQueryExpression(
            selectExpression,
            new RelationalStructuralTypeShaperExpression(
                queryRoot.EntityType,
                new ProjectionBindingExpression(selectExpression, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }
#pragma warning restore EF1001

    private static bool HasJsonComplexProperties(
        IEntityType entityType
    ) => entityType
        .GetAllBaseTypes()
        .Concat(entityType.GetDerivedTypesInclusive())
        .SelectMany(candidate => candidate.GetDeclaredComplexProperties())
        .Any(complexProperty => complexProperty.ComplexType.IsMappedToJson());

    /// <summary>
    /// Keeps join-based query shapes as native multi-table deletes. Falling back to
    /// EF Core's key-subquery rewrite would make MySQL read the target table from a
    /// nested query and trigger error 1093 even though the native delete grammar can
    /// express the operation directly.
    /// </summary>
    protected override bool IsValidSelectExpressionForExecuteDelete(
        SelectExpression selectExpression
    ) => selectExpression.Offset is null
        && selectExpression.GroupBy.Count == 0
        && selectExpression.Having is null
        && (selectExpression.Tables.Count == 1
            || (selectExpression.Orderings.Count == 0 && selectExpression.Limit is null));

    protected override UpdateExpression TranslateExecuteUpdate(
        ShapedQueryExpression source,
        IReadOnlyList<ExecuteUpdateSetter> setters
    )
    {
        var previousRequiresNativeJoinedUpdate = _requiresNativeJoinedUpdate;
        _requiresNativeJoinedUpdate = setters.Any(RequiresNativeJoinedUpdate);

        try
        {
            return base.TranslateExecuteUpdate(source, setters);
        }
        finally
        {
            _requiresNativeJoinedUpdate = previousRequiresNativeJoinedUpdate;
        }
    }

    /// <summary>
    /// Keeps entity-splitting targets and indexed primitive-collection setters
    /// on MySQL's native join shape. Other join targets use EF Core's primary-key
    /// rewrite so projection pruning cannot remove navigation predicates.
    /// </summary>
    protected override bool IsValidSelectExpressionForExecuteUpdate(
        SelectExpression selectExpression,
        TableExpressionBase targetTable,
        [NotNullWhen(true)] out TableExpression? tableExpression
    )
    {
        tableExpression = null;

        if (selectExpression.Offset is not null
            || selectExpression.IsDistinct
            || selectExpression.GroupBy.Count > 0
            || selectExpression.Having is not null
            || selectExpression.Orderings.Count > 0
            || selectExpression.Tables.Count == 0
            || (selectExpression.Tables.Count > 1 && selectExpression.Limit is not null))
        {
            return false;
        }

        if (targetTable is JoinExpressionBase join)
        {
            if (!_requiresNativeJoinedUpdate)
            {
                return false;
            }

            targetTable = join.Table;
        }

        tableExpression = targetTable as TableExpression;
        return tableExpression is not null;
    }

    private bool RequiresNativeJoinedUpdate(
        ExecuteUpdateSetter setter
    )
    {
        if (ContainsIndexAccess(setter.PropertySelector.Body))
        {
            return true;
        }

        var entityClrType = setter.PropertySelector.Parameters[0].Type;

        return RelationalDependencies.Model
            .GetEntityTypes()
            .Where(entityType => entityType.ClrType == entityClrType)
            .Any(entityType => entityType.GetMappingFragments(StoreObjectType.Table).Any());
    }

    private static bool ContainsIndexAccess(
        Expression expression
    ) => expression switch
    {
        IndexExpression => true,
        MethodCallExpression { Method.Name: "get_Item" or nameof(Enumerable.ElementAt), } => true,
        MemberExpression { Expression: { } instance, } => ContainsIndexAccess(instance),
        UnaryExpression unary => ContainsIndexAccess(unary.Operand),
        _ => false,
    };
}
