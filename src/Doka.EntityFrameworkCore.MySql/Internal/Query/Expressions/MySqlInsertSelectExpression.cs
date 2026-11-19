namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Owns one physical INSERT target and its finalized positional SELECT projection.</summary>
#pragma warning disable EF9100 // Implements the qualified EF11 SQL quotation contract; only Quote is exposed as experimental.
internal sealed class MySqlInsertSelectExpression : Expression, IRelationalQuotableExpression, IPrintableExpression
#pragma warning restore EF9100
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlInsertSelectExpression).GetConstructor(
    [
        typeof(TableExpression), typeof(SelectExpression), typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>),
        typeof(string),
    ])!;

    private static readonly NewArrayExpression s_stringArray =
        (NewArrayExpression)((Expression<Func<string[]>>)(() => new[] { string.Empty })).Body;

    /// <summary>Creates an immutable insert statement without retaining source entities or context instances.</summary>
    public MySqlInsertSelectExpression(
        TableExpression table,
        SelectExpression source,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> tags,
        string? cancellationTokenParameter
    )
    {
        Table = table;
        Source = source;
        Columns = columns;
        Tags = tags;
        CancellationTokenParameter = cancellationTokenParameter;
    }

    /// <summary>Gets the actual physical target table.</summary>
    internal TableExpression Table { get; }

    /// <summary>Gets the complete SELECT, including its original bag, filter and paging scopes.</summary>
    internal SelectExpression Source { get; }

    /// <summary>Gets target column names in the exact source projection order.</summary>
    internal IReadOnlyList<string> Columns { get; }

    /// <summary>Gets diagnostic tags; tags never select write or locking semantics.</summary>
    internal IReadOnlyList<string> Tags { get; }

    /// <summary>Gets the compiled token parameter name without retaining the caller's CancellationTokenSource.</summary>
    internal string? CancellationTokenParameter { get; }

    /// <inheritdoc />
    public override Type Type => typeof(void);

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <inheritdoc />
    public void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter
            .Append("INSERT INTO ")
            .Append(Table.Name)
            .Append(" (");

        expressionPrinter
            .Append(string.Join(", ", Columns))
            .AppendLine(")");

        expressionPrinter.Visit(Source);
    }

    /// <inheritdoc />
    [Experimental("EF9100")]
    public Expression Quote() => New(
        s_constructor,
        Table.Quote(),
        Source.Quote(),

        // WHY: Both arrays have a statically rooted string[] type; quotation never asks
        // the runtime to construct an array for a dynamically discovered element type.
        s_stringArray.Update(Columns.Select(column => Constant(column))),
        s_stringArray.Update(Tags.Select(tag => Constant(tag))),
        Constant(CancellationTokenParameter, typeof(string)));

    /// <summary>Retains the statement identity when a parameter visitor preserves its SELECT.</summary>
    internal MySqlInsertSelectExpression Update(
        SelectExpression source
    ) => ReferenceEquals(source, Source)
        ? this
        : new MySqlInsertSelectExpression(Table, source, Columns, Tags, CancellationTokenParameter);

    /// <summary>Adds the compiler-owned diagnostic tags without mutating a shared SQL tree.</summary>
    internal MySqlInsertSelectExpression ApplyTags(
        ISet<string> tags
    ) => tags.Count == 0
        ? this
        : new MySqlInsertSelectExpression(Table, Source, Columns, tags.ToArray(), CancellationTokenParameter);

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    )
    {
        var source = (SelectExpression)visitor.Visit(Source);
        var table = (TableExpression)visitor.Visit(Table);

        return ReferenceEquals(table, Table) && ReferenceEquals(source, Source)
            ? this
            : new MySqlInsertSelectExpression(table, source, Columns, Tags, CancellationTokenParameter);
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is MySqlInsertSelectExpression other
        && Table.Equals(other.Table)
        && CancellationTokenParameter == other.CancellationTokenParameter

        // WHY: EF11 SelectExpression.Equals omits DISTINCT despite its effect on row multiplicity.
        && Source.IsDistinct == other.Source.IsDistinct
        && Source.Equals(other.Source)
        && Columns.SequenceEqual(other.Columns, StringComparer.Ordinal)
        && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Table.Name, StringComparer.Ordinal);
        hash.Add(Table.Schema, StringComparer.Ordinal);
        hash.Add(Table.Alias, StringComparer.Ordinal);
        hash.Add(CancellationTokenParameter, StringComparer.Ordinal);

        // WHY: EF11 ProjectionExpression hashes object identity despite structural
        // equality, so SelectExpression.GetHashCode changes after quotation. Hash
        // stable primitive shape data instead; full source equality still separates
        // legal collisions without SQL-string allocation or a second SQL-tree comparer.
        hash.Add(Source.Alias, StringComparer.Ordinal);
        hash.Add(Source.IsDistinct);
        hash.Add(Source.Tables.Count);
        hash.Add(Source.Projection.Count);
        hash.Add(Source.GroupBy.Count);
        hash.Add(Source.Orderings.Count);
        hash.Add(Source.Predicate is not null);
        hash.Add(Source.Having is not null);
        hash.Add(Source.Offset is not null);
        hash.Add(Source.Limit is not null);

        // WHY: Projection aliases and CLR shapes are stable through quotation and
        // preserve positional identity without consuming EF's unstable child hashes.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Source.Projection.Count; index++)
        {
            var projection = Source.Projection[index];
            hash.Add(projection.Alias, StringComparer.Ordinal);
            hash.Add(projection.Expression.GetType());
            hash.Add(projection.Expression.Type);
        }

        foreach (var column in Columns)
        {
            hash.Add(column, StringComparer.Ordinal);
        }

        foreach (var tag in Tags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
