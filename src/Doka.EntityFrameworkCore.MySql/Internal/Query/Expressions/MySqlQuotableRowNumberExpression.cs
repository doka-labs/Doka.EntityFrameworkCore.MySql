namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves EF10's native row-number shape with complete public-framework quotation.</summary>
internal sealed class MySqlQuotableRowNumberExpression : RowNumberExpression
{
    private static readonly ConstructorInfo s_constructor = typeof(RowNumberExpression).GetConstructor(
    [
        typeof(IReadOnlyList<SqlExpression>),
        typeof(IReadOnlyList<OrderingExpression>),
        typeof(RelationalTypeMapping),
    ])!;

    private static readonly ConstructorInfo s_sqlListConstructor =
        typeof(List<SqlExpression>).GetConstructor(Type.EmptyTypes)!;

    private static readonly ConstructorInfo s_orderingListConstructor =
        typeof(List<OrderingExpression>).GetConstructor(Type.EmptyTypes)!;

    private static readonly MethodInfo s_sqlListAdd =
        typeof(List<SqlExpression>).GetMethod(nameof(List<SqlExpression>.Add))!;

    private static readonly MethodInfo s_orderingListAdd =
        typeof(List<OrderingExpression>).GetMethod(nameof(List<OrderingExpression>.Add))!;

    /// <summary>Creates a native ordinal with the same partition, ordering, and mapping contracts as EF.</summary>
    public MySqlQuotableRowNumberExpression(
        IReadOnlyList<SqlExpression> partitions,
        IReadOnlyList<OrderingExpression> orderings,
        RelationalTypeMapping? typeMapping
    ) : base(partitions, orderings, typeMapping) { }

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    ) => Update(VisitItems(Partitions, visitor), VisitItems(Orderings, visitor));

    /// <inheritdoc />
    public override RowNumberExpression Update(
        IReadOnlyList<SqlExpression>? partitions,
        IReadOnlyList<OrderingExpression> orderings
    ) => Partitions.SequenceEqual(partitions ?? []) && Orderings.SequenceEqual(orderings)
        ? this
        : new MySqlQuotableRowNumberExpression(partitions ?? [], orderings, TypeMapping);

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote()
    {
        // WHY: EF10 omits partitions and supplies only two arguments to a three-argument
        // constructor. Reconstruct the public EF node so generated C# needs no internal Doka types.
        return New(s_constructor,
            ListInit(New(s_sqlListConstructor),
                Partitions.Select(partition => ElementInit(s_sqlListAdd, partition.Quote()))),
            ListInit(New(s_orderingListConstructor),
                Orderings.Select(ordering => ElementInit(s_orderingListAdd, ordering.Quote()))),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
    }

    /// <summary>Retains the quotation adapter when a visitor changes a child without copying unchanged lists.</summary>
    private static IReadOnlyList<T> VisitItems<T>(
        IReadOnlyList<T> items,
        ExpressionVisitor visitor
    )
        where T : Expression
    {
        T[]? changed = null;

        // WHY: These interface lists can allocate enumerators; lazy indexed copying preserves the unchanged path.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < items.Count; index++)
        {
            var original = items[index];
            var visited = (T)visitor.Visit(original);

            if (!ReferenceEquals(visited, original))
            {
                // WHY: Create an owned copy only when a visited child actually changes.
                // ReSharper disable once UseCollectionExpression
                changed ??= items.ToArray();
                changed[index] = visited;
            }
        }

        return changed ?? items;
    }
}
