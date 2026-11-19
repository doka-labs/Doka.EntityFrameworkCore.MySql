namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>Translates recorded scalar assignments without invoking a target constructor or source materializer.</summary>
    private MySqlInsertSelectExpression TranslateInsertSelect(
        MySqlInsertSelectQueryExpression insert
    )
    {
        if (!ReferenceEquals(_queryCompilationContext.Model.FindEntityType(insert.Target.Name), insert.Target))
        {
            throw new InvalidOperationException("INSERT SELECT target metadata belongs to a different EF model.");
        }

        var target = MySqlDirectWriteTarget.Resolve(insert.Target);
        ValidateInsertTarget(target);

        if (Visit(insert.Source) is not ShapedQueryExpression
            {
                QueryExpression: SelectExpression select,
                ShaperExpression: NewArrayExpression { NodeType: ExpressionType.NewArrayInit } values,
            }
            || values.Expressions.Count != insert.Properties.Count)
        {
            throw new InvalidOperationException(
                "INSERT SELECT requires a flat SQL-translatable assignment projection.");
        }

        var columns = new List<string>(insert.Properties.Count + 1);
        var projections = new List<ProjectionExpression>(insert.Properties.Count + 1);
        var assigned = new Dictionary<IColumn, int>();
        var discriminator = insert.Target.FindDiscriminatorProperty();

        // WHY: A physical shared column may be selected through different mapped properties.
        // Keep one equal assignment, but never silently choose one of two conflicting values.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < insert.Properties.Count; index++)
        {
            var property = insert.Properties[index];
            var column = target.RequireColumn(property);

            if (ReferenceEquals(property, discriminator))
            {
                throw new InvalidOperationException(
                    "INSERT SELECT uses the target model's discriminator; it cannot be assigned explicitly.");
            }

            if (property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
                || column.ComputedColumnSql is not null)
            {
                throw new InvalidOperationException(
                    $"INSERT SELECT cannot assign generated or read-only property '{property.Name}'.");
            }

            var mapping = column.PropertyMappings.First(mapping => ReferenceEquals(mapping.Property, property))
                .TypeMapping;

            var value = ResolveInsertProjection(values.Expressions[index], select);
            value = ApplyInsertMapping(value, property, mapping);

            if (assigned.TryGetValue(column, out var existing))
            {
                if (!projections[existing]
                    .Expression
                    .Equals(value))
                {
                    throw new InvalidOperationException(
                        $"INSERT SELECT has conflicting assignments for shared column '{column.Name}'.");
                }

                continue;
            }

            assigned.Add(column, columns.Count);
            columns.Add(column.Name);
            projections.Add(new ProjectionExpression(value, column.Name));
        }

        if (discriminator is not null)
        {
            var column = target.RequireColumn(discriminator);
            var mapping = column.PropertyMappings.First(mapping => ReferenceEquals(mapping.Property, discriminator))
                .TypeMapping;

            var value = new SqlConstantExpression(insert.Target.GetDiscriminatorValue(),
                discriminator.ClrType,
                mapping);

            assigned.Add(column, columns.Count);
            columns.Add(column.Name);
            projections.Add(new ProjectionExpression(value, column.Name));
        }

        foreach (var column in target.Table.Columns)
        {
            if (!assigned.ContainsKey(column)
                && !CanOmitInsertColumn(column))
            {
                throw new InvalidOperationException(
                    $"INSERT SELECT requires a value for physical column '{column.Name}' without a server default.");
            }
        }

        // WHY: No source entity/array is materialized. Finalizing the original SELECT first
        // preserves EF's completed DISTINCT/set/paging scopes; only its output columns change.
        select.ApplyProjection();
        var source = select.Update(select.Tables,
            select.Predicate,
            select.GroupBy,
            select.Having,
            projections,
            select.Orderings,
            select.Offset,
            select.Limit);

        MySqlInsertSourceValidator.Validate(source, target.Table);

        return new MySqlInsertSelectExpression(
            new TableExpression(_queryCompilationContext.SqlAliasManager.GenerateTableAlias(target.Table.Name),
                target.Table),
            source,
            columns.ToArray(),
            [],
            insert.CancellationTokenParameter);
    }

    /// <summary>Accepts only SQL projection leaves; client callbacks and collection shapers never execute.</summary>
    private static SqlExpression ResolveInsertProjection(
        Expression expression,
        SelectExpression select
    )
    {
        while (expression is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            } convert)
        {
            expression = convert.Operand;
        }

        return expression switch
        {
            ProjectionBindingExpression binding when select.GetProjection(binding) is SqlExpression sql => sql,
            SqlExpression sql => sql,
            _ => throw new InvalidOperationException(
                "INSERT SELECT assignments must translate completely to SQL; client callbacks and collections are unsupported."),
        };
    }

    /// <summary>Preserves store values and applies only explicitly known GUID representation conversions.</summary>
    private SqlExpression ApplyInsertMapping(
        SqlExpression value,
        IProperty property,
        RelationalTypeMapping targetMapping
    )
    {
        if (value is SqlConstantExpression constant)
        {
            return new SqlConstantExpression(constant.Value, constant.Type, targetMapping);
        }

        if (value is SqlParameterExpression parameter)
        {
            return parameter.ApplyTypeMapping(targetMapping);
        }

        var sourceMapping = value.TypeMapping
            ?? throw new InvalidOperationException(
                "INSERT SELECT requires a relational mapping for every source value.");

        var propertyType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (propertyType == typeof(Guid))
        {
            // WHY: Equal custom GUID converters on the same physical representation
            // already store the required bytes/text. Copy them without invoking CLR
            // converters per row; only different canonical representations need SQL.
            if (sourceMapping.StoreType.Equals(targetMapping.StoreType, StringComparison.OrdinalIgnoreCase)
                && EquivalentInsertConverters(sourceMapping.Converter, targetMapping.Converter))
            {
                return value;
            }

            return ApplyInsertGuidMapping(value, sourceMapping, targetMapping);
        }

        if (!EquivalentInsertConverters(sourceMapping.Converter, targetMapping.Converter))
        {
            throw new InvalidOperationException(
                $"INSERT SELECT cannot apply different CLR converter representations to property '{property.Name}' on the server.");
        }

        return value;
    }

    /// <summary>Uses the existing canonical GUID formatter and portable network-order UNHEX representation.</summary>
    private SqlExpression ApplyInsertGuidMapping(
        SqlExpression value,
        RelationalTypeMapping source,
        RelationalTypeMapping target
    )
    {
        var sourceBinary = source is MySqlGuidBinaryTypeMapping { Converter: null };
        var targetBinary = target is MySqlGuidBinaryTypeMapping { Converter: null };
        var sourceText = source.Converter?.GetType() == typeof(GuidToStringConverter);
        var targetText = target.Converter?.GetType() == typeof(GuidToStringConverter);

        if ((!sourceBinary && !sourceText)
            || (!targetBinary && !targetText))
        {
            throw new InvalidOperationException(
                "INSERT SELECT GUID conversion requires the provider's Binary16, Char36 or native UUID mappings.");
        }

        if (sourceBinary == targetBinary)
        {
            return value;
        }

        var formatter = new MySqlGuidTextExpressionFactory(_sqlExpressionFactory, _typeMappingSource);
        var text = formatter.Create(value);

        if (targetText)
        {
            return text;
        }

        var stringMapping = MySqlTranslationTypeMapping.GetRequired(_typeMappingSource, typeof(string));
        var hexadecimal = _sqlExpressionFactory.Function("REPLACE",
            [
                text, _sqlExpressionFactory.Constant("-", stringMapping),
                _sqlExpressionFactory.Constant(string.Empty, stringMapping),
            ],
            nullable: true,
            argumentsPropagateNullability: [true, false, false],
            typeof(string),
            stringMapping);

        return _sqlExpressionFactory.Function("UNHEX",
            [hexadecimal],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(Guid),
            target);
    }

    /// <summary>Rejects custom converter disagreement instead of evaluating CLR conversions over server rows.</summary>
    private static bool EquivalentInsertConverters(
        ValueConverter? source,
        ValueConverter? target
    ) => ReferenceEquals(source, target)
        || (source is not null
            && target is not null
            && source.ModelClrType == target.ModelClrType
            && source.ProviderClrType == target.ProviderClrType
            && ExpressionEqualityComparer.Instance.Equals(source.ConvertToProviderExpression,
                target.ConvertToProviderExpression)
            && ExpressionEqualityComparer.Instance.Equals(source.ConvertFromProviderExpression,
                target.ConvertFromProviderExpression));

    /// <summary>Checks known physical contracts without adding a per-execution schema query.</summary>
    private static void ValidateInsertTarget(
        MySqlDirectWriteTarget target
    )
    {
        if (target.EntityType.GetInsertStoredProcedure() is not null)
        {
            throw new InvalidOperationException("INSERT SELECT cannot bypass an insert stored-procedure mapping.");
        }

        var storageEngine = target.Table.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string
            ?? target.EntityType.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string;

        if (storageEngine is not null
            && !storageEngine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "INSERT SELECT requires the model's direct-write target to use InnoDB.");
        }
    }

    /// <summary>Distinguishes real server generation from a client-only generated key.</summary>
    private static bool CanOmitInsertColumn(
        IColumn column
    ) => column.IsNullable
        || column.ComputedColumnSql is not null
        || column.DefaultValueSql is not null
        || column.TryGetDefaultValue(out _)
        || column.PropertyMappings.Any(mapping
            => mapping.Property.GetMySqlValueGenerationStrategy() == MySqlValueGenerationStrategy.AutoIncrement);
}
