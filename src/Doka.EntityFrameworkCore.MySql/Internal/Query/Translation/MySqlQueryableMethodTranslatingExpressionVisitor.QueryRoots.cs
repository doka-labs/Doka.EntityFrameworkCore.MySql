namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Applies temporal and application-time metadata to physical query sources.</summary>
internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    private ShapedQueryExpression TranslateApplicationTimeQueryRoot(
        MySqlApplicationTimeQueryRootExpression queryRoot
    )
    {
        var entityType = queryRoot.EntityType;
        var hierarchyIsApplicationTime = entityType
            .GetRootType()
            .GetDerivedTypesInclusive()
            .Any(candidate => candidate.IsMySqlApplicationTime());

        if (!hierarchyIsApplicationTime)
        {
            throw new InvalidOperationException(
                $"FOR PORTION OF cannot be applied to non-application-time entity type "
                + $"'{entityType.DisplayName()}'.");
        }

        var selectExpression = CreateSelect(entityType);
        var annotationVisitor = new ApplicationTimeAnnotationApplyingExpressionVisitor(
            queryRoot,
            _queryCompilationContext.Model.GetRelationalModel());

        selectExpression = (SelectExpression)annotationVisitor.Visit(selectExpression);

        if (annotationVisitor.AnnotatedTableCount == 0)
        {
            throw new InvalidOperationException(
                $"Application-time entity type '{entityType.DisplayName()}' has no physical "
                + "application-time table mapping that can be mutated.");
        }

        return new ShapedQueryExpression(
            selectExpression,
            new RelationalStructuralTypeShaperExpression(
                entityType,
                new ProjectionBindingExpression(selectExpression, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

    /// <summary>
    /// Marks physical application-time sources with the period and requested range. The SQL
    /// generator later accepts this marker only while producing ExecuteUpdate or ExecuteDelete;
    /// ordinary enumeration is deliberately rejected because FOR PORTION OF is DML-only.
    /// </summary>
    private sealed class ApplicationTimeAnnotationApplyingExpressionVisitor : ExpressionVisitor
    {
        private readonly MySqlApplicationTimeQueryRootExpression _queryRoot;
        private readonly IRelationalModel _relationalModel;

        public ApplicationTimeAnnotationApplyingExpressionVisitor(
            MySqlApplicationTimeQueryRootExpression queryRoot,
            IRelationalModel relationalModel
        )
        {
            _queryRoot = queryRoot;
            _relationalModel = relationalModel;
        }

        public int AnnotatedTableCount { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (node is not TableExpression tableExpression)
            {
                return base.VisitExtension(node);
            }

            var table = _relationalModel.FindTable(tableExpression.Name, tableExpression.Schema);

            if (table is null
                || MySqlApplicationTimeMetadata.FindTableMetadata(table) is not { } metadata)
            {
                return tableExpression;
            }

            AnnotatedTableCount++;

            return tableExpression
                .AddAnnotation(MySqlAnnotationNames.ApplicationTimeOperation, true)
                .AddAnnotation(MySqlAnnotationNames.ApplicationTimePeriodName, metadata.PeriodName)
                .AddAnnotation(
                    MySqlAnnotationNames.ApplicationTimePeriodStartColumn,
                    metadata.PeriodStartColumn)
                .AddAnnotation(
                    MySqlAnnotationNames.ApplicationTimePeriodEndColumn,
                    metadata.PeriodEndColumn)
                .AddAnnotation(MySqlAnnotationNames.ApplicationTimeRangeStart, _queryRoot.From)
                .AddAnnotation(MySqlAnnotationNames.ApplicationTimeRangeEnd, _queryRoot.To);
        }
    }

    /// <summary>
    /// Applies temporal metadata to every physical table in the generated select. This
    /// mirrors EF Core's provider contract for table-sharing and inheritance query shapes.
    /// </summary>
    private sealed class TemporalAnnotationApplyingExpressionVisitor : ExpressionVisitor
    {
        private const string TpcTablesExpressionTypeName =
            "Microsoft.EntityFrameworkCore.Query.Internal.TpcTablesExpression, "
            + "Microsoft.EntityFrameworkCore.Relational";

        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicProperties)]
        private static readonly Type s_tpcTablesExpressionType =
            Type.GetType(TpcTablesExpressionTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"EF Core no longer exposes the expected '{TpcTablesExpressionTypeName}' query shape.");

        private static readonly ConstructorInfo s_tpcTablesExpressionConstructor =
            s_tpcTablesExpressionType.GetConstructor(
                [
                    typeof(string),
                    typeof(IEntityType),
                    typeof(IReadOnlyList<SelectExpression>),
                    typeof(ColumnExpression),
                    typeof(List<string>)
                ])
            ?? throw new InvalidOperationException(
                $"EF Core changed the constructor contract for '{TpcTablesExpressionTypeName}'.");

        private static readonly PropertyInfo s_tpcSelectExpressionsProperty =
            GetRequiredTpcProperty("SelectExpressions");

        private static readonly PropertyInfo s_tpcEntityTypeProperty =
            GetRequiredTpcProperty("EntityType");

        private static readonly PropertyInfo s_tpcDiscriminatorColumnProperty =
            GetRequiredTpcProperty("DiscriminatorColumn");

        private static readonly PropertyInfo s_tpcDiscriminatorValuesProperty =
            GetRequiredTpcProperty("DiscriminatorValues");

        private readonly MySqlTemporalQueryRootExpression _queryRoot;
        private readonly IRelationalModel _relationalModel;

        public TemporalAnnotationApplyingExpressionVisitor(
            MySqlTemporalQueryRootExpression queryRoot,
            IRelationalModel relationalModel
        )
        {
            _queryRoot = queryRoot;
            _relationalModel = relationalModel;
        }

        public int AnnotatedTableCount { get; private set; }

        protected override Expression VisitExtension(
            Expression node
        )
        {
            if (s_tpcTablesExpressionType.IsInstanceOfType(node))
            {
                return VisitTpcTablesExpression((TableExpressionBase)node);
            }

            if (node is not TableExpression tableExpression)
            {
                return base.VisitExtension(node);
            }

            var table = _relationalModel.FindTable(tableExpression.Name, tableExpression.Schema);

            if (table is null
                || MySqlTemporalMetadata.FindTableMetadata(table) is not { } temporalMetadata)
            {
                return tableExpression;
            }

            AnnotatedTableCount++;

            var annotatedTable = tableExpression
                .AddAnnotation(MySqlAnnotationNames.TemporalOperation, _queryRoot.Operation)
                .AddAnnotation(
                    MySqlAnnotationNames.TemporalPeriodStartColumn,
                    temporalMetadata.PeriodStartColumn)
                .AddAnnotation(
                    MySqlAnnotationNames.TemporalPeriodEndColumn,
                    temporalMetadata.PeriodEndColumn);

            if (temporalMetadata.HistoryTable is not null)
            {
                annotatedTable = annotatedTable.AddAnnotation(
                    MySqlAnnotationNames.TemporalHistoryTable,
                    temporalMetadata.HistoryTable);
            }

            if (temporalMetadata.HistorySchema is not null)
            {
                annotatedTable = annotatedTable.AddAnnotation(
                    MySqlAnnotationNames.TemporalHistorySchema,
                    temporalMetadata.HistorySchema);
            }

            if (_queryRoot.PointInTime is { } pointInTime)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalPointInTime, pointInTime);
            }

            if (_queryRoot.From is { } from)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalRangeStart, from);
            }

            if (_queryRoot.To is { } to)
            {
                annotatedTable = annotatedTable.AddAnnotation(MySqlAnnotationNames.TemporalRangeEnd, to);
            }

            return annotatedTable;
        }

        private TableExpressionBase VisitTpcTablesExpression(
            TableExpressionBase tpcTablesExpression
        )
        {
            var selectExpressions =
                (IReadOnlyList<SelectExpression>)s_tpcSelectExpressionsProperty.GetValue(tpcTablesExpression)!;

            var visitedSelectExpressions = selectExpressions
                .Select(selectExpression => (SelectExpression)Visit(selectExpression))
                .ToArray();

            if (selectExpressions
                .Zip(visitedSelectExpressions, ReferenceEquals)
                .All(unchanged => unchanged))
            {
                return tpcTablesExpression;
            }

            // TpcTablesExpression intentionally hides its union branches from ordinary
            // expression visitors. EF Core exposes that temporary shape only through
            // an internal namespace, so the bridge is resolved reflectively and fails fast
            // when the pinned EF patch changes. This keeps provider code free of an EF1001
            // suppression while still applying each branch's own temporal table metadata.
            var rebuiltExpression = (TableExpressionBase)s_tpcTablesExpressionConstructor.Invoke(
                [
                    tpcTablesExpression.Alias,
                    (IEntityType)s_tpcEntityTypeProperty.GetValue(tpcTablesExpression)!,
                    visitedSelectExpressions,
                    (ColumnExpression)s_tpcDiscriminatorColumnProperty.GetValue(tpcTablesExpression)!,
                    (List<string>)s_tpcDiscriminatorValuesProperty.GetValue(tpcTablesExpression)!,
                ]);

            foreach (var annotation in tpcTablesExpression.GetAnnotations())
            {
                rebuiltExpression = rebuiltExpression.AddAnnotation(annotation.Name, annotation.Value);
            }

            return rebuiltExpression;
        }

        private static PropertyInfo GetRequiredTpcProperty(
            string propertyName
        ) => s_tpcTablesExpressionType.GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"EF Core changed the '{propertyName}' contract on '{TpcTablesExpressionTypeName}'.");
    }
}
