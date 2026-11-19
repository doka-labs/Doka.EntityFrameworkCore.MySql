namespace Doka.EntityFrameworkCore.MySql;

/// <summary>A finite previous-iteration leaf; it never embeds its owning recursive definition.</summary>
internal sealed class MySqlRecursiveCteReferenceExpression : TableExpressionBase
{
    private static readonly ConstructorInfo s_constructor = typeof(MySqlRecursiveCteReferenceExpression).GetConstructor(
    [
        typeof(string),
        typeof(int),
        typeof(SqlExpression),
        typeof(IReadOnlyDictionary<string, IAnnotation>),
    ])!;

    /// <summary>Initializes the lexical frontier and the actual SQL depth parameter identity.</summary>
    public MySqlRecursiveCteReferenceExpression(
        string alias,
        int bindingId,
        SqlExpression maximumDepth,
        IReadOnlyDictionary<string, IAnnotation>? annotations = null
    ) : base(alias, annotations)
    {
        ArgumentNullException.ThrowIfNull(maximumDepth);

        if (maximumDepth.Type != typeof(int)
            || maximumDepth is not (SqlConstantExpression { Value: int } or SqlParameterExpression))
        {
            throw new InvalidOperationException("Recursive CTE depth requires an integer constant or query parameter.");
        }

        if (maximumDepth is SqlConstantExpression { Value: int value })
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(maximumDepth));
        }

        BindingId = bindingId;
        MaximumDepth = maximumDepth;
    }

    /// <summary>Gets the statement-local recursive definition identity.</summary>
    public int BindingId { get; }

    /// <summary>Gets the integer semantic limit with its actual command parameter name.</summary>
    public SqlExpression MaximumDepth { get; }

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    )
    {
        var visited = (SqlExpression)visitor.Visit(MaximumDepth);

        return ReferenceEquals(visited, MaximumDepth)
            ? this
            : new MySqlRecursiveCteReferenceExpression(Alias!, BindingId, visited, Annotations);
    }

    /// <inheritdoc />
    public override TableExpressionBase Clone(
        string? alias,
        ExpressionVisitor cloningExpressionVisitor
    ) => new MySqlRecursiveCteReferenceExpression(alias ?? Alias!,
        BindingId,
        (SqlExpression)cloningExpressionVisitor.Visit(MaximumDepth),
        Annotations);

    /// <inheritdoc />
    public override TableExpressionBase WithAlias(
        string newAlias
    ) => new MySqlRecursiveCteReferenceExpression(newAlias, BindingId, MaximumDepth, Annotations);

    /// <inheritdoc />
    protected override TableExpressionBase WithAnnotations(
        IReadOnlyDictionary<string, IAnnotation> annotations
    ) => new MySqlRecursiveCteReferenceExpression(Alias!, BindingId, MaximumDepth, annotations);

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote() => New(
        s_constructor,
        Constant(Alias),
        Constant(BindingId),
        MaximumDepth.Quote(),
        MySqlRelationalExpressionQuoting.QuoteAnnotations(Annotations));

    /// <inheritdoc />
    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append("RecursiveFrontier(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(") AS ")
        .Append(Alias!);

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is MySqlRecursiveCteReferenceExpression other
        && base.Equals(other)
        && BindingId == other.BindingId
        && MaximumDepth.Equals(other.MaximumDepth);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), BindingId, MaximumDepth);
}
