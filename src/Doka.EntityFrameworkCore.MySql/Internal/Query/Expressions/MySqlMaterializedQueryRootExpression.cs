namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Identifies a lexical source without embedding its dependency graph.</summary>
internal interface IMySqlMaterializedQueryRoot
{
    /// <summary>Gets the lexical definition identity within one query compilation.</summary>
    int BindingId { get; }
}

/// <summary>Carries a flat projection reference through ordinary EF navigation processing.</summary>
internal sealed class MySqlMaterializedQueryRootExpression(
    Type elementType,
    int bindingId
) : QueryRootExpression(elementType), IMySqlMaterializedQueryRoot
{
    /// <inheritdoc />
    public int BindingId { get; } = bindingId;

    /// <inheritdoc />
    public override Expression DetachQueryProvider() =>
        new MySqlMaterializedQueryRootExpression(ElementType, BindingId);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append("MaterializedSource(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(")");

    public override bool Equals(
        object? obj
    ) => obj is MySqlMaterializedQueryRootExpression other && base.Equals(other) && BindingId == other.BindingId;

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), BindingId);
}

/// <summary>Retains the mapped navigation identity of a completed entity source.</summary>
internal sealed class MySqlMaterializedEntityQueryRootExpression(
    EntityQueryRootExpression sourceOrigin,
    int bindingId
) : EntityQueryRootExpression(sourceOrigin.EntityType), IMySqlMaterializedQueryRoot
{
    /// <summary>Gets the detached mapped root semantics retained by this lexical source.</summary>
    public EntityQueryRootExpression SourceOrigin { get; } = sourceOrigin.QueryProvider is null
        ? sourceOrigin
        : (EntityQueryRootExpression)sourceOrigin.DetachQueryProvider();

    /// <summary>Gets the compilation-local identity of the completed source.</summary>
    public int BindingId { get; } = bindingId;

    /// <inheritdoc />
    public override Expression DetachQueryProvider() =>
        new MySqlMaterializedEntityQueryRootExpression(SourceOrigin, BindingId);

    /// <inheritdoc />
    public override EntityQueryRootExpression UpdateEntityType(
        IEntityType entityType
    ) => new MySqlMaterializedEntityQueryRootExpression(SourceOrigin.UpdateEntityType(entityType), BindingId);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append("MaterializedEntitySource(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(")");

    public override bool Equals(
        object? obj
    ) => obj is MySqlMaterializedEntityQueryRootExpression other
        && base.Equals(other)
        && BindingId == other.BindingId
        && SourceOrigin.Equals(other.SourceOrigin);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), BindingId, SourceOrigin);
}
