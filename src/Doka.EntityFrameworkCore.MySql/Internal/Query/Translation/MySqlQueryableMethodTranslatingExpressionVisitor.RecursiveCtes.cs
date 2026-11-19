namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>Uses EF's set-operation projection alignment while keeping the recursive object graph finite.</summary>
    private ShapedQueryExpression TranslateRecursiveCteSource(
        MySqlRecursiveSourceQueryExpression recursive
    )
    {
        var maximumDepth = TranslateExpression(recursive.MaximumDepth)
            ?? throw new InvalidOperationException("A recursive CTE maximum depth must translate to an integer value.");

        var anchor = NormalizeSourceShaper(TranslateIndependentSource(recursive.Anchor));
        var first = TranslateRecursiveStep(anchor, recursive, maximumDepth);
        var step = first.Step;
        var widened = MySqlRecursiveTextCapacity.WidenAnchor(anchor,
            step,
            first.Frontier,
            maximumDepth,
            recursive.BindingId);

        if (widened is not null)
        {
            // WHY: Widen before rebuilding frontier columns, so later SQL parameters use the real
            // recursive capacity as well. A late SQL-only CAST would leave their old Size behind.
            step = TranslateRecursiveStep(anchor, recursive, maximumDepth)
                .Step;

            MySqlRecursiveTextCapacity.ApplyStepMappings(step, widened);
        }

        if (anchor.ShaperExpression.Type != step.ShaperExpression.Type
            || (anchor.ShaperExpression is StructuralTypeShaperExpression anchorEntity
                && (step.ShaperExpression is not StructuralTypeShaperExpression stepEntity
                    || !ReferenceEquals(anchorEntity.StructuralType, stepEntity.StructuralType))))
        {
            throw new InvalidOperationException("Recursive CTE arms require the same mapped scalar or entity shape.");
        }

        // WHY: EF matches projection members, physical mappings and nullability for UNION ALL.
        // Rebuilding a DTO or matching user column names would lose converters and entity identity.
        return TranslateConcat(anchor, step);
    }

    /// <summary>Owns one provisional frontier only for one independent step translation attempt.</summary>
    private (ShapedQueryExpression Step, ShapedQueryExpression Frontier) TranslateRecursiveStep(
        ShapedQueryExpression anchor,
        MySqlRecursiveSourceQueryExpression recursive,
        SqlExpression maximumDepth
    )
    {
        var compilation = (MySqlQueryCompilationContext)_queryCompilationContext;
        var frontier = BindCompletedSource(CloneSourceShape(anchor),
            recursive.BindingId,
            MySqlSourceBindingKind.Recursive,
            maximumDepth);

        if (!compilation.RecursiveCteFrontiers.TryAdd(recursive.BindingId, frontier))
        {
            throw new InvalidOperationException("Recursive CTE definitions cannot recursively translate one another.");
        }

        ShapedQueryExpression step;

        try
        {
            step = NormalizeSourceShaper(TranslateIndependentSource(recursive.RecursiveStep));
        }
        finally
        {
            // WHY: A provisional frontier is visible only during its one step translation, never
            // during output composition or a later independently compiled query.
            compilation.RecursiveCteFrontiers.Remove(recursive.BindingId);
        }

        return (step, frontier);
    }
}
