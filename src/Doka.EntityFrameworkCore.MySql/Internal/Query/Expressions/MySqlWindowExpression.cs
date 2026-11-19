namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Owns an immutable scalar window calculation without captured descriptors or query state.</summary>
internal sealed class MySqlWindowExpression : SqlExpression
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlWindowExpression).GetConstructor(
    [
        typeof(MySqlWindowFunction),
        typeof(IReadOnlyList<SqlExpression>),
        typeof(IReadOnlyList<SqlExpression>),
        typeof(IReadOnlyList<OrderingExpression>),
        typeof(MySqlWindowFrame),
        typeof(Type),
        typeof(RelationalTypeMapping),
        typeof(bool),
    ])!;

    private static readonly ConstructorInfo s_sqlListConstructor =
        typeof(List<SqlExpression>).GetConstructor(Type.EmptyTypes)!;

    private static readonly ConstructorInfo s_orderingListConstructor =
        typeof(List<OrderingExpression>).GetConstructor(Type.EmptyTypes)!;

    private static readonly MethodInfo s_sqlListAdd =
        typeof(List<SqlExpression>).GetMethod(nameof(List<SqlExpression>.Add))!;

    private static readonly MethodInfo s_orderingListAdd =
        typeof(List<OrderingExpression>).GetMethod(nameof(List<OrderingExpression>.Add))!;

    /// <summary>Creates a complete typed calculation and takes snapshots of its child lists.</summary>
    public MySqlWindowExpression(
        MySqlWindowFunction function,
        IReadOnlyList<SqlExpression> arguments,
        IReadOnlyList<SqlExpression> partitions,
        IReadOnlyList<OrderingExpression> orderings,
        MySqlWindowFrame? frame,
        Type type,
        RelationalTypeMapping? typeMapping,
        bool nullable
    ) : base(type, typeMapping)
    {
        if (!Enum.IsDefined(function))
        {
            throw new ArgumentOutOfRangeException(nameof(function));
        }

        Function = function;

        // WHY: Own all three snapshots so later caller mutations cannot change this SQL expression.
        // ReSharper disable once UseCollectionExpression
        Arguments = Array.AsReadOnly(arguments.ToArray());

        // ReSharper disable once UseCollectionExpression
        Partitions = Array.AsReadOnly(partitions.ToArray());

        // ReSharper disable once UseCollectionExpression
        Orderings = Array.AsReadOnly(orderings.ToArray());

        Frame = frame;
        IsNullable = nullable;
    }

    /// <summary>Gets the native calculation, independently of its OVER definition.</summary>
    public MySqlWindowFunction Function { get; }

    /// <summary>Gets the function's scalar arguments in native order.</summary>
    public IReadOnlyList<SqlExpression> Arguments { get; }

    /// <summary>Gets the partition keys using their real mapped equality semantics.</summary>
    public IReadOnlyList<SqlExpression> Partitions { get; }

    /// <summary>Gets the window ordering, which does not imply result ordering.</summary>
    public IReadOnlyList<OrderingExpression> Orderings { get; }

    /// <summary>Gets the explicit frame, or NULL to preserve the native default.</summary>
    public MySqlWindowFrame? Frame { get; }

    /// <summary>Gets whether a missing neighbor or empty frame can produce SQL NULL.</summary>
    public bool IsNullable { get; }

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    ) => Update(VisitItems(Arguments, visitor),
        VisitItems(Partitions, visitor),
        VisitItems(Orderings, visitor),
        Frame?.Visit(visitor));

    /// <summary>Preserves identity when a visitor leaves every scalar child unchanged.</summary>
    public MySqlWindowExpression Update(
        IReadOnlyList<SqlExpression> arguments,
        IReadOnlyList<SqlExpression> partitions,
        IReadOnlyList<OrderingExpression> orderings,
        MySqlWindowFrame? frame
    ) => Equals(frame, Frame)
        && arguments.SequenceEqual(Arguments)
        && partitions.SequenceEqual(Partitions)
        && orderings.SequenceEqual(Orderings)
            ? this
            : new MySqlWindowExpression(Function,
                arguments,
                partitions,
                orderings,
                frame,
                Type,
                TypeMapping,
                IsNullable);

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote() => New(
        s_constructor,
        Constant(Function),
        QuoteSqlList(Arguments),
        QuoteSqlList(Partitions),
        ListInit(New(s_orderingListConstructor),
            Orderings.Select(ordering => ElementInit(s_orderingListAdd, ordering.Quote()))),
        Frame?.Quote() ?? Constant(null, typeof(MySqlWindowFrame)),
        Constant(Type, typeof(Type)),
        RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping),
        Constant(IsNullable));

    /// <summary>Quotes the closed child type without runtime-generated array construction.</summary>
    [Experimental("EF9100")]
    private static ListInitExpression QuoteSqlList(
        IReadOnlyList<SqlExpression> expressions
    )
    {
        // WHY: NewArrayInit requires dynamic code even though these element types are known.
        // Concrete closed lists and their known Add methods reconstruct the same IReadOnlyList contract.
        return ListInit(New(s_sqlListConstructor),
            expressions.Select(expression => ElementInit(s_sqlListAdd, expression.Quote())));
    }

    /// <inheritdoc />
    protected override void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter
            .Append(Function.ToString())
            .Append("(");

        expressionPrinter.VisitCollection(Arguments);
        expressionPrinter.Append(") OVER (PARTITION BY ");
        expressionPrinter.VisitCollection(Partitions);
        expressionPrinter.Append(" ORDER BY ");
        expressionPrinter.VisitCollection(Orderings);
        expressionPrinter.Append(")");
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => ReferenceEquals(this, obj)
        || (obj is MySqlWindowExpression other
            && base.Equals(other)
            && Function == other.Function
            && IsNullable == other.IsNullable
            && Equals(Frame, other.Frame)
            && Arguments.SequenceEqual(other.Arguments)
            && Partitions.SequenceEqual(other.Partitions)
            && Orderings.SequenceEqual(other.Orderings));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Function);
        hash.Add(IsNullable);
        hash.Add(Frame);
        AddItems(ref hash, Arguments);
        AddItems(ref hash, Partitions);
        AddItems(ref hash, Orderings);

        return hash.ToHashCode();
    }

    private static IReadOnlyList<T> VisitItems<T>(
        IReadOnlyList<T> items,
        ExpressionVisitor visitor
    )
        where T : Expression
    {
        T[]? changed = null;

        // WHY: These read-only wrappers allocate interface enumerators; indexed traversal also permits lazy copying.
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

    private static void AddItems<T>(
        ref HashCode hash,
        IReadOnlyList<T> items
    )
    {
        // WHY: Hashing the stored read-only wrappers must not allocate one enumerator per child list.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < items.Count; index++)
        {
            hash.Add(items[index]);
        }
    }
}

