namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates provider query roots, JSON collections, and bulk mutation expressions
/// into relational query shapes for MySQL and MariaDB.
/// </summary>
internal sealed partial class
    MySqlQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalQueryCompilationContext _queryCompilationContext;

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

    /// <summary>
    /// Accepts MySQL's native join-based update shape and single-table
    /// <c>LIMIT</c>. Offsets and ordered multi-table updates still use EF Core's
    /// primary-key join rewrite.
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
            targetTable = join.Table;
        }

        tableExpression = targetTable as TableExpression;
        return tableExpression is not null;
    }
}
