namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Preserves the insert terminal while EF preprocesses its ordinary typed source projection.</summary>
internal sealed class MySqlInsertSelectQueryExpression : Expression, IPrintableExpression
{
    /// <summary>Creates a normal runtime terminal whose cancellation comes from EF's query context.</summary>
    internal MySqlInsertSelectQueryExpression(
        Expression source,
        IEntityType target,
        IReadOnlyList<IProperty> properties
    ) : this(source, target, properties, cancellationTokenParameter: null) { }

    /// <summary>Creates a compiled terminal carrying only its token parameter's name, never a token source.</summary>
    internal MySqlInsertSelectQueryExpression(
        Expression source,
        IEntityType target,
        IReadOnlyList<IProperty> properties,
        string? cancellationTokenParameter,
        EntityQueryRootExpression? targetRoot = null
    )
    {
        Source = source;
        Target = target;
        Properties = properties;
        CancellationTokenParameter = cancellationTokenParameter;
        TargetRoot = targetRoot;
    }

    /// <summary>Gets the ordinary SELECT expression to process through the existing EF pipeline.</summary>
    internal Expression Source { get; }

    /// <summary>Gets exact target identity without retaining a context or set instance.</summary>
    internal IEntityType Target { get; }

    /// <summary>Gets the immutable positional assignment identities owned by this invocation.</summary>
    internal IReadOnlyList<IProperty> Properties { get; }

    /// <summary>Gets the compiled token parameter, or null for the query context's ordinary token.</summary>
    internal string? CancellationTokenParameter { get; }

    /// <summary>Retains a target root only until EF validates and detaches its query provider.</summary>
    internal EntityQueryRootExpression? TargetRoot { get; }

    /// <inheritdoc />
    public override Type Type => typeof(int);

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <inheritdoc />
    public void Print(
        ExpressionPrinter expressionPrinter
    )
    {
        expressionPrinter
            .Append("InsertSelect(")
            .Append(Target.Name)
            .Append("): ");

        expressionPrinter.Visit(Source);
    }

    /// <summary>Preserves identity when preprocessing leaves the source unchanged.</summary>
    internal MySqlInsertSelectQueryExpression Update(
        Expression source
    ) => ReferenceEquals(source, Source)
        ? this
        : new MySqlInsertSelectQueryExpression(source, Target, Properties, CancellationTokenParameter, TargetRoot);

    /// <inheritdoc />
    protected override Expression VisitChildren(
        ExpressionVisitor visitor
    )
    {
        var source = visitor.Visit(Source);

        // WHY: EF funcletization resets its evaluatability state even for Visit(null).
        // An absent receipt must not erase the real source's non-client state.
        var targetRoot = TargetRoot is null
            ? null
            : (EntityQueryRootExpression)visitor.Visit(TargetRoot);

        return ReferenceEquals(targetRoot, TargetRoot)
            ? Update(source)
            : new MySqlInsertSelectQueryExpression(source, Target, Properties, CancellationTokenParameter, targetRoot);
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is MySqlInsertSelectQueryExpression other
        && ReferenceEquals(Target, other.Target)
        && CancellationTokenParameter == other.CancellationTokenParameter
        && Properties.SequenceEqual(other.Properties)
        && ExpressionEqualityComparer.Instance.Equals(Source, other.Source);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RuntimeHelpers.GetHashCode(Target));
        hash.Add(CancellationTokenParameter, StringComparer.Ordinal);
        hash.Add(ExpressionEqualityComparer.Instance.GetHashCode(Source));

        foreach (var property in Properties)
        {
            hash.Add(RuntimeHelpers.GetHashCode(property));
        }

        return hash.ToHashCode();
    }
}
