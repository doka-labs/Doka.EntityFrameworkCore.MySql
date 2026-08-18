namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlAnnotationCodeGenerator : AnnotationCodeGenerator
{
    private static readonly MethodInfo s_entityTypeToTableMethod =
        typeof(RelationalEntityTypeBuilderExtensions).GetRuntimeMethod(
            nameof(RelationalEntityTypeBuilderExtensions.ToTable),
            [
                typeof(EntityTypeBuilder),
                typeof(Action<TableBuilder>),
            ])!;

    private static readonly MethodInfo s_tableIsTemporalMethod = typeof(MySqlTableBuilderExtensions).GetRuntimeMethod(
        nameof(MySqlTableBuilderExtensions.IsTemporal),
        [
            typeof(TableBuilder),
            typeof(Action<MySqlTemporalTableBuilder>),
        ])!;

    private static readonly MethodInfo s_temporalUseHistoryTableMethod =
        typeof(MySqlTemporalTableBuilder).GetRuntimeMethod(
            nameof(MySqlTemporalTableBuilder.UseHistoryTable),
            [typeof(string)])!;

    private static readonly MethodInfo s_temporalUseHistoryTableWithSchemaMethod =
        typeof(MySqlTemporalTableBuilder).GetRuntimeMethod(
            nameof(MySqlTemporalTableBuilder.UseHistoryTable),
            [
                typeof(string),
                typeof(string),
            ])!;

    private static readonly MethodInfo s_temporalHasPeriodStartMethod =
        typeof(MySqlTemporalTableBuilder).GetRuntimeMethod(
            nameof(MySqlTemporalTableBuilder.HasPeriodStart),
            [typeof(string)])!;

    private static readonly MethodInfo s_temporalHasPeriodEndMethod =
        typeof(MySqlTemporalTableBuilder).GetRuntimeMethod(
            nameof(MySqlTemporalTableBuilder.HasPeriodEnd),
            [typeof(string)])!;

    private static readonly MethodInfo s_propertyHasColumnNameMethod =
        typeof(RelationalPropertyBuilderExtensions).GetRuntimeMethod(
            nameof(RelationalPropertyBuilderExtensions.HasColumnName),
            [
                typeof(PropertyBuilder),
                typeof(string),
            ])!;

    private static readonly MethodInfo s_tableHasApplicationTimePeriodMethod =
        typeof(MySqlApplicationTimeTableBuilderExtensions).GetRuntimeMethod(
            nameof(MySqlApplicationTimeTableBuilderExtensions.HasApplicationTimePeriod),
            [
                typeof(TableBuilder),
                typeof(Action<MySqlApplicationTimeTableBuilder>),
            ])!;

    private static readonly MethodInfo s_applicationTimeHasPeriodNameMethod =
        typeof(MySqlApplicationTimeTableBuilder).GetRuntimeMethod(
            nameof(MySqlApplicationTimeTableBuilder.HasPeriodName),
            [typeof(string)])!;

    private static readonly MethodInfo s_applicationTimeHasPeriodStartMethod =
        typeof(MySqlApplicationTimeTableBuilder).GetRuntimeMethod(
            nameof(MySqlApplicationTimeTableBuilder.HasPeriodStart),
            [typeof(string)])!;

    private static readonly MethodInfo s_applicationTimeHasPeriodEndMethod =
        typeof(MySqlApplicationTimeTableBuilder).GetRuntimeMethod(
            nameof(MySqlApplicationTimeTableBuilder.HasPeriodEnd),
            [typeof(string)])!;

    private static readonly MethodInfo s_applicationTimeUseWithoutOverlapsMethod =
        typeof(MySqlApplicationTimeTableBuilder).GetRuntimeMethod(
            nameof(MySqlApplicationTimeTableBuilder.UseWithoutOverlaps),
            [typeof(bool)])!;

    public MySqlAnnotationCodeGenerator(
        AnnotationCodeGeneratorDependencies dependencies
    ) : base(dependencies) { }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IModel model,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(model, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.CharSet, out var charSetAnnotation)
            && charSetAnnotation.Value is string charSet
            && !string.IsNullOrWhiteSpace(charSet))
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlModelBuilderExtensions.HasCharSet), charSet));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IEntityType entityType,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(entityType, annotations)
            .ToList();

        var tableBuilderCalls = new List<MethodCallCodeFragment>();

        GenerateTemporalTableCall(entityType, annotations, tableBuilderCalls);
        GenerateApplicationTimeTableCall(entityType, annotations, tableBuilderCalls);

        if (tableBuilderCalls.Count > 0)
        {
            fragments.Add(
                new MethodCallCodeFragment(
                    s_entityTypeToTableMethod,
                    new NestedClosureCodeFragment("tableBuilder", tableBuilderCalls)));
        }

        if (annotations.Remove(MySqlAnnotationNames.CharSet, out var charSetAnnotation)
            && charSetAnnotation.Value is string charSet
            && !string.IsNullOrWhiteSpace(charSet))
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlEntityTypeBuilderExtensions.HasCharSet), charSet));
        }

        if (annotations.Remove(MySqlAnnotationNames.StorageEngine, out var storageEngineAnnotation)
            && storageEngineAnnotation.Value is string storageEngine
            && !string.IsNullOrWhiteSpace(storageEngine))
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlEntityTypeBuilderExtensions.UseStorageEngine), storageEngine));
        }

        return fragments;
    }

    private static void GenerateTemporalTableCall(
        IEntityType entityType,
        IDictionary<string, IAnnotation> annotations,
        List<MethodCallCodeFragment> tableBuilderCalls
    )
    {
        if (annotations.TryGetValue(MySqlAnnotationNames.IsTemporal, out var annotation)
            && (annotation.Value as bool?) == true)
        {
            var periodStartPropertyName = RequireName(
                entityType.GetMySqlTemporalPeriodStartPropertyName(),
                entityType,
                "temporal period-start property");

            var periodEndPropertyName = RequireName(
                entityType.GetMySqlTemporalPeriodEndPropertyName(),
                entityType,
                "temporal period-end property");
            var temporalCalls = new List<MethodCallCodeFragment>();

            var historyTableName = entityType.GetMySqlTemporalHistoryTableName();
            var historyTableSchema = entityType.GetMySqlTemporalHistoryTableSchema();

            if (historyTableName is not null)
            {
                temporalCalls.Add(
                    historyTableSchema is null
                        ? new MethodCallCodeFragment(s_temporalUseHistoryTableMethod, historyTableName)
                        : new MethodCallCodeFragment(
                            s_temporalUseHistoryTableWithSchemaMethod,
                            historyTableName,
                            historyTableSchema));
            }

            temporalCalls.Add(
                CreatePeriodPropertyCall(entityType, periodStartPropertyName, s_temporalHasPeriodStartMethod));
            temporalCalls.Add(
                CreatePeriodPropertyCall(entityType, periodEndPropertyName, s_temporalHasPeriodEndMethod));

            tableBuilderCalls.Add(
                new MethodCallCodeFragment(
                    s_tableIsTemporalMethod,
                    new NestedClosureCodeFragment("temporalTableBuilder", temporalCalls)));
        }

        annotations.Remove(MySqlAnnotationNames.IsTemporal);
        annotations.Remove(MySqlAnnotationNames.TemporalHistoryTableName);
        annotations.Remove(MySqlAnnotationNames.TemporalHistoryTableSchema);
        annotations.Remove(MySqlAnnotationNames.TemporalPeriodStartPropertyName);
        annotations.Remove(MySqlAnnotationNames.TemporalPeriodEndPropertyName);
    }

    private static void GenerateApplicationTimeTableCall(
        IEntityType entityType,
        IDictionary<string, IAnnotation> annotations,
        List<MethodCallCodeFragment> tableBuilderCalls
    )
    {
        if (annotations.TryGetValue(MySqlAnnotationNames.IsApplicationTime, out var annotation)
            && annotation.Value as bool? == true)
        {
            var periodName = RequireName(
                entityType.GetMySqlApplicationTimePeriodName(),
                entityType,
                "application-time period");

            var periodStartPropertyName = RequireName(
                entityType.GetMySqlApplicationTimePeriodStartPropertyName(),
                entityType,
                "application-time period-start property");

            var periodEndPropertyName = RequireName(
                entityType.GetMySqlApplicationTimePeriodEndPropertyName(),
                entityType,
                "application-time period-end property");

            var applicationTimeCalls = new List<MethodCallCodeFragment>
            {
                new(s_applicationTimeHasPeriodNameMethod, periodName),
                CreatePeriodPropertyCall(
                    entityType,
                    periodStartPropertyName,
                    s_applicationTimeHasPeriodStartMethod),
                CreatePeriodPropertyCall(
                    entityType,
                    periodEndPropertyName,
                    s_applicationTimeHasPeriodEndMethod),
            };

            if (entityType.GetMySqlApplicationTimeWithoutOverlaps())
            {
                applicationTimeCalls.Add(
                    new MethodCallCodeFragment(s_applicationTimeUseWithoutOverlapsMethod));
            }

            tableBuilderCalls.Add(
                new MethodCallCodeFragment(
                    s_tableHasApplicationTimePeriodMethod,
                    new NestedClosureCodeFragment("applicationTimeTableBuilder", applicationTimeCalls)));
        }

        annotations.Remove(MySqlAnnotationNames.IsApplicationTime);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodStartPropertyName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimePeriodEndPropertyName);
        annotations.Remove(MySqlAnnotationNames.ApplicationTimeWithoutOverlaps);
    }

    private static MethodCallCodeFragment CreatePeriodPropertyCall(
        IEntityType entityType,
        string propertyName,
        MethodInfo method
    )
    {
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.DisplayName()}' has temporal metadata but no table mapping.");

        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var columnName = entityType
            .FindProperty(propertyName)
            ?.GetColumnName(storeObject);

        var call = new MethodCallCodeFragment(method, propertyName);

        return columnName is null ? call : call.Chain(s_propertyHasColumnNameMethod, columnName);
    }

    private static string RequireName(
        string? value,
        IEntityType entityType,
        string role
    ) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Entity type '{entityType.DisplayName()}' has no {role} name.")
        : value;

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IKey key,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(key, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps, out var annotation)
            && annotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlKeyBuilderExtensions.UseWithoutOverlaps)));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IProperty property,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(property, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.GuidFormat, out var guidFormatAnnotation)
            && guidFormatAnnotation.Value is MySqlGuidFormat guidFormat)
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlPropertyBuilderExtensions.HasMySqlGuidFormat), guidFormat));
        }

        if (annotations.Remove(MySqlAnnotationNames.ValueGenerationStrategy, out var valueGenerationAnnotation)
            && valueGenerationAnnotation.Value is MySqlValueGenerationStrategy valueGenerationStrategy)
        {
            fragments.Add(
                new MethodCallCodeFragment(
                    nameof(MySqlPropertyBuilderExtensions.HasMySqlValueGenerationStrategy),
                    valueGenerationStrategy));
        }

        if (annotations.Remove(
                MySqlAnnotationNames.SpatialReferenceSystemId,
                out var spatialReferenceSystemIdAnnotation)
            && spatialReferenceSystemIdAnnotation.Value is int spatialReferenceSystemId)
        {
            fragments.Add(new MethodCallCodeFragment("HasSrid", spatialReferenceSystemId));
        }

        if (annotations.Remove(MySqlAnnotationNames.Invisible, out var invisibleAnnotation)
            && invisibleAnnotation.Value is bool invisible)
        {
            fragments.Add(
                invisible
                    ? new MethodCallCodeFragment(nameof(MySqlPropertyBuilderExtensions.IsInvisible))
                    : new MethodCallCodeFragment(nameof(MySqlPropertyBuilderExtensions.IsInvisible), false));
        }

        return fragments;
    }

    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IIndex index,
        IDictionary<string, IAnnotation> annotations
    )
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(annotations);

        var fragments = base
            .GenerateFluentApiCalls(index, annotations)
            .ToList();

        if (annotations.Remove(MySqlAnnotationNames.SpatialIndex, out var spatialIndexAnnotation)
            && spatialIndexAnnotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment("IsSpatial"));
        }

        if (annotations.Remove(MySqlAnnotationNames.FullTextIndex, out var fullTextIndexAnnotation)
            && fullTextIndexAnnotation.Value is true)
        {
            fragments.Add(
                new MethodCallCodeFragment(nameof(MySqlIndexBuilderExtensions.IsFullText)));
        }

        if (annotations.Remove(MySqlAnnotationNames.IndexPrefixLength, out var prefixLengthAnnotation)
            && prefixLengthAnnotation.Value is int[] prefixLengths)
        {
            fragments.Add(
                new MethodCallCodeFragment(
                    nameof(MySqlIndexBuilderExtensions.HasPrefixLength),
                    prefixLengths.Cast<object>().ToArray()));
        }

        if (annotations.Remove(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps, out var annotation)
            && annotation.Value is true)
        {
            fragments.Add(new MethodCallCodeFragment(nameof(MySqlIndexBuilderExtensions.UseWithoutOverlaps)));
        }

        return fragments;
    }
}
