namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Rejects collection loading carried by a bound entity rather than a rehydrated output entity.
/// </summary>
internal sealed class MySqlMaterializedSourceIncludeValidator : MySqlShapedQueryTraversingExpressionVisitor
{
    public static void Validate(
        Expression expression
    ) => new MySqlMaterializedSourceIncludeValidator().Visit(expression);

    protected override Expression VisitExtension(
        Expression node
    )
    {
        if (node is IncludeExpression { Navigation.IsCollection: true } include
            && IsBoundEntity(include.EntityExpression))
        {
            // WHY: Navigation expansion delays Includes beyond identity-shaped operators. Inspect
            // the final owner projection so ignored Includes and IgnoreAutoIncludes stay ordinary EF.
            throw new InvalidOperationException(
                "A materialized entity source cannot carry a collection Include. "
                + "Bind a flat source and rehydrate the output entity before including its collection.");
        }

        return base.VisitExtension(node);
    }

    private static bool IsBoundEntity(
        Expression entity
    )
    {
        // WHY: Includes and conversions may alternate; peeling both wrappers here
        // preserves the same entity lookup without recursive conversion frames.
        while (true)
        {
            if (entity is IncludeExpression include)
            {
                entity = include.EntityExpression;
                continue;
            }

            if (entity is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
            {
                entity = conversion.Operand;
                continue;
            }

            break;
        }

        if (entity is not StructuralTypeShaperExpression
            {
                StructuralType: IEntityType entityType,
                ValueBufferExpression: ProjectionBindingExpression
                {
                    QueryExpression: SelectExpression select,
                } binding,
            }
            || select.GetProjection(binding) is not ConstantExpression
            {
                Value: IReadOnlyDictionary<IPropertyBase, int> propertyIndexes,
            })
        {
            return false;
        }

        var key = entityType.FindPrimaryKey();

        return key is not null
            && key.Properties.Any(property =>
                propertyIndexes.TryGetValue(property, out var index)
                && IsBoundColumn(select, select.Projection[index].Expression));
    }

    private static bool IsBoundColumn(
        SelectExpression select,
        SqlExpression expression
    )
    {
        while (true)
        {
            if (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion)
            {
                expression = conversion.Operand;
                continue;
            }

            if (expression is not ColumnExpression column)
            {
                return false;
            }

            foreach (var source in select.Tables)
            {
                var table = source;

                while (table is JoinExpressionBase join)
                {
                    table = join.Table;
                }

                if (table.Alias != column.TableAlias)
                {
                    continue;
                }

                if (table is MySqlMaterializedSourceExpression)
                {
                    return true;
                }

                if (table is SelectExpression inner)
                {
                    var projection = inner.Projection.FirstOrDefault(candidate => candidate.Alias == column.Name);

                    return projection is not null && IsBoundColumn(inner, projection.Expression);
                }
            }

            return false;
        }
    }
}
