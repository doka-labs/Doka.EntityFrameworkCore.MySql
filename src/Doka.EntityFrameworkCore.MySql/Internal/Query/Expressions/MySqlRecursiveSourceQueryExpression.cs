namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Retains independently preprocessed recursive arms without constructing a cyclic expression graph.</summary>
internal sealed class MySqlRecursiveSourceQueryExpression : Expression
{
    /// <summary>Initializes the immutable dependencies for this operation.</summary>
    public MySqlRecursiveSourceQueryExpression(
        Expression anchor,
        Expression recursiveStep,
        Expression maximumDepth,
        int bindingId
    )
    {
        Anchor = anchor;
        RecursiveStep = recursiveStep;
        MaximumDepth = maximumDepth;
        BindingId = bindingId;
    }

    /// <summary>Gets the completed depth-zero query.</summary>
    public Expression Anchor { get; }

    /// <summary>Gets the step query containing one lexical frontier leaf.</summary>
    public Expression RecursiveStep { get; }

    /// <summary>Gets the integer constant or query parameter supplying the semantic depth.</summary>
    public Expression MaximumDepth { get; }

    /// <summary>Gets the statement-local definition and frontier identity.</summary>
    public int BindingId { get; }

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <inheritdoc />
    public override Type Type => Anchor.Type;

    /// <summary>Copies only changed arms while retaining their lexical identity.</summary>
    public MySqlRecursiveSourceQueryExpression Update(
        Expression updatedAnchor,
        Expression updatedStep,
        Expression updatedMaximumDepth
    ) => ReferenceEquals(updatedAnchor, Anchor)
        && ReferenceEquals(updatedStep, RecursiveStep)
        && ReferenceEquals(updatedMaximumDepth, MaximumDepth)
            ? this
            : new MySqlRecursiveSourceQueryExpression(updatedAnchor, updatedStep, updatedMaximumDepth, BindingId);

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    ) => Update(visitor.Visit(Anchor), visitor.Visit(RecursiveStep), visitor.Visit(MaximumDepth));
}