/// <summary>Enumerates supported scalar calculations rather than accepting SQL function strings.</summary>
internal enum MySqlWindowFunction
{
    Count,
    LongCount,
    Sum,
    Average,
    Min,
    Max,
    RowNumber,
    Rank,
    DenseRank,
    Ntile,
    PercentRank,
    CumeDist,
    Lag,
    Lead,
    FirstValue,
    LastValue,
    NthValue,
}

/// <summary>Preserves an exact ROWS or numeric RANGE frame in the SQL tree and quotation.</summary>
internal sealed record MySqlWindowFrame(
    bool Rows,
    MySqlWindowBoundKind StartKind,
    SqlExpression? StartOffset,
    MySqlWindowBoundKind EndKind,
    SqlExpression? EndOffset
)
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlWindowFrame).GetConstructor(
    [
        typeof(bool),
        typeof(MySqlWindowBoundKind),
        typeof(SqlExpression),
        typeof(MySqlWindowBoundKind),
        typeof(SqlExpression),
    ])!;

    /// <summary>Visits actual parameterized offsets without evaluating or capturing them.</summary>
    public MySqlWindowFrame Visit(
        ExpressionVisitor visitor
    )
    {
        var start = (SqlExpression?)visitor.Visit(StartOffset);
        var end = (SqlExpression?)visitor.Visit(EndOffset);

        return ReferenceEquals(start, StartOffset) && ReferenceEquals(end, EndOffset)
            ? this
            : this with
            {
                StartOffset = start,
                EndOffset = end,
            };
    }

    /// <summary>Reconstructs all frame facets without embedding an opaque runtime descriptor constant.</summary>
    [Experimental("EF9100")]
    public Expression Quote() => Expression.New(
        s_constructor,
        Expression.Constant(Rows),
        Expression.Constant(StartKind),
        StartOffset?.Quote() ?? Expression.Constant(null, typeof(SqlExpression)),
        Expression.Constant(EndKind),
        EndOffset?.Quote() ?? Expression.Constant(null, typeof(SqlExpression)));
}
