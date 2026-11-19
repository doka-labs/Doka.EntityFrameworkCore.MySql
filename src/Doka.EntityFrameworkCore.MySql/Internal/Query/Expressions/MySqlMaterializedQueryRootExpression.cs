namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Identifies a lexical source without embedding its dependency graph.</summary>
internal interface IMySqlMaterializedQueryRoot
{
    /// <summary>Gets the lexical definition identity within one query compilation.</summary>
    int BindingId { get; }

    /// <summary>Gets the evaluation contract owned by the lexical definition.</summary>
    MySqlSourceBindingKind Kind { get; }

    /// <summary>Gets whether this leaf denotes the previous recursive iteration instead of the completed relation.</summary>
    bool IsRecursiveFrontier { get; }
}

/// <summary>Carries a flat projection reference through ordinary EF navigation processing.</summary>
internal sealed class MySqlMaterializedQueryRootExpression : QueryRootExpression, IMySqlMaterializedQueryRoot
{
    /// <summary>Retains the element type and lexical identity of a flat source.</summary>
    public MySqlMaterializedQueryRootExpression(
        Type elementType,
        int bindingId,
        MySqlSourceBindingKind kind = MySqlSourceBindingKind.Materialized,
        bool isRecursiveFrontier = false
    ) : base(elementType)
    {
        BindingId = bindingId;
        Kind = kind;
        IsRecursiveFrontier = isRecursiveFrontier;
    }

    /// <inheritdoc />
    public int BindingId { get; }

    /// <inheritdoc />
    public MySqlSourceBindingKind Kind { get; }

    /// <inheritdoc />
    public bool IsRecursiveFrontier { get; }

    /// <inheritdoc />
    public override Expression DetachQueryProvider()
        => new MySqlMaterializedQueryRootExpression(ElementType, BindingId, Kind, IsRecursiveFrontier);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append(Kind == MySqlSourceBindingKind.Ordinary
            ? "CommonTableExpression("
            : "MaterializedSource(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(")");

    public override bool Equals(
        object? obj
    ) => obj is MySqlMaterializedQueryRootExpression other
        && base.Equals(other)
        && BindingId == other.BindingId
        && Kind == other.Kind
        && IsRecursiveFrontier == other.IsRecursiveFrontier;

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), BindingId, Kind, IsRecursiveFrontier);
}

/// <summary>Retains the mapped navigation identity of a completed entity source.</summary>
internal sealed class MySqlMaterializedEntityQueryRootExpression : EntityQueryRootExpression,
    IMySqlMaterializedQueryRoot
{
    /// <summary>Retains the detached origin and lexical identity of a mapped source.</summary>
    public MySqlMaterializedEntityQueryRootExpression(
        EntityQueryRootExpression sourceOrigin,
        int bindingId,
        MySqlSourceBindingKind kind = MySqlSourceBindingKind.Materialized,
        bool isRecursiveFrontier = false
    ) : this(
        sourceOrigin,
        bindingId,
        kind,
        isRecursiveFrontier,
        sourceOrigin.QueryProvider is null
            ? sourceOrigin
            : (EntityQueryRootExpression)sourceOrigin.DetachQueryProvider()) { }

    /// <summary>Initializes mapped-root metadata after the source provider has been detached.</summary>
    private MySqlMaterializedEntityQueryRootExpression(
        EntityQueryRootExpression sourceOrigin,
        int bindingId,
        MySqlSourceBindingKind kind,
        bool isRecursiveFrontier,
        EntityQueryRootExpression detachedSourceOrigin
    ) : base(sourceOrigin.EntityType)
    {
        // WHY: Constructor chaining preserves detachment before EF reads the original
        // entity metadata, matching the former primary-constructor initializer order.
        SourceOrigin = detachedSourceOrigin;
        BindingId = bindingId;
        Kind = kind;
        IsRecursiveFrontier = isRecursiveFrontier;
    }

    /// <summary>Gets the detached mapped root semantics retained by this lexical source.</summary>
    public EntityQueryRootExpression SourceOrigin { get; }

    /// <summary>Gets the compilation-local identity of the completed source.</summary>
    public int BindingId { get; }

    /// <inheritdoc />
    public MySqlSourceBindingKind Kind { get; }

    /// <inheritdoc />
    public bool IsRecursiveFrontier { get; }

    /// <inheritdoc />
    public override Expression DetachQueryProvider()
        => new MySqlMaterializedEntityQueryRootExpression(SourceOrigin, BindingId, Kind, IsRecursiveFrontier);

    /// <inheritdoc />
    public override EntityQueryRootExpression UpdateEntityType(
        IEntityType entityType
    ) => new MySqlMaterializedEntityQueryRootExpression(
        SourceOrigin.UpdateEntityType(entityType),
        BindingId,
        Kind,
        IsRecursiveFrontier);

    protected override void Print(
        ExpressionPrinter expressionPrinter
    ) => expressionPrinter
        .Append(Kind == MySqlSourceBindingKind.Ordinary
            ? "CommonTableEntityExpression("
            : "MaterializedEntitySource(")
        .Append(BindingId.ToString(CultureInfo.InvariantCulture))
        .Append(")");

    public override bool Equals(
        object? obj
    ) => obj is MySqlMaterializedEntityQueryRootExpression other
        && base.Equals(other)
        && BindingId == other.BindingId
        && Kind == other.Kind
        && IsRecursiveFrontier == other.IsRecursiveFrontier
        && SourceOrigin.Equals(other.SourceOrigin);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), BindingId, Kind, IsRecursiveFrontier, SourceOrigin);
}
