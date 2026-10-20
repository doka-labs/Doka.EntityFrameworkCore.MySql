namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// A statement-local source reference with a shared immutable relational definition.
/// </summary>
internal sealed class MySqlMaterializedSourceExpression : TableExpressionBase
{
    private readonly MySqlMaterializedSourceDefinition _definition;

    public MySqlMaterializedSourceExpression(
        string alias,
        int bindingId,
        SelectExpression source,
        IReadOnlyDictionary<string, IAnnotation>? annotations = null
    ) : this(alias, bindingId, new MySqlMaterializedSourceDefinition(source), annotations) { }

    internal MySqlMaterializedSourceExpression(
        string alias,
        int bindingId,
        MySqlMaterializedSourceDefinition definition,
        IReadOnlyDictionary<string, IAnnotation>? annotations
    ) : base(alias, annotations)
    {
        BindingId = bindingId;
        _definition = definition;
    }

    /// <summary>Gets the lexical identity owned by this statement compilation.</summary>
    public int BindingId { get; }

    /// <summary>Gets the shared definition identity used by local DAG reconstruction.</summary>
    internal MySqlMaterializedSourceDefinition Definition => _definition;

    /// <summary>Gets the completed source with its common column schema.</summary>
    public SelectExpression Source => _definition.Source;

    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    )
    {
        var visited = _definition.Visit(visitor);

        return ReferenceEquals(visited, _definition)
            ? this
            : new MySqlMaterializedSourceExpression(Alias!, BindingId, visited, Annotations);
    }

    public override TableExpressionBase Clone(
        string? alias,
        ExpressionVisitor cloningExpressionVisitor
    ) => new MySqlMaterializedSourceExpression(alias ?? Alias!, BindingId, _definition, Annotations);

    public override TableExpressionBase WithAlias(
        string newAlias
    ) => new MySqlMaterializedSourceExpression(newAlias, BindingId, _definition, Annotations);

    protected override TableExpressionBase WithAnnotations(
        IReadOnlyDictionary<string, IAnnotation> annotations
    ) => new MySqlMaterializedSourceExpression(Alias!, BindingId, _definition, annotations);

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override Expression Quote() => MySqlMaterializedSourceQuoting.Quote(this);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append("MaterializedSource(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(") AS ")
        .Append(Alias!);

    public override bool Equals(
        object? obj
    ) => obj is MySqlMaterializedSourceExpression other
        && base.Equals(other)
        && BindingId == other.BindingId
        && ReferenceEquals(_definition, other._definition);

    // WHY: A shared definition has identity. Structural hashing recursively traverses every path
    // through nested references even though SQL emits that definition only once.
    public override int GetHashCode() => HashCode.Combine(
        base.GetHashCode(),
        BindingId,
        RuntimeHelpers.GetHashCode(_definition));
}
