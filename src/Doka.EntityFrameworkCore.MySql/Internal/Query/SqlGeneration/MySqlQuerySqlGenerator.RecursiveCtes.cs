namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    private Dictionary<int, MySqlRecursiveCteShape>? _recursiveCteShapes;

    /// <summary>Preserves actual text encoding and comparison while establishing the proved native anchor capacity.</summary>
    private void GenerateRecursiveTextCast(
        MySqlRecursiveTextCastExpression expression
    )
    {
        Sql.Append("CAST(");
        Visit(expression.Operand);
        Sql
            .Append(" AS CHAR(")
            .Append(expression.Capacity.ToString(CultureInfo.InvariantCulture))
            .Append(") CHARACTER SET ")
            .Append(expression.CharSet)
            .Append(") COLLATE ")
            .Append(expression.Collation);
    }

    /// <summary>Emits only hidden depth projections and a native recursive predicate, leaving visible columns unchanged.</summary>
    private void GenerateRecursiveCteSource(
        MySqlRecursiveCteShape shape
    )
    {
        var integerMapping = shape.Frontier.MaximumDepth.TypeMapping
            ?? throw new InvalidOperationException("Recursive CTE depth requires an integer type mapping.");

        var zero = new SqlConstantExpression(0, integerMapping);
        var one = new SqlConstantExpression(1, integerMapping);
        var depth = new ColumnExpression(shape.DepthColumnName,
            shape.Frontier.Alias!,
            typeof(int),
            integerMapping,
            nullable: false);

        var nextDepth = new SqlBinaryExpression(ExpressionType.Add, depth, one, typeof(int), integerMapping);
        var canRecurse = new SqlBinaryExpression(ExpressionType.LessThan,
            depth,
            shape.Frontier.MaximumDepth,
            typeof(bool),
            BoolTypeMapping.Default);

        var stepPredicate = shape.Step.Predicate is null
            ? canRecurse
            : new SqlBinaryExpression(ExpressionType.AndAlso,
                shape.Step.Predicate,
                canRecurse,
                typeof(bool),
                BoolTypeMapping.Default);

        Visit(AppendRecursiveDepth(shape.Anchor, zero, shape.DepthColumnName, shape.Anchor.Predicate));
        Sql.AppendLine();
        Sql.AppendLine("UNION ALL");
        Visit(AppendRecursiveDepth(shape.Step, nextDepth, shape.DepthColumnName, stepPredicate));
        Sql.AppendLine();
    }

    /// <summary>Copies a finalized SQL arm so the hidden depth never becomes an EF key, member or shaper ordinal.</summary>
    private static SelectExpression AppendRecursiveDepth(
        SelectExpression arm,
        SqlExpression depth,
        string name,
        SqlExpression? predicate
    )
    {
        // WHY: EF can legitimately project an empty flat DTO. SQL still needs the same visible
        // SELECT 1 sentinel declared by that relation's header before its separate hidden depth.
        ProjectionExpression[] projections = arm.Projection.Count == 0
            ?
            [
                new ProjectionExpression(new SqlConstantExpression(1, IntTypeMapping.Default), "__empty"),
                new ProjectionExpression(depth, name),
            ]
            : [.. arm.Projection, new ProjectionExpression(depth, name)];

        return arm.Update(
            arm.Tables,
            predicate,
            arm.GroupBy,
            arm.Having,
            projections,
            arm.Orderings,
            arm.Offset,
            arm.Limit);
    }
}
