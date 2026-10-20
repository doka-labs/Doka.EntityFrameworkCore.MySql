namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Retains one directed inner-join edge through EF's ordinary relational visitors.</summary>
internal sealed class MySqlStraightJoinExpression : InnerJoinExpression
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlStraightJoinExpression).GetConstructor(
    [
        typeof(TableExpressionBase),
        typeof(SqlExpression),
        typeof(bool),
        typeof(IReadOnlyDictionary<string, IAnnotation>)
    ])!;

    /// <summary>Creates an edge with the existing join's predicate, pruning contract and annotations.</summary>
    public MySqlStraightJoinExpression(
        TableExpressionBase table,
        SqlExpression predicate,
        bool prunable = false,
        IReadOnlyDictionary<string, IAnnotation>? annotations = null
    )
#pragma warning disable EF1001 // Preserve the public join node's existing annotations through EF's documented provider seam.
        : base(table, predicate, prunable, annotations)
#pragma warning restore EF1001
    { }

    /// <inheritdoc />
    public override JoinExpressionBase Update(
        TableExpressionBase table,
        SqlExpression joinPredicate
    )
        // WHY: InnerJoinExpression.Update rewrites a changed TRUE predicate to CrossJoin and drops
        // annotations. This edge's physical direction must survive that otherwise equivalent rewrite.
        => ReferenceEquals(table, Table) && ReferenceEquals(joinPredicate, JoinPredicate)
            ? this
            : new MySqlStraightJoinExpression(table, joinPredicate, IsPrunable, Annotations);

    /// <inheritdoc />
    public override InnerJoinExpression Update(
        TableExpressionBase table
    ) => ReferenceEquals(table, Table)
        ? this
        : new MySqlStraightJoinExpression(table, JoinPredicate, IsPrunable, Annotations);

    /// <inheritdoc />
    protected override InnerJoinExpression WithAnnotations(
        IReadOnlyDictionary<string, IAnnotation> annotations
    ) => new MySqlStraightJoinExpression(Table, JoinPredicate, IsPrunable, annotations);

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote() => New(
        s_constructor,
        Table.Quote(),
        JoinPredicate.Quote(),
        Expression.Constant(IsPrunable),
        MySqlRelationalExpressionQuoting.QuoteAnnotations(Annotations));

    /// <inheritdoc />
    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter.Append("STRAIGHT_JOIN ");
        expressionPrinter.Visit(Table);
        expressionPrinter.Append(" ON ");
        expressionPrinter.Visit(JoinPredicate);
        PrintAnnotations(expressionPrinter);
    }
}
